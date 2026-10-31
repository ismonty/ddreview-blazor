using DDReview.Core.Gl;

namespace DDReview.Web.Components.Home;

/// <summary>What the home tabs need from the signed-in user: the client connection and the stores allowed for Sales (the old StoreListAuthorizedSales).</summary>
public sealed class UserContextHolder(string connectionString, IReadOnlyList<StoreInfo> stores)
{
    public string ConnectionString { get; } = connectionString;
    public IReadOnlyList<StoreInfo> Stores { get; } = stores;
    public IReadOnlyList<int> StoreIds { get; } = stores.Select(s => s.StoreId).ToList();
}
