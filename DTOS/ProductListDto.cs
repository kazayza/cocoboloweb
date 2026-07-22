namespace COCOBOLOERPNEW.DTOs;

public class ProductListDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string ProductDescription { get; set; } = "";
    public string? CustomerName { get; set; }
    public int? Customer { get; set; }
    public int? PricingStatusId { get; set; }
    public string? PricingType { get; set; }
    public decimal? SuggestedSalePrice { get; set; }
    public decimal? SuggestedSalePriceCClass { get; set; }
    public decimal? SuggestedSalePriceElite { get; set; }
    public string? PdfPath { get; set; }
    public bool HasOldPdf { get; set; } = false;

    // ⭐ تواريخ وأوقات التسجيل والتسعير
    public DateTime? CreatedAt { get; set; }
    public DateTime? FactoryPricedAt { get; set; }
    public string? ResponseTimeText { get; set; }
    public string? ResponseTimeClass { get; set; }
}