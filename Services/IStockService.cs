using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IStockService
{
    Task<List<BranchListDto>> GetBranchesAsync();
    Task<List<WarehouseListDto>> GetWarehousesAsync(int? branchId = null);
    Task<List<StockProductLookupDto>> SearchProductsAsync(string? searchText, int take = 30);
    Task<List<StockProductLookupDto>> SearchProductsByWarehouseAsync(int warehouseId, string? searchText, int take = 30);
    Task<int> GetCurrentStockAsync(int productId, int warehouseId);
    Task<StockEntryResultDto> AddStockEntryAsync(StockEntryFormDto dto, string currentUserName);
    Task<StockTransferResultDto> TransferStockAsync(StockTransferFormDto dto, string currentUserName);
    Task<List<StockTransactionListDto>> GetStockTransactionsAsync(StockTransactionFilterDto filter);
    Task<StockCountWorkspaceDto?> GetStockCountWorkspaceAsync(int warehouseId, int? stockCountId = null);
    Task<List<StockCountHeaderListDto>> GetStockCountsAsync(StockCountFilterDto filter);
    Task<(bool Success, string Message, int? StockCountId)> SaveStockCountDraftAsync(StockCountWorkspaceDto dto, string currentUserName);
    Task<(bool Success, string Message)> FinalizeStockCountAsync(int stockCountId, string currentUserName);
}
