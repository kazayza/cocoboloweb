using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services;

public interface ICrmSettingsService
{
    // Sources
    Task<List<ContactSource>> GetSourcesAsync(bool includeInactive = false);
    Task<ContactSource?> GetSourceByIdAsync(int id);
    Task<(bool Success, string Message)> SaveSourceAsync(ContactSource source, string userName);
    Task<(bool Success, string Message)> ToggleSourceAsync(int id, bool isActive, string userName);
    
    // Stages
    Task<List<SalesStage>> GetStagesAsync(bool includeInactive = false);
    Task<(bool Success, string Message)> SaveStageAsync(SalesStage stage, string userName);
    Task<(bool Success, string Message)> ToggleStageAsync(int id, bool isActive, string userName);
    Task<(bool Success, string Message)> ReorderStagesAsync(List<int> stageIds, string userName);
    
    // Lost Reasons
    Task<List<LostReason>> GetLostReasonsAsync(bool includeInactive = false);
    Task<(bool Success, string Message)> SaveLostReasonAsync(LostReason reason, string userName);
    Task<(bool Success, string Message)> ToggleLostReasonAsync(int id, bool isActive, string userName);
    
    // Categories
    Task<List<InterestCategory>> GetCategoriesAsync(bool includeInactive = false);
    Task<(bool Success, string Message)> SaveCategoryAsync(InterestCategory category, string userName);
    Task<(bool Success, string Message)> ToggleCategoryAsync(int id, bool isActive, string userName);
    
    // Task Types
    Task<List<TaskType>> GetTaskTypesAsync(bool includeInactive = false);
    Task<(bool Success, string Message)> SaveTaskTypeAsync(TaskType taskType, string userName);
    Task<(bool Success, string Message)> ToggleTaskTypeAsync(int id, bool isActive, string userName);
    
    // Ad Types
    Task<List<AdType>> GetAdTypesAsync(bool includeInactive = false);
    Task<(bool Success, string Message)> SaveAdTypeAsync(AdType adType, string userName);
    Task<(bool Success, string Message)> ToggleAdTypeAsync(int id, bool isActive, string userName);
    
    // Usage Counts
    Task<Dictionary<string, int>> GetUsageCountsAsync();
}