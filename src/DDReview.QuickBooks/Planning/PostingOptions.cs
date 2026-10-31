namespace DDReview.QuickBooks.Planning;

/// <summary>Settings that change how a package becomes a plan (these were database/registry settings in DDAdminCS).</summary>
public sealed class PostingOptions
{
    /// <summary>Post purchase details for A/R invoices (setting ExportPurchaseDetails).</summary>
    public bool DetailPostingActive { get; set; }
    /// <summary>Put the purchase details in the invoice memo instead of a "Purchase Details" line.</summary>
    public bool PostDetailAsInvoiceMemo { get; set; }
    /// <summary>Sales tax code to put on invoices (ExportInvoiceItemSalesTaxCode). Empty: none.</summary>
    public string ItemSalesTaxCode { get; set; } = "";
    /// <summary>Canadian tax: the item and entity to put on journal lines that carry tax. Empty: none.</summary>
    public string SalesTaxItemName { get; set; } = "";
    public string SalesTaxItemListId { get; set; } = "";
    public string SalesTaxEntityName { get; set; } = "";
    public string SalesTaxEntityListId { get; set; } = "";
}
