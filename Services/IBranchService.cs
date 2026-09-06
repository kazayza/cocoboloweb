using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services;

public interface IBranchService
{
    Task<List<BranchListDto>> GetBranchesAsync();
    Task<BranchFormDto?> GetBranchForEditAsync(int branchId);
    Task<List<Employee>> GetManagersAsync();
    Task<(bool Success, string Message, int? BranchId)> SaveBranchAsync(BranchFormDto dto, string currentUserName);
    Task<(bool Success, string Message)> ToggleBranchStatusAsync(int branchId, bool isActive, string currentUserName);

    /// <summary>
    /// ⭐ قائمة الفروع النشطة لاستخدامها في قوائم الفلترة (خفيفة: Id + الاسم العربي فقط)
    /// </summary>
    Task<List<BranchOptionDto>> GetBranchesForFilterAsync();
}
