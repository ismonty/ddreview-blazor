namespace DDReview.Core.Tenancy;

/// <summary>One database and the logins under it, as the pick list and the admin page show them.</summary>
public sealed record DatabaseGroup(int? DatabaseId, string Label, IReadOnlyList<LoginChoice> Logins);

public static class DatabaseGroups
{
    /// <summary>
    /// Groups logins under their database, databases in short-name order. A database with no logins is left out unless <paramref name="keepEmpty"/> is set
    /// (the admin page keeps them so they can be seen). Two databases with the same short name get their ID added so they can be told apart.
    /// Logins that point at no database (or one that is not in the table) go last, under "Other logins".
    /// </summary>
    public static IReadOnlyList<DatabaseGroup> Build(IReadOnlyList<DatabaseRow> databases, IReadOnlyList<LoginChoice> logins, bool keepEmpty = false)
    {
        var known = databases.Select(d => d.DatabaseId).ToHashSet();
        var dup = databases.GroupBy(d => d.Label, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<DatabaseGroup>();
        foreach (var d in databases.OrderBy(d => d.Label, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.DatabaseId))
        {
            var mine = logins.Where(l => l.DatabaseId == d.DatabaseId).ToList();
            if (mine.Count == 0 && !keepEmpty) continue;
            result.Add(new DatabaseGroup(d.DatabaseId, dup.Contains(d.Label) ? $"{d.Label} (ID {d.DatabaseId})" : d.Label, mine));
        }
        var other = logins.Where(l => l.DatabaseId is not { } id || !known.Contains(id)).ToList();
        if (other.Count > 0) result.Add(new DatabaseGroup(null, "Other logins", other));
        return result;
    }
}
