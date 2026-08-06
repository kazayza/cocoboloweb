namespace COCOBOLOERPNEW.Models;

public partial class ProductFactoryAlternative
{
    public int AlternativeId { get; set; }
    public int ProductId { get; set; }
    public string AlternativeName { get; set; } = "";
    public string? SpecificationSummary { get; set; }
    public string? ManufacturingDescription { get; set; }
    public int? Period { get; set; }
    public decimal? PurchasePriceCClass { get; set; }
    public decimal? PurchasePricePremium { get; set; }
    public decimal? PurchasePriceElite { get; set; }
    public decimal? SuggestedSalePriceCClass { get; set; }
    public decimal? SuggestedSalePricePremium { get; set; }
    public decimal? SuggestedSalePriceElite { get; set; }
    public string Status { get; set; } = "Proposed";
    public bool IsPrimary { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public virtual Product Product { get; set; } = null!;
    public virtual ICollection<ProductFactoryAlternativeImage> Images { get; set; } = new List<ProductFactoryAlternativeImage>();
}
