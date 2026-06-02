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

    Task ApproveSalePriceChangeAsync(
        int productId, 
        string currentUsername);

    Task RejectSalePriceChangeAsync(
        int productId, 
        string currentUsername, 
        string? rejectReason = null);

    Task RequestCostChangeAsync(
        int productId, 
        string currentUsername);

    Task ApproveCostChangeAsync(
        int productId, 
        decimal? newPremiumCost, 
        decimal? newEliteCost, 
        string currentUsername);
}