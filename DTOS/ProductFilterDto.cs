namespace COCOBOLOERPNEW.DTOs;

/// <summary>
/// فلتر البحث في المنتجات مع Pagination
/// </summary>
public class ProductFilterDto
{
    public string? Search { get; set; }
    public int? CustomerId { get; set; }
    public int? PricingStatusId { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string SortBy { get; set; } = "ProductId";
    public bool SortDescending { get; set; } = true;
}