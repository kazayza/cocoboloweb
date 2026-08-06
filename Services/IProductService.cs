using COCOBOLOERPNEW.DTOs;
using Microsoft.AspNetCore.Components.Forms;

public interface IProductService
{
    Task<List<ProductListDto>> GetProductsAsync(string? search);
    
    Task FactorySetCostAsync(
        int productId,
        decimal? cClassCost,
        decimal? premiumCost,
        decimal? eliteCost,
        string currentUsername);

    Task RequestSalePriceChangeAsync(
        int productId,
        decimal? newCClassSalePrice,
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
        decimal? newCClassCost,
        decimal? newPremiumCost,
        decimal? newEliteCost,
        string currentUsername);

    Task RequestPeriodChangeAsync(
        int productId,
        int? newPeriod,
        string? newManufacturingNotes,
        string reason,
        string currentUsername);

    Task ApprovePeriodChangeAsync(
        int productId,
        string currentUsername);

    Task RejectPeriodChangeAsync(
        int productId,
        string currentUsername,
        string? rejectReason = null);

    Task<List<ProductFactoryAlternativeDto>> GetFactoryAlternativesAsync(int productId);
    Task<(bool Success, string Message, int? AlternativeId)> SaveFactoryAlternativeAsync(ProductFactoryAlternativeDto dto, IReadOnlyList<IBrowserFile> files, string currentUsername);
    Task<(bool Success, string Message)> ApproveFactoryAlternativeAsync(int alternativeId, string currentUsername);
    Task<(bool Success, string Message)> RejectFactoryAlternativeAsync(int alternativeId, string currentUsername, string? reason = null);
    Task<(bool Success, string Message)> DeleteFactoryAlternativeAsync(int alternativeId, string currentUsername);
}