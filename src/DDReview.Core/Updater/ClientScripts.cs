using System.Reflection;

namespace DDReview.Core.Updater;

/// <summary>
/// One change DD Review needs in a CLIENT database. The script itself is a file in /sql (embedded here, so there is one copy),
/// and <see cref="CheckSql"/> says whether it is already in place. Scripts add objects; nothing is removed. The one exception, 011, adds a step to an existing function.
/// </summary>
public sealed record ClientScript(string Id, string Title, string ResourceName, string CheckSql, string? GrantHint = null)
{
    /// <summary>The script text, as written in /sql.</summary>
    public string ReadSql()
    {
        using var s = typeof(ClientScript).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded script missing: {ResourceName}");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}

public static class ClientScripts
{
    /// <summary>In the order they must run. To add one: put the .sql file in /sql, embed it in DDReview.Core.csproj, and list it here.</summary>
    public static IReadOnlyList<ClientScript> All { get; } =
    [
        new("002", "Drill-down filter table type (dbo.DrillPair)", "ClientScripts/002_client_drill_pair_type.sql",
            // columns: Present (the object exists), Usable (this login may use it)
            @"select cast(case when type_id('dbo.DrillPair') is null then 0 else 1 end as int) as Present,
                     cast(case when type_id('dbo.DrillPair') is not null
                               and has_perms_by_name('dbo.DrillPair', 'TYPE', 'EXECUTE') = 1 then 1 else 0 end as int) as Usable",
            "exec dbo.usp_DDReviewGrantAccess @UserName = N'{user}', @Execute = 1;  "),
        // Present is false when an OLDER copy of the procedure is there (it does not yet mention the Setup tables), so the updater re-creates it.
        // If this login may not read the procedure's text, object_definition is null and the copy is taken as current.
        new("004", "Grant procedure (dbo.usp_DDReviewGrantAccess): every permission the web login needs, in one place", "ClientScripts/004_client_grant_procedure.sql",
            @"select cast(case when object_id('dbo.usp_DDReviewGrantAccess', 'P') is null then 0
                               when object_definition(object_id('dbo.usp_DDReviewGrantAccess', 'P')) is not null
                                and (charindex('tblDDReviewAdminAccess', object_definition(object_id('dbo.usp_DDReviewGrantAccess', 'P'))) = 0
                                 or charindex('usp_DDReviewRequestBatchResubmission', object_definition(object_id('dbo.usp_DDReviewGrantAccess', 'P'))) = 0) then 0
                               else 1 end as int) as Present,
                     cast(case when object_id('dbo.usp_DDReviewGrantAccess', 'P') is not null then 1 else 0 end as int) as Usable"),
        new("007", "Setup pages: who may use them (dbo.tblDDReviewAdminAccess) and the change audit trail (dbo.tblDDReviewAdminAudit)", "ClientScripts/007_client_admin_access.sql",
            @"select cast(case when object_id('dbo.tblDDReviewAdminAccess', 'U') is not null and object_id('dbo.tblDDReviewAdminAudit', 'U') is not null then 1 else 0 end as int) as Present,
                     cast(case when has_perms_by_name('dbo.tblDDReviewAdminAccess', 'OBJECT', 'SELECT') = 1
                                and has_perms_by_name('dbo.tblDDReviewAdminAccess', 'OBJECT', 'INSERT') = 1
                                and has_perms_by_name('dbo.tblDDReviewAdminAccess', 'OBJECT', 'UPDATE') = 1
                                and has_perms_by_name('dbo.tblDDReviewAdminAudit', 'OBJECT', 'SELECT') = 1
                                and has_perms_by_name('dbo.tblDDReviewAdminAudit', 'OBJECT', 'INSERT') = 1 then 1 else 0 end as int) as Usable",
            "exec dbo.usp_DDReviewGrantAccess @UserName = N'{user}', @Execute = 1;  "),
        new("010", "General Ledger Review: Request Batch Resubmission (dbo.usp_DDReviewRequestBatchResubmission)", "ClientScripts/010_client_batch_resubmission.sql",
            @"select cast(case when object_id('dbo.usp_DDReviewRequestBatchResubmission', 'P') is not null then 1 else 0 end as int) as Present,
                     cast(case when has_perms_by_name('dbo.usp_DDReviewRequestBatchResubmission', 'OBJECT', 'EXECUTE') = 1 then 1 else 0 end as int) as Usable",
            "exec dbo.usp_DDReviewGrantAccess @UserName = N'{user}', @Execute = 1;  "),
        // The one script that AMENDS something that exists: it adds a block to this database's own copy of the GL function (see the script's header).
        // A database without that function, or where its text cannot be read, counts as nothing to do.
        new("011", "General Ledger: an Adjust GL line keeps its own QuickBooks type (adds one step to fn_CashAndSalesGLandARMultiStoreSelect)", "ClientScripts/011_client_adjust_gl_quickbooks_type.sql",
            @"select cast(case when object_id('dbo.fn_CashAndSalesGLandARMultiStoreSelect', 'TF') is null then 1
                               when object_definition(object_id('dbo.fn_CashAndSalesGLandARMultiStoreSelect', 'TF')) is null then 1
                               when charindex('-- PATCH: an Adjust GL line carries its own QuickBooks type', object_definition(object_id('dbo.fn_CashAndSalesGLandARMultiStoreSelect', 'TF'))) > 0 then 1
                               else 0 end as int) as Present,
                     cast(1 as int) as Usable"),
        // Adds a NEW function beside the old one (the old one stays as the fallback); the app calls the new one when it is there.
        new("012", "General Ledger: set-based function dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor (no cursors; the old function is untouched)", "ClientScripts/012_client_gl_blazor_function.sql",
            @"select cast(case when object_id('dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor', 'IF') is not null then 1 else 0 end as int) as Present,
                     cast(1 as int) as Usable"),
        // Stage 2: the sales-mapping step as one query, and a COPY of this database's per-store function that uses it. Needs 012. Nothing existing is changed except the Blazor function from 012.
        new("013", "General Ledger: set-based sales mapping (dbo.fn_CashAndSalesMappedRecordsBlazor and a copy of the per-store function that uses it)", "ClientScripts/013_client_gl_blazor_mapping.sql",
            @"select cast(case when object_id('dbo.fn_CashAndSalesGLandARBlazor', 'TF') is null then 0
                               when object_id('dbo.fn_CashAndSalesMappedRecordsBlazor', 'IF') is null then 0
                               when object_id('dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor', 'IF') is null then 0
                               when charindex('dbo.fn_CashAndSalesGLandARBlazor(', object_definition(object_id('dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor', 'IF'))) > 0 then 1
                               else 0 end as int) as Present,
                     cast(1 as int) as Usable"),
        // Stage 3: rebuilds the copy from 013 with the per-batch update loop (the profile's one expensive statement) as a single UPDATE. Needs 012 and 013.
        new("014", "General Ledger: batch totals as one update (rebuilds the copy dbo.fn_CashAndSalesGLandARBlazor; the original function is untouched)", "ClientScripts/014_client_gl_blazor_batch_totals.sql",
            @"select cast(case when object_id('dbo.fn_CashAndSalesGLandARBlazor', 'TF') is null then 0
                               when charindex('-- PATCH: batch totals set-based', object_definition(object_id('dbo.fn_CashAndSalesGLandARBlazor', 'TF'))) > 0 then 1
                               else 0 end as int) as Present,
                     cast(1 as int) as Usable"),
    ];
}

public static class SqlBatches
{
    /// <summary>Splits a script on lines that contain only GO (SSMS style); comments and blank batches are dropped.</summary>
    public static IReadOnlyList<string> Split(string script)
    {
        var batches = new List<string>();
        var cur = new System.Text.StringBuilder();
        void Flush()
        {
            var text = cur.ToString().Trim();
            cur.Clear();
            if (HasStatement(text)) batches.Add(text);
        }
        foreach (var line in script.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase)) Flush();
            else cur.AppendLine(line);
        }
        Flush();
        return batches;
    }

    private static bool HasStatement(string text) =>
        text.Split('\n').Any(l => { var t = l.Trim(); return t.Length > 0 && !t.StartsWith("--"); });
}
