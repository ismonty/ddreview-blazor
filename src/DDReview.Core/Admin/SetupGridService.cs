using System.Data;
using System.Text.Json;
using DDReview.Core.Gl;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Admin;

public sealed record SetupColumnInfo(SetupColumn Def, SetupDbColumn Db)
{
    public bool Required => Db.Required;
    public bool Nullable => Db.Nullable;
}

public sealed record SetupRow(int Key, IReadOnlyDictionary<string, object?> Values);
public sealed record SetupLookupItem(int Value, string Text, int? Parent = null);

public sealed record SetupGridData(SetupTable Table, IReadOnlyList<SetupColumnInfo> Columns, IReadOnlyList<SetupRow> Rows,
    IReadOnlyDictionary<string, IReadOnlyList<SetupLookupItem>> Lookups);

public enum SetupOutcome { Saved, Unchanged, Conflict, NotFound, Invalid, Refused }
public sealed record SetupWrite(SetupOutcome Outcome, string? Message = null, int? Key = null)
{
    public bool Ok => Outcome is SetupOutcome.Saved or SetupOutcome.Unchanged;
}

/// <summary>
/// Reads and changes the Setup tables for the pages. Every call checks the Setup rules itself (administrator or granted area, Google session,
/// Google confirmed in the last 30 minutes): the page being shown is never the check. Writes are one transaction with their audit row.
/// </summary>
public sealed class SetupGridService
{
    private const int ForeignKeyViolation = 547, DuplicateKey = 2627, DuplicateIndex = 2601, NullInsert = 515, Truncation = 8152;

    public static void Require(SetupActor actor, SetupDecision decision, DateTimeOffset now, SetupArea area)
    {
        if (!decision.Allows(area) || !actor.GoogleSession || !SetupPolicy.IsFresh(actor.VerifiedAt, now))
            throw new UnauthorizedAccessException("Setup needs an administrator (or someone allowed this part) signed in with Google and confirmed in the last 30 minutes.");
    }

    /// <summary>Every active store (administrators see all of them), named the way the other pages name them.</summary>
    public async Task<IReadOnlyList<StoreInfo>> StoresAsync(string cs, SetupActor actor, SetupDecision decision, DateTimeOffset now, SetupTable table, CancellationToken ct = default)
    {
        Require(actor, decision, now, table.Area);
        return await StoreLookup.LoadAllActiveAsync(cs, ct);
    }

