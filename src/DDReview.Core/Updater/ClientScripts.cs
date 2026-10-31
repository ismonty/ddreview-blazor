using System.Reflection;

namespace DDReview.Core.Updater;

/// <summary>
/// One change DD Review needs in a CLIENT database. The script itself is a file in /sql (embedded here, so there is one copy),
/// and <see cref="CheckSql"/> says whether it is already in place. Scripts only ADD objects; nothing existing is altered.
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
        new("004", "Grant procedure (dbo.usp_DDReviewGrantAccess): every permission the web login needs, in one place", "ClientScripts/004_client_grant_procedure.sql",
            @"select cast(case when object_id('dbo.usp_DDReviewGrantAccess', 'P') is null then 0 else 1 end as int) as Present,
                     cast(case when object_id('dbo.usp_DDReviewGrantAccess', 'P') is not null then 1 else 0 end as int) as Usable"),
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
