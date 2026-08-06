namespace COCOBOLOERPNEW.DTOs;

public static class ProductFactoryAlternativeStatuses
{
    public const string Proposed = "Proposed";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    public static readonly string[] All = [Proposed, Approved, Rejected];
}

public class ProductFactoryAlternativeImageDto
{
    public int AlternativeImageId { get; set; }
    public int AlternativeId { get; set; }
    public string ImagePath { get; set; } = "";
    public string? Caption { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProductFactoryAlternativeDto
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
    public string Status { get; set; } = ProductFactoryAlternativeStatuses.Proposed;
    public bool IsPrimary { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public List<ProductFactoryAlternativeImageDto> Images { get; set; } = new();
}
