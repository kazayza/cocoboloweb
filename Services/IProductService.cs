using COCOBOLOERPNEW.DTOs;

public interface IProductService
{
    Task<List<ProductListDto>> GetProductsAsync(string? search);
    Task FactorySetCostAsync(
        int productId,
        decimal? premiumCost,
        decimal? eliteCost,
        string currentUsername);

    Task RequestSalePriceChangeAsync(
        int productId,
        decimal newPremiumSalePrice,
        decimal? newEliteSalePrice,
        string currentUsername);
}