    /// <summary>The rows of one table (for one store when it is per-store). Null when the table is not in this database.</summary>
    public async Task<SetupGridData?> LoadAsync(string cs, SetupActor actor, SetupDecision decision, DateTimeOffset now, SetupTable table, int? storeId, CancellationToken ct = default)
    {
        Require(actor, decision, now, table.Area);
        if (table.StoreColumn is not null && storeId is null or <= 0) return await EmptyAsync(cs, table, ct);

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        var cols = await ColumnsAsync(cn, null, table, ct);
        if (cols is null) return null;

        var rows = new List<SetupRow>();
        await using (var cmd = new SqlCommand(SetupSql.Select(table, cols.Select(c => c.Def.Name).ToList()), cn) { CommandTimeout = 60 })
        {
            Bind(cmd, table, storeId, null);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var values = new Dictionary<string, object?>();
                foreach (var c in cols) { var v = r[c.Def.Name]; values[c.Def.Name] = v is DBNull ? null : v; }
                rows.Add(new SetupRow(Convert.ToInt32(r[table.Key]), values));
            }
        }
        return new SetupGridData(table, cols, rows, await LookupsAsync(cn, null, cols, storeId, ct));
    }

    private async Task<SetupGridData?> EmptyAsync(string cs, SetupTable table, CancellationToken ct)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        var cols = await ColumnsAsync(cn, null, table, ct);
        return cols is null ? null : new SetupGridData(table, cols, [], await LookupsAsync(cn, null, cols, null, ct));
    }

    /// <param name="edits">What was typed, by column name. Blank means "none" (or the database default when adding).</param>
    public async Task<SetupWrite> InsertAsync(string cs, SetupActor actor, SetupDecision decision, DateTimeOffset now, SetupTable table, int? storeId,
        IReadOnlyDictionary<string, string?> edits, CancellationToken ct = default)
    {
        Require(actor, decision, now, table.Area);
        if (!table.AllowAdd) return new SetupWrite(SetupOutcome.Refused, "Rows cannot be added here.");
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        try
        {
            var cols = await ColumnsAsync(cn, tx, table, ct);
            if (cols is null) return Fail(tx, SetupOutcome.NotFound, "That table is not in this database.");
            if (await CheckStoreAsync(cn, tx, table, storeId, ct) is { } storeProblem) return Fail(tx, SetupOutcome.Invalid, storeProblem);
            var lookups = await LookupsAsync(cn, tx, cols, storeId, ct);

            var names = new List<string>(); var values = new Dictionary<string, object?>(); var typed = new Dictionary<string, SetupDbColumn>();
            foreach (var c in cols.Where(c => !c.Db.Identity && !c.Def.ReadOnly))
            {
                edits.TryGetValue(c.Def.Name, out var text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    if (c.Required) return Fail(tx, SetupOutcome.Invalid, $"{c.Def.Label} is required.");
                    continue;
                }
                if (!SetupValue.TryParse(c.Def, c.Db, text, out var v, out var err)) return Fail(tx, SetupOutcome.Invalid, err);
                if (LookupProblem(c, v, lookups) is { } lp) return Fail(tx, SetupOutcome.Invalid, lp);
                names.Add(c.Def.Name); values[c.Def.Name] = v; typed[c.Def.Name] = c.Db;
            }

            if (ExclusiveProblem(table, n => values.TryGetValue(n, out var v) ? v is true
                    : cols.FirstOrDefault(c => c.Def.Name == n) is { } ci && SetupValue.FromDefault(ci.Def, ci.Db) == "true") is { } ex) return Fail(tx, SetupOutcome.Invalid, ex);

            await using var cmd = new SqlCommand(SetupSql.Insert(table, FixedColumns(table, names)), cn, tx) { CommandTimeout = 60 };
            // the store and the fixed filter value are ordinary columns of the new row, filled by the page, never typed
            if (table.StoreColumn is not null) cmd.Parameters.Add("@v_" + table.StoreColumn, SqlDbType.Int).Value = storeId!.Value;
            if (table.FilterColumn is not null) cmd.Parameters.Add("@v_" + table.FilterColumn, SqlDbType.Bit).Value = table.FilterValue;
            foreach (var n in names) cmd.Parameters.Add(Param("@v_" + n, typed[n], values[n]));
            var key = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));

            var shown = names.ToDictionary(n => n, n => SetupValue.Format(cols.First(c => c.Def.Name == n).Def, values[n]));
            await SetupAudit.WriteAsync(cn, tx, actor, table.Area, table.Table, key.ToString(), "insert", null, Json(Scope(table, storeId, shown)), ct);
            await tx.CommitAsync(ct);
            return new SetupWrite(SetupOutcome.Saved, null, key);
        }
        catch (SqlException ex) { return Fail(tx, SetupOutcome.Refused, Describe(ex)); }
        catch { await SafeRollbackAsync(tx); throw; }
    }

    /// <param name="original">The row as it was loaded; only fields that differ from it are written, and each must still hold its loaded value.</param>
    public async Task<SetupWrite> UpdateAsync(string cs, SetupActor actor, SetupDecision decision, DateTimeOffset now, SetupTable table, int? storeId,
        SetupRow original, IReadOnlyDictionary<string, string?> edits, CancellationToken ct = default)
    {
        Require(actor, decision, now, table.Area);
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        try
        {
            var cols = await ColumnsAsync(cn, tx, table, ct);
            if (cols is null) return Fail(tx, SetupOutcome.NotFound, "That table is not in this database.");
            if (await CheckStoreAsync(cn, tx, table, storeId, ct) is { } storeProblem) return Fail(tx, SetupOutcome.Invalid, storeProblem);
            var lookups = await LookupsAsync(cn, tx, cols, storeId, ct);

            var changed = new List<SetupColumnInfo>(); var newValues = new Dictionary<string, object?>();
            foreach (var c in cols.Where(c => !c.Db.Identity && !c.Def.ReadOnly && edits.ContainsKey(c.Def.Name)))
            {
                var text = edits[c.Def.Name];
                original.Values.TryGetValue(c.Def.Name, out var was);
                object? newValue = null;
                if (string.IsNullOrWhiteSpace(text))
                {
                    if (!c.Nullable) return Fail(tx, SetupOutcome.Invalid, $"{c.Def.Label} cannot be left empty.");
                }
                else
                {
                    if (!SetupValue.TryParse(c.Def, c.Db, text, out newValue, out var err)) return Fail(tx, SetupOutcome.Invalid, err);
                }
                if (SetupValue.Format(c.Def, newValue) == SetupValue.Format(c.Def, was)) continue;
                // only a value that is really being changed has to be on the list (an old row may hold one that no longer is)
                if (LookupProblem(c, newValue, lookups) is { } lp) return Fail(tx, SetupOutcome.Invalid, lp);
                changed.Add(c); newValues[c.Def.Name] = newValue;
            }
            if (changed.Count == 0) { await tx.RollbackAsync(ct); return new SetupWrite(SetupOutcome.Unchanged, "Nothing was changed.", original.Key); }

            if (ExclusiveProblem(table, n => newValues.TryGetValue(n, out var v) ? v is true : original.Values.GetValueOrDefault(n) is true) is { } ex) return Fail(tx, SetupOutcome.Invalid, ex);

            await using var cmd = new SqlCommand(SetupSql.Update(table, changed.Select(c => c.Def.Name).ToList()), cn, tx) { CommandTimeout = 60 };
            Bind(cmd, table, storeId, original.Key);
            foreach (var c in changed)
            {
                cmd.Parameters.Add(Param("@v_" + c.Def.Name, c.Db, newValues[c.Def.Name]));
                cmd.Parameters.Add(Param("@o_" + c.Def.Name, c.Db, original.Values.GetValueOrDefault(c.Def.Name)));
            }
            if (await cmd.ExecuteNonQueryAsync(ct) == 0)
            {
                // gone, or changed by someone else since this page loaded it
                var exists = await ExistsAsync(cn, tx, table, storeId, original.Key, ct);
                return Fail(tx, exists ? SetupOutcome.Conflict : SetupOutcome.NotFound,
                    exists ? "Someone else changed this row since you opened it. Reload and try again." : "That row no longer exists. Reload the page.");
            }
            var old = changed.ToDictionary(c => c.Def.Name, c => SetupValue.Format(c.Def, original.Values.GetValueOrDefault(c.Def.Name)));
            var neu = changed.ToDictionary(c => c.Def.Name, c => SetupValue.Format(c.Def, newValues[c.Def.Name]));
            await SetupAudit.WriteAsync(cn, tx, actor, table.Area, table.Table, original.Key.ToString(), "update", Json(Scope(table, storeId, old)), Json(Scope(table, storeId, neu)), ct);
            await tx.CommitAsync(ct);
            return new SetupWrite(SetupOutcome.Saved, null, original.Key);
        }
        catch (SqlException ex) { return Fail(tx, SetupOutcome.Refused, Describe(ex)); }
        catch { await SafeRollbackAsync(tx); throw; }
    }

    public async Task<SetupWrite> DeleteAsync(string cs, SetupActor actor, SetupDecision decision, DateTimeOffset now, SetupTable table, int? storeId,
        int key, CancellationToken ct = default)
    {
        Require(actor, decision, now, table.Area);
        if (!table.AllowDelete) return new SetupWrite(SetupOutcome.Refused, "Rows cannot be removed here.");
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        try
        {
            var cols = await ColumnsAsync(cn, tx, table, ct);
            if (cols is null) return Fail(tx, SetupOutcome.NotFound, "That table is not in this database.");
            if (await CheckStoreAsync(cn, tx, table, storeId, ct) is { } storeProblem) return Fail(tx, SetupOutcome.Invalid, storeProblem);

            // read the row first (locked) so the audit trail keeps what was removed
            Dictionary<string, string>? gone = null;
            await using (var read = new SqlCommand(SetupSql.Select(table, cols.Select(c => c.Def.Name).ToList(), lockRow: true, byKey: true), cn, tx))
            {
                Bind(read, table, storeId, key);
                await using var r = await read.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                    gone = cols.ToDictionary(c => c.Def.Name, c => SetupValue.Format(c.Def, r[c.Def.Name] is DBNull ? null : r[c.Def.Name]));
            }
            if (gone is null) return Fail(tx, SetupOutcome.NotFound, "That row no longer exists. Reload the page.");

            await using var cmd = new SqlCommand(SetupSql.Delete(table), cn, tx) { CommandTimeout = 60 };
            Bind(cmd, table, storeId, key);
            await cmd.ExecuteNonQueryAsync(ct);
            await SetupAudit.WriteAsync(cn, tx, actor, table.Area, table.Table, key.ToString(), "delete", Json(Scope(table, storeId, gone)), null, ct);
            await tx.CommitAsync(ct);
            return new SetupWrite(SetupOutcome.Saved, null, key);
        }
        catch (SqlException ex) { return Fail(tx, SetupOutcome.Refused, Describe(ex)); }
        catch { await SafeRollbackAsync(tx); throw; }
    }

    // ---- helpers ----

    /// <summary>The row as it would be stored must not have the leader on together with one of the columns it excludes.</summary>
    internal static string? ExclusiveProblem(SetupTable t, Func<string, bool> isOn)
    {
        if (t.Exclusive is not { } x || !isOn(x.Leader)) return null;
        var clash = t.Columns.Where(c => x.Others.Contains(c.Name) && isOn(c.Name)).Select(c => c.Label).ToList();
        var lead = t.Columns.First(c => c.Name == x.Leader).Label;
        return clash.Count == 0 ? null : $"\"{lead}\" cannot be on together with {string.Join(", ", clash)}. Turn one side off.";
    }

    /// <summary>Store and filter columns are set by the page, never typed: added to every insert.</summary>
    private static IReadOnlyList<string> FixedColumns(SetupTable t, List<string> typed)
    {
        var all = new List<string>(typed);
        if (t.StoreColumn is not null && !all.Contains(t.StoreColumn)) all.Add(t.StoreColumn);
        if (t.FilterColumn is not null && !all.Contains(t.FilterColumn)) all.Add(t.FilterColumn);
        return all;
    }

    /// <summary>Adds the page's own parameters (@store, @filter, @key) to the ones the statement actually uses.</summary>
    private static void Bind(SqlCommand cmd, SetupTable t, int? storeId, int? key)
    {
        if (t.StoreColumn is not null && cmd.CommandText.Contains("@store", StringComparison.Ordinal)) cmd.Parameters.Add("@store", SqlDbType.Int).Value = storeId ?? 0;
        if (t.FilterColumn is not null && cmd.CommandText.Contains("@filter", StringComparison.Ordinal)) cmd.Parameters.Add("@filter", SqlDbType.Bit).Value = t.FilterValue;
        if (key is not null && cmd.CommandText.Contains("@key", StringComparison.Ordinal)) cmd.Parameters.Add("@key", SqlDbType.Int).Value = key.Value;
    }

    internal static SqlParameter Param(string name, SetupDbColumn db, object? value)
    {
        var p = new SqlParameter { ParameterName = name };
        switch (db.Type)
        {
            case "int": p.SqlDbType = SqlDbType.Int; value = value is null ? null : Convert.ToInt32(value); break;
            case "smallint": p.SqlDbType = SqlDbType.SmallInt; value = value is null ? null : Convert.ToInt16(value); break;
            case "tinyint": p.SqlDbType = SqlDbType.TinyInt; value = value is null ? null : Convert.ToByte(value); break;
            case "bigint": p.SqlDbType = SqlDbType.BigInt; value = value is null ? null : Convert.ToInt64(value); break;
            case "bit": p.SqlDbType = SqlDbType.Bit; break;
            case "money": p.SqlDbType = SqlDbType.Money; break;
            case "smallmoney": p.SqlDbType = SqlDbType.SmallMoney; break;
            case "decimal" or "numeric": p.SqlDbType = SqlDbType.Decimal; p.Precision = (byte)db.Precision; p.Scale = (byte)db.Scale; break;
            case "datetime": p.SqlDbType = SqlDbType.DateTime; break;
            case "smalldatetime": p.SqlDbType = SqlDbType.SmallDateTime; break;
            case "datetime2": p.SqlDbType = SqlDbType.DateTime2; break;
            case "date": p.SqlDbType = SqlDbType.Date; break;
            case "varchar" or "char": p.SqlDbType = SqlDbType.VarChar; p.Size = db.MaxLength > 0 ? db.MaxLength : -1; break;
            case "nvarchar" or "nchar": p.SqlDbType = SqlDbType.NVarChar; p.Size = db.MaxLength > 0 ? db.MaxLength : -1; break;
            default: throw new NotSupportedException("Column type " + db.Type);
        }
        p.Value = value is null ? DBNull.Value : value;
        return p;
    }

    private static async Task<IReadOnlyList<SetupColumnInfo>?> ColumnsAsync(SqlConnection cn, SqlTransaction? tx, SetupTable table, CancellationToken ct)
    {
        var found = new Dictionary<string, SetupDbColumn>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand(@"
select c.name, t.name, c.is_nullable, c.is_identity, cast(case when c.default_object_id <> 0 then 1 else 0 end as bit), c.max_length, c.precision, c.scale, dc.definition
from sys.columns c join sys.types t on t.user_type_id = c.user_type_id
left join sys.default_constraints dc on dc.object_id = c.default_object_id
where c.object_id = object_id(@t)", cn, tx))
        {
            cmd.Parameters.Add("@t", SqlDbType.NVarChar, 300).Value = "dbo." + SetupSql.Q(table.Table);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var type = r.GetString(1); var len = (int)r.GetInt16(5);
                if (type is "nvarchar" or "nchar" && len > 0) len /= 2;
                found[r.GetString(0)] = new SetupDbColumn(r.GetString(0), type, r.GetBoolean(2), r.GetBoolean(3), r.GetBoolean(4), len, r.GetByte(6), r.GetByte(7), r.IsDBNull(8) ? null : r.GetString(8));
            }
        }
        if (found.Count == 0 || !found.TryGetValue(table.Key, out var key) || (!key.Identity && table.AllowAdd)) return null;     // rows are added only to tables whose key the database numbers
        // older databases lack some columns, and the page's own store/filter columns are never offered as fields
        var list = new List<SetupColumnInfo>();
        foreach (var c in table.Columns)
            if (found.TryGetValue(c.Name, out var db) && db.Supported && !db.Identity && c.Name != table.StoreColumn && c.Name != table.FilterColumn)
                list.Add(new SetupColumnInfo(c, db));
        if (table.StoreColumn is not null && !found.ContainsKey(table.StoreColumn)) return null;
        if (table.FilterColumn is not null && !found.ContainsKey(table.FilterColumn)) return null;
        return list;
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<SetupLookupItem>>> LookupsAsync(SqlConnection cn, SqlTransaction? tx, IReadOnlyList<SetupColumnInfo> cols, int? storeId, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<SetupLookupItem>>();
        foreach (var name in cols.Select(c => c.Def.Lookup).Where(n => n is not null).Distinct())
        {
            var sql = SetupLookups.Sql(name!);
            if (sql is null) continue;
            var items = new List<SetupLookupItem>();
            try
            {
                await using var cmd = new SqlCommand(sql, cn, tx);
                if (sql.Contains("@store", StringComparison.Ordinal)) cmd.Parameters.Add("@store", SqlDbType.Int).Value = storeId ?? 0;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    items.Add(new SetupLookupItem(Convert.ToInt32(r.GetValue(0)), r.IsDBNull(1) ? "" : r.GetString(1).Trim(),
                        r.FieldCount > 2 && !r.IsDBNull(2) ? Convert.ToInt32(r.GetValue(2)) : null));
            }
            catch (SqlException) { /* an older database without the lookup table: the list stays empty */ }
            result[name!] = items;
        }
        return result;
    }

    private static string? LookupProblem(SetupColumnInfo c, object? value, IReadOnlyDictionary<string, IReadOnlyList<SetupLookupItem>> lookups)
    {
        if (c.Def.Lookup is null || value is null) return null;
        return lookups.TryGetValue(c.Def.Lookup, out var items) && items.Any(i => i.Value == Convert.ToInt32(value)) ? null : $"{c.Def.Label}: choose one from the list.";
    }

    private static async Task<string?> CheckStoreAsync(SqlConnection cn, SqlTransaction tx, SetupTable table, int? storeId, CancellationToken ct)
    {
        if (table.StoreColumn is null) return null;
        if (storeId is null or <= 0) return "Choose a store first.";
        await using var cmd = new SqlCommand("select count(*) from dbo.tblStores where StoreID = @s and Active > 0", cn, tx);
        cmd.Parameters.Add("@s", SqlDbType.Int).Value = storeId.Value;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 0 ? "That is not an active store." : null;
    }

    private static async Task<bool> ExistsAsync(SqlConnection cn, SqlTransaction tx, SetupTable table, int? storeId, int key, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SetupSql.Select(table, [], byKey: true), cn, tx);
        Bind(cmd, table, storeId, key);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct);
    }

    private static SetupWrite Fail(SqlTransaction tx, SetupOutcome outcome, string? message)
    {
        try { tx.Rollback(); } catch (InvalidOperationException) { /* already finished */ }
        return new SetupWrite(outcome, message);
    }

    private static async Task SafeRollbackAsync(SqlTransaction tx)
    {
        try { await tx.RollbackAsync(); } catch (InvalidOperationException) { }
    }

    private static string Describe(SqlException ex) => ex.Number switch
    {
        ForeignKeyViolation => "Something else still uses this row (or the value points at something that does not exist), so the database refused the change.",
        DuplicateKey or DuplicateIndex => "A row with those values already exists.",
        NullInsert => "A required value is missing.",
        Truncation => "A value is too long for its field.",
        _ => "The database refused the change: " + ex.Message,
    };

    private static Dictionary<string, string> Scope(SetupTable t, int? storeId, Dictionary<string, string> values)
    {
        var d = new Dictionary<string, string>(values);
        if (t.StoreColumn is not null && storeId is not null) d[t.StoreColumn] = storeId.Value.ToString();
        if (t.FilterColumn is not null) d[t.FilterColumn] = t.FilterValue ? "true" : "false";
        return d;
    }

    private static string Json(Dictionary<string, string> d) => JsonSerializer.Serialize(d);
}
