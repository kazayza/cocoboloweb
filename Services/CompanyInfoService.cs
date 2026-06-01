using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class CompanyInfoService : ICompanyInfoService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;

    public CompanyInfoService(db24804Context db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// جلب بيانات الشركة
    /// </summary>
    public async Task<CompanyInfoDto?> GetCompanyInfoAsync()
    {
        var company = await _db.CompanyInfos.AsNoTracking().FirstOrDefaultAsync();
        if (company is null) return null;

        var locationsCount = await _db.CompanyLocations.AsNoTracking().CountAsync();

        return new CompanyInfoDto
        {
            CompanyId = company.CompanyId,
            CompanyName = company.CompanyName,
            LogoBase64 = company.Logo != null && company.Logo.Length > 0
                ? Convert.ToBase64String(company.Logo)
                : null,
            LogoPath = company.LogoPath,
            Address = company.Address,
            Phone1 = company.Phone1,
            Phone2 = company.Phone2,
            SalesEmail = company.SalesEmail,
            InfoEmail = company.InfoEmail,
            Website = company.Website,
            TocDropBox = company.TocDropBox,
            CurrentVersion = company.CurrentVersion,
            LocationsCount = locationsCount
        };
    }

    /// <summary>
    /// تحديث بيانات الشركة
    /// </summary>
    public async Task<CompanyInfoDto> UpdateCompanyInfoAsync(CompanyInfoUpdateDto dto)
    {
        var company = await _db.CompanyInfos.FirstOrDefaultAsync(c => c.CompanyId == dto.CompanyId);
        if (company is null)
            throw new KeyNotFoundException("لم يتم العثور على بيانات الشركة");

        // حفظ البيانات القديمة للأوديت
        var oldData = new
        {
            company.CompanyName,
            company.Address,
            company.Phone1,
            company.Phone2,
            company.SalesEmail,
            company.InfoEmail,
            company.Website,
            company.TocDropBox,
            company.CurrentVersion
        };

        company.CompanyName = dto.CompanyName;
        company.Address = dto.Address;
        company.Phone1 = dto.Phone1;
        company.Phone2 = dto.Phone2;
        company.SalesEmail = dto.SalesEmail;
        company.InfoEmail = dto.InfoEmail;
        company.Website = dto.Website;
        company.TocDropBox = dto.TocDropBox;
        company.CurrentVersion = dto.CurrentVersion;

        // لو فيه شعار جديد Base64
        if (!string.IsNullOrEmpty(dto.LogoBase64))
        {
            try
            {
                company.Logo = Convert.FromBase64String(dto.LogoBase64);
            }
            catch
            {
                // تجاهل لو الـ Base64 مش صالح
            }
        }

        await _db.SaveChangesAsync();

        // حفظ البيانات الجديدة للأوديت
        var newData = new
        {
            company.CompanyName,
            company.Address,
            company.Phone1,
            company.Phone2,
            company.SalesEmail,
            company.InfoEmail,
            company.Website,
            company.TocDropBox,
            company.CurrentVersion
        };

        await _audit.LogAsync("CompanyInfo", "Edit", company.CompanyId.ToString(), oldData, newData, "System");

        return await GetCompanyInfoAsync() ?? throw new InvalidOperationException("فشل في استرجاع البيانات بعد التحديث");
    }

    /// <summary>
    /// رفع شعار الشركة
    /// </summary>
    public async Task<string> UploadLogoAsync(int companyId, string base64Image)
    {
        var company = await _db.CompanyInfos.FirstOrDefaultAsync(c => c.CompanyId == companyId);
        if (company is null)
            throw new KeyNotFoundException("لم يتم العثور على بيانات الشركة");

        var oldData = new { company.LogoPath, HasLogo = company.Logo != null };

        company.Logo = Convert.FromBase64String(base64Image);
        await _db.SaveChangesAsync();

        var newData = new { company.LogoPath, HasLogo = company.Logo != null };

        await _audit.LogAsync("CompanyInfo", "Edit", company.CompanyId.ToString(), oldData, newData, "System");

        return "تم رفع الشعار بنجاح";
    }

    /// <summary>
    /// جلب كل فروع الشركة
    /// </summary>
    public async Task<List<CompanyLocationDto>> GetLocationsAsync()
    {
        return await _db.CompanyLocations
            .AsNoTracking()
            .Select(l => new CompanyLocationDto
            {
                LocationId = l.LocationId,
                LocationName = l.LocationName,
                Latitude = l.Latitude,
                Longitude = l.Longitude,
                AllowedRadius = l.AllowedRadius,
                IsActive = l.IsActive
            })
            .OrderBy(l => l.LocationName)
            .ToListAsync();
    }

    /// <summary>
    /// إضافة فرع جديد
    /// </summary>
    public async Task<CompanyLocationDto> AddLocationAsync(CompanyLocationFormDto dto)
    {
        var location = new CompanyLocation
        {
            LocationName = dto.LocationName,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            AllowedRadius = dto.AllowedRadius,
            IsActive = dto.IsActive
        };

        _db.CompanyLocations.Add(location);
        await _db.SaveChangesAsync();

        var newData = new
        {
            location.LocationName,
            location.Latitude,
            location.Longitude,
            location.AllowedRadius,
            location.IsActive
        };

        await _audit.LogAsync("CompanyLocation", "Add", location.LocationId.ToString(), 
            (object?)null, newData, "System");

        return new CompanyLocationDto
        {
            LocationId = location.LocationId,
            LocationName = location.LocationName,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            AllowedRadius = location.AllowedRadius,
            IsActive = location.IsActive
        };
    }

    /// <summary>
    /// تعديل فرع
    /// </summary>
    public async Task<CompanyLocationDto> UpdateLocationAsync(CompanyLocationFormDto dto)
    {
        if (!dto.LocationId.HasValue)
            throw new ArgumentException("معرف الفرع مطلوب للتعديل");

        var location = await _db.CompanyLocations.FirstOrDefaultAsync(l => l.LocationId == dto.LocationId.Value);
        if (location is null)
            throw new KeyNotFoundException("لم يتم العثور على الفرع");

        var oldData = new
        {
            location.LocationName,
            location.Latitude,
            location.Longitude,
            location.AllowedRadius,
            location.IsActive
        };

        location.LocationName = dto.LocationName;
        location.Latitude = dto.Latitude;
        location.Longitude = dto.Longitude;
        location.AllowedRadius = dto.AllowedRadius;
        location.IsActive = dto.IsActive;

        await _db.SaveChangesAsync();

        var newData = new
        {
            location.LocationName,
            location.Latitude,
            location.Longitude,
            location.AllowedRadius,
            location.IsActive
        };

        await _audit.LogAsync("CompanyLocation", "Edit", location.LocationId.ToString(), oldData, newData, "System");

        return new CompanyLocationDto
        {
            LocationId = location.LocationId,
            LocationName = location.LocationName,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            AllowedRadius = location.AllowedRadius,
            IsActive = location.IsActive
        };
    }

    /// <summary>
    /// حذف فرع
    /// </summary>
    public async Task<bool> DeleteLocationAsync(int locationId)
    {
        var location = await _db.CompanyLocations.FirstOrDefaultAsync(l => l.LocationId == locationId);
        if (location is null) return false;

        var oldData = new
        {
            location.LocationName,
            location.Latitude,
            location.Longitude,
            location.AllowedRadius,
            location.IsActive
        };

        _db.CompanyLocations.Remove(location);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("CompanyLocation", "Delete", locationId.ToString(), oldData, (object?)null, "System");

        return true;
    }
}
