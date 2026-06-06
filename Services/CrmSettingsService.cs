using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class CrmSettingsService : ICrmSettingsService
{
    private readonly IDbContextFactory<db24804Context> _dbFactory;

    public CrmSettingsService(IDbContextFactory<db24804Context> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ═══════════════════════════════════════════
    // SOURCES
    // ═══════════════════════════════════════════
    public async Task<List<ContactSource>> GetSourcesAsync(bool includeInactive = false)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.ContactSources.AsQueryable();
        if (!includeInactive) query = query.Where(s => s.IsActive);
        return await query.OrderBy(s => s.SourceNameAr).ToListAsync();
    }

    public async Task<ContactSource?> GetSourceByIdAsync(int id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ContactSources.FindAsync(id);
    }

    public async Task<(bool Success, string Message)> SaveSourceAsync(ContactSource source, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            
            if (source.SourceId == 0)
            {
                source.CreatedBy = userName;
                source.CreatedAt = DateTime.Now;
                source.IsActive = true;
                db.ContactSources.Add(source);
            }
            else
            {
                var existing = await db.ContactSources.FindAsync(source.SourceId);
                if (existing == null) return (false, "المصدر غير موجود");
                
                existing.SourceName = source.SourceName;
                existing.SourceNameAr = source.SourceNameAr;
                existing.SourceIcon = source.SourceIcon;
                existing.LastUpdatedBy = userName;
                existing.LastUpdatedAt = DateTime.Now;
            }
            
            await db.SaveChangesAsync();
            return (true, "تم الحفظ بنجاح");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ToggleSourceAsync(int id, bool isActive, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var source = await db.ContactSources.FindAsync(id);
            if (source == null) return (false, "المصدر غير موجود");
            
            // Check usage if deactivating
            if (!isActive)
            {
                var usage = await db.SalesOpportunities.CountAsync(o => o.SourceId == id && o.IsActive);
                if (usage > 0) return (false, $"لا يمكن التعطيل - مستخدم في {usage} فرصة");
            }
            
            source.IsActive = isActive;
            source.LastUpdatedBy = userName;
            source.LastUpdatedAt = DateTime.Now;
            
            await db.SaveChangesAsync();
            return (true, isActive ? "تم التفعيل" : "تم التعطيل");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════
    // STAGES
    // ═══════════════════════════════════════════
    public async Task<List<SalesStage>> GetStagesAsync(bool includeInactive = false)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.SalesStages.AsQueryable();
        if (!includeInactive) query = query.Where(s => s.IsActive);
        return await query.OrderBy(s => s.StageOrder).ThenBy(s => s.StageNameAr).ToListAsync();
    }

    public async Task<(bool Success, string Message)> SaveStageAsync(SalesStage stage, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            
            if (stage.StageId == 0)
            {
                stage.CreatedBy = userName;
                stage.CreatedAt = DateTime.Now;
                stage.IsActive = true;
                stage.StageOrder = await db.SalesStages.MaxAsync(s => (int?)s.StageOrder) ?? 0 + 1;
                db.SalesStages.Add(stage);
            }
            else
            {
                var existing = await db.SalesStages.FindAsync(stage.StageId);
                if (existing == null) return (false, "المرحلة غير موجودة");
                
                existing.StageName = stage.StageName;
                existing.StageNameAr = stage.StageNameAr;
                existing.StageColor = stage.StageColor;
                existing.LastUpdatedBy = userName;
                existing.LastUpdatedAt = DateTime.Now;
            }
            
            await db.SaveChangesAsync();
            return (true, "تم الحفظ بنجاح");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ToggleStageAsync(int id, bool isActive, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var stage = await db.SalesStages.FindAsync(id);
            if (stage == null) return (false, "المرحلة غير موجودة");
            
            if (!isActive)
            {
                var usage = await db.SalesOpportunities.CountAsync(o => o.StageId == id && o.IsActive);
                if (usage > 0) return (false, $"لا يمكن التعطيل - مستخدم في {usage} فرصة");
            }
            
            stage.IsActive = isActive;
            stage.LastUpdatedBy = userName;
            stage.LastUpdatedAt = DateTime.Now;
            
            await db.SaveChangesAsync();
            return (true, isActive ? "تم التفعيل" : "تم التعطيل");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ReorderStagesAsync(List<int> stageIds, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            for (int i = 0; i < stageIds.Count; i++)
            {
                var stage = await db.SalesStages.FindAsync(stageIds[i]);
                if (stage != null)
                {
                    stage.StageOrder = i + 1;
                    stage.LastUpdatedBy = userName;
                    stage.LastUpdatedAt = DateTime.Now;
                }
            }
            await db.SaveChangesAsync();
            return (true, "تم إعادة الترتيب");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════
    // LOST REASONS
    // ═══════════════════════════════════════════
    public async Task<List<LostReason>> GetLostReasonsAsync(bool includeInactive = false)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.LostReasons.AsQueryable();
        if (!includeInactive) query = query.Where(l => l.IsActive);
        return await query.OrderBy(l => l.ReasonNameAr).ToListAsync();
    }

    public async Task<(bool Success, string Message)> SaveLostReasonAsync(LostReason reason, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            
            if (reason.LostReasonId == 0)
            {
                reason.CreatedBy = userName;
                reason.CreatedAt = DateTime.Now;
                reason.IsActive = true;
                db.LostReasons.Add(reason);
            }
            else
            {
                var existing = await db.LostReasons.FindAsync(reason.LostReasonId);
                if (existing == null) return (false, "السبب غير موجود");
                
                existing.ReasonName = reason.ReasonName;
                existing.ReasonNameAr = reason.ReasonNameAr;
                existing.LastUpdatedBy = userName;
                existing.LastUpdatedAt = DateTime.Now;
            }
            
            await db.SaveChangesAsync();
            return (true, "تم الحفظ بنجاح");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ToggleLostReasonAsync(int id, bool isActive, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var reason = await db.LostReasons.FindAsync(id);
            if (reason == null) return (false, "السبب غير موجود");
            
            reason.IsActive = isActive;
            reason.LastUpdatedBy = userName;
            reason.LastUpdatedAt = DateTime.Now;
            
            await db.SaveChangesAsync();
            return (true, isActive ? "تم التفعيل" : "تم التعطيل");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════
    // CATEGORIES
    // ═══════════════════════════════════════════
    public async Task<List<InterestCategory>> GetCategoriesAsync(bool includeInactive = false)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.InterestCategories.AsQueryable();
        if (!includeInactive) query = query.Where(c => c.IsActive);
        return await query.OrderBy(c => c.CategoryNameAr).ToListAsync();
    }

    public async Task<(bool Success, string Message)> SaveCategoryAsync(InterestCategory category, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            
            if (category.CategoryId == 0)
            {
                category.CreatedBy = userName;
                category.CreatedAt = DateTime.Now;
                category.IsActive = true;
                db.InterestCategories.Add(category);
            }
            else
            {
                var existing = await db.InterestCategories.FindAsync(category.CategoryId);
                if (existing == null) return (false, "الفئة غير موجودة");
                
                existing.CategoryName = category.CategoryName;
                existing.CategoryNameAr = category.CategoryNameAr;
                existing.LastUpdatedBy = userName;
                existing.LastUpdatedAt = DateTime.Now;
            }
            
            await db.SaveChangesAsync();
            return (true, "تم الحفظ بنجاح");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ToggleCategoryAsync(int id, bool isActive, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var cat = await db.InterestCategories.FindAsync(id);
            if (cat == null) return (false, "الفئة غير موجودة");
            
            cat.IsActive = isActive;
            cat.LastUpdatedBy = userName;
            cat.LastUpdatedAt = DateTime.Now;
            
            await db.SaveChangesAsync();
            return (true, isActive ? "تم التفعيل" : "تم التعطيل");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════
    // TASK TYPES
    // ═══════════════════════════════════════════
    public async Task<List<TaskType>> GetTaskTypesAsync(bool includeInactive = false)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.TaskTypes.AsQueryable();
        if (!includeInactive) query = query.Where(t => t.IsActive);
        return await query.OrderBy(t => t.TaskTypeNameAr).ToListAsync();
    }

    public async Task<(bool Success, string Message)> SaveTaskTypeAsync(TaskType taskType, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            
            if (taskType.TaskTypeId == 0)
            {
                taskType.CreatedBy = userName;
                taskType.CreatedAt = DateTime.Now;
                taskType.IsActive = true;
                db.TaskTypes.Add(taskType);
            }
            else
            {
                var existing = await db.TaskTypes.FindAsync(taskType.TaskTypeId);
                if (existing == null) return (false, "النوع غير موجود");
                
                existing.TaskTypeName = taskType.TaskTypeName;
                existing.TaskTypeNameAr = taskType.TaskTypeNameAr;
                existing.LastUpdatedBy = userName;
                existing.LastUpdatedAt = DateTime.Now;
            }
            
            await db.SaveChangesAsync();
            return (true, "تم الحفظ بنجاح");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ToggleTaskTypeAsync(int id, bool isActive, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var task = await db.TaskTypes.FindAsync(id);
            if (task == null) return (false, "النوع غير موجود");
            
            task.IsActive = isActive;
            task.LastUpdatedBy = userName;
            task.LastUpdatedAt = DateTime.Now;
            
            await db.SaveChangesAsync();
            return (true, isActive ? "تم التفعيل" : "تم التعطيل");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════
    // AD TYPES
    // ═══════════════════════════════════════════
    public async Task<List<AdType>> GetAdTypesAsync(bool includeInactive = false)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.AdTypes.AsQueryable();
        if (!includeInactive) query = query.Where(a => a.IsActive);
        return await query.OrderBy(a => a.AdTypeNameAr).ToListAsync();
    }

    public async Task<(bool Success, string Message)> SaveAdTypeAsync(AdType adType, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            
            if (adType.AdTypeId == 0)
            {
                adType.CreatedBy = userName;
                adType.CreatedAt = DateTime.Now;
                adType.IsActive = true;
                db.AdTypes.Add(adType);
            }
            else
            {
                var existing = await db.AdTypes.FindAsync(adType.AdTypeId);
                if (existing == null) return (false, "الحملة غير موجودة");
                
                existing.AdTypeName = adType.AdTypeName;
                existing.AdTypeNameAr = adType.AdTypeNameAr;
                existing.LastUpdatedBy = userName;
                existing.LastUpdatedAt = DateTime.Now;
            }
            
            await db.SaveChangesAsync();
            return (true, "تم الحفظ بنجاح");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ToggleAdTypeAsync(int id, bool isActive, string userName)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var ad = await db.AdTypes.FindAsync(id);
            if (ad == null) return (false, "الحملة غير موجودة");
            
            if (!isActive)
            {
                var usage = await db.SalesOpportunities.CountAsync(o => o.AdTypeId == id && o.IsActive);
                if (usage > 0) return (false, $"لا يمكن التعطيل - مستخدم في {usage} فرصة");
            }
            
            ad.IsActive = isActive;
            ad.LastUpdatedBy = userName;
            ad.LastUpdatedAt = DateTime.Now;
            
            await db.SaveChangesAsync();
            return (true, isActive ? "تم التفعيل" : "تم التعطيل");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════
    // USAGE COUNTS
    // ═══════════════════════════════════════════
    public async Task<Dictionary<string, int>> GetUsageCountsAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var counts = new Dictionary<string, int>();
        
        var sources = await db.SalesOpportunities
            .Where(o => o.IsActive && o.SourceId != null)
            .GroupBy(o => o.SourceId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();
        
        foreach (var s in sources)
            counts[$"source_{s.Id}"] = s.Count;
        
        var stages = await db.SalesOpportunities
            .Where(o => o.IsActive)
            .GroupBy(o => o.StageId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();
        
        foreach (var s in stages)
            counts[$"stage_{s.Id}"] = s.Count;
        
        return counts;
    }
}