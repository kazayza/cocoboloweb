using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class WarehouseService : IWarehouseService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;

    public WarehouseService(db24804Context db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<WarehouseListDto>> GetWarehousesAsync()
    {
        return await _db.Warehouses
            .AsNoTracking()
            .Include(x => x.Branch)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Branch != null ? x.Branch.BranchNameAr : "")
            .ThenBy(x => x.WarehouseName)
            .Select(x => new WarehouseListDto
            {
                WarehouseId = x.WarehouseId,
                WarehouseName = x.WarehouseName,
                BranchId = x.BranchId,
                BranchNameAr = x.Branch != null ? x.Branch.BranchNameAr : null,
                Location = x.Location,
                Notes = x.Notes,
                IsActive = x.IsActive ?? true,
                CreatedAt = x.CreatedAt,
                CreatedBy = x.CreatedBy
            })
            .ToListAsync();
    }

    public async Task<WarehouseFormDto?> GetWarehouseForEditAsync(int warehouseId)
    {
        return await _db.Warehouses
            .AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .Select(x => new WarehouseFormDto
            {
                WarehouseId = x.WarehouseId,
                WarehouseName = x.WarehouseName,
                BranchId = x.BranchId,
                Location = x.Location,
                Notes = x.Notes,
                IsActive = x.IsActive ?? true
            })
            .FirstOrDefaultAsync();
    }

    public async Task<List<Branch>> GetBranchesAsync()
    {
        return await _db.Branches
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.BranchNameAr)
            .ToListAsync();
    }

    public async Task<(bool Success, string Message, int? WarehouseId)> SaveWarehouseAsync(WarehouseFormDto dto, string currentUserName)
    {
        var name = dto.WarehouseName?.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return (false, "اسم المخزن مطلوب.", null);

        if (!dto.BranchId.HasValue || dto.BranchId.Value <= 0)
            return (false, "اختيار الفرع مطلوب.", null);

        var branchExists = await _db.Branches.AsNoTracking().AnyAsync(x => x.BranchId == dto.BranchId.Value);
        if (!branchExists)
            return (false, "الفرع المختار غير موجود.", null);

        var duplicate = await _db.Warehouses
            .AsNoTracking()
            .AnyAsync(x => x.WarehouseName == name && x.BranchId == dto.BranchId && x.WarehouseId != dto.WarehouseId);

        if (duplicate)
            return (false, "يوجد مخزن بنفس الاسم داخل نفس الفرع.", null);

        var isNew = dto.WarehouseId == 0;
        Warehouse entity;
        object? oldData = null;

        if (isNew)
        {
            entity = new Warehouse
            {
                CreatedAt = DateTime.Now,
                CreatedBy = currentUserName,
                IsActive = true
            };
            _db.Warehouses.Add(entity);
        }
        else
        {
            entity = await _db.Warehouses.FirstOrDefaultAsync(x => x.WarehouseId == dto.WarehouseId)
                ?? throw new Exception("المخزن غير موجود.");

            oldData = new
            {
                entity.WarehouseId,
                entity.WarehouseName,
                entity.BranchId,
                entity.Location,
                entity.Notes,
                entity.IsActive
            };
        }

        entity.WarehouseName = name;
        entity.BranchId = dto.BranchId;
        entity.Location = string.IsNullOrWhiteSpace(dto.Location) ? null : dto.Location.Trim();
        entity.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
        entity.IsActive = dto.IsActive;

        await _db.SaveChangesAsync();

        var newData = new
        {
            entity.WarehouseId,
            entity.WarehouseName,
            entity.BranchId,
            entity.Location,
            entity.Notes,
            entity.IsActive
        };

        await _audit.LogAsync<object>(
            "Warehouses",
            isNew ? "Insert" : "Update",
            entity.WarehouseId.ToString(),
            oldData,
            newData,
            currentUserName);

        return (true, isNew ? "تم إضافة المخزن بنجاح." : "تم تعديل المخزن بنجاح.", entity.WarehouseId);
    }

    public async Task<(bool Success, string Message)> ToggleWarehouseStatusAsync(int warehouseId, bool isActive, string currentUserName)
    {
        var entity = await _db.Warehouses.FirstOrDefaultAsync(x => x.WarehouseId == warehouseId);
        if (entity == null)
            return (false, "المخزن غير موجود.");

        var oldData = new
        {
            entity.WarehouseId,
            entity.IsActive
        };

        entity.IsActive = isActive;
        await _db.SaveChangesAsync();

        var newData = new
        {
            entity.WarehouseId,
            entity.IsActive
        };

        await _audit.LogAsync<object>(
            "Warehouses",
            isActive ? "Activate" : "Deactivate",
            entity.WarehouseId.ToString(),
            oldData,
            newData,
            currentUserName);

        return (true, isActive ? "تم تفعيل المخزن." : "تم تعطيل المخزن.");
    }
}
