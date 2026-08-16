using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services;

public interface IWarehouseService
{
    Task<List<WarehouseListDto>> GetWarehousesAsync();
    Task<WarehouseFormDto?> GetWarehouseForEditAsync(int warehouseId);
    Task<List<Branch>> GetBranchesAsync();
    Task<(bool Success, string Message, int? WarehouseId)> SaveWarehouseAsync(WarehouseFormDto dto, string currentUserName);
    Task<(bool Success, string Message)> ToggleWarehouseStatusAsync(int warehouseId, bool isActive, string currentUserName);
}
