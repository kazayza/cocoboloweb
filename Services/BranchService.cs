using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class BranchService : IBranchService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;

    public BranchService(db24804Context db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<BranchListDto>> GetBranchesAsync()
    {
        return await _db.Branches
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.BranchNameAr)
            .Select(x => new BranchListDto
            {
                BranchId = x.BranchId,
                BranchCode = x.BranchCode,
                BranchNameAr = x.BranchNameAr,
                BranchNameEn = x.BranchNameEn,
                Address = x.Address,
                Phone = x.Phone,
                ManagerEmployeeId = x.ManagerEmployeeId,
                ManagerEmployeeName = x.ManagerEmployee != null ? x.ManagerEmployee.FullName : null,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                CreatedBy = x.CreatedBy
            })
            .ToListAsync();
    }

    public async Task<BranchFormDto?> GetBranchForEditAsync(int branchId)
    {
        return await _db.Branches
            .AsNoTracking()
            .Where(x => x.BranchId == branchId)
            .Select(x => new BranchFormDto
            {
                BranchId = x.BranchId,
                BranchCode = x.BranchCode,
                BranchNameAr = x.BranchNameAr,
                BranchNameEn = x.BranchNameEn,
                Address = x.Address,
                Phone = x.Phone,
                ManagerEmployeeId = x.ManagerEmployeeId,
                IsActive = x.IsActive
            })
            .FirstOrDefaultAsync();
    }

    public async Task<List<Employee>> GetManagersAsync()
    {
        return await _db.Employees
            .AsNoTracking()
            .Where(x => x.Status == "نشط")
            .OrderBy(x => x.FullName)
            .Select(x => new Employee
            {
                EmployeeId = x.EmployeeId,
                FullName = x.FullName,
                Department = x.Department,
                JobTitle = x.JobTitle
            })
            .ToListAsync();
    }

    public async Task<(bool Success, string Message, int? BranchId)> SaveBranchAsync(BranchFormDto dto, string currentUserName)
    {
        var code = dto.BranchCode?.Trim();
        var nameAr = dto.BranchNameAr?.Trim();

        if (string.IsNullOrWhiteSpace(code))
            return (false, "كود الفرع مطلوب.", null);

        if (string.IsNullOrWhiteSpace(nameAr))
            return (false, "اسم الفرع بالعربي مطلوب.", null);

        code = code.ToUpperInvariant();

        var duplicateCode = await _db.Branches
            .AsNoTracking()
            .AnyAsync(x => x.BranchCode == code && x.BranchId != dto.BranchId);

        if (duplicateCode)
            return (false, "كود الفرع مستخدم بالفعل.", null);

        var duplicateName = await _db.Branches
            .AsNoTracking()
            .AnyAsync(x => x.BranchNameAr == nameAr && x.BranchId != dto.BranchId);

        if (duplicateName)
            return (false, "اسم الفرع بالعربي مستخدم بالفعل.", null);

        var isNew = dto.BranchId == 0;
        Branch entity;
        object? oldData = null;

        if (isNew)
        {
            entity = new Branch
            {
                CreatedAt = DateTime.Now,
                CreatedBy = currentUserName,
                IsActive = true
            };
            _db.Branches.Add(entity);
        }
        else
        {
            entity = await _db.Branches.FirstOrDefaultAsync(x => x.BranchId == dto.BranchId)
                ?? throw new Exception("الفرع غير موجود.");

            oldData = new
            {
                entity.BranchId,
                entity.BranchCode,
                entity.BranchNameAr,
                entity.BranchNameEn,
                entity.Address,
                entity.Phone,
                entity.ManagerEmployeeId,
                entity.IsActive
            };
        }

        entity.BranchCode = code;
        entity.BranchNameAr = nameAr;
        entity.BranchNameEn = string.IsNullOrWhiteSpace(dto.BranchNameEn) ? null : dto.BranchNameEn.Trim();
        entity.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
        entity.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
        entity.ManagerEmployeeId = dto.ManagerEmployeeId;
        entity.IsActive = dto.IsActive;

        await _db.SaveChangesAsync();

        var newData = new
        {
            entity.BranchId,
            entity.BranchCode,
            entity.BranchNameAr,
            entity.BranchNameEn,
            entity.Address,
            entity.Phone,
            entity.ManagerEmployeeId,
            entity.IsActive
        };

        await _audit.LogAsync(
            "Branches",
            isNew ? "Insert" : "Update",
            entity.BranchId.ToString(),
            oldData,
            newData,
            currentUserName);

        return (true, isNew ? "تم إضافة الفرع بنجاح." : "تم تعديل الفرع بنجاح.", entity.BranchId);
    }

    public async Task<(bool Success, string Message)> ToggleBranchStatusAsync(int branchId, bool isActive, string currentUserName)
    {
        var entity = await _db.Branches.FirstOrDefaultAsync(x => x.BranchId == branchId);
        if (entity == null)
            return (false, "الفرع غير موجود.");

        var oldData = new { entity.BranchId, entity.IsActive };

        entity.IsActive = isActive;
        await _db.SaveChangesAsync();

        var newData = new { entity.BranchId, entity.IsActive };

        await _audit.LogAsync(
            "Branches",
            isActive ? "Activate" : "Deactivate",
            entity.BranchId.ToString(),
            oldData,
            newData,
            currentUserName);

        return (true, isActive ? "تم تفعيل الفرع." : "تم تعطيل الفرع.");
    }
}
