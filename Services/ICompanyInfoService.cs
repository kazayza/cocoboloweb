using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface ICompanyInfoService
{
    /// <summary>
    /// جلب بيانات الشركة
    /// </summary>
    Task<CompanyInfoDto?> GetCompanyInfoAsync();

    /// <summary>
    /// تحديث بيانات الشركة
    /// </summary>
    Task<CompanyInfoDto> UpdateCompanyInfoAsync(CompanyInfoUpdateDto dto);

    /// <summary>
    /// رفع شعار الشركة
    /// </summary>
    Task<string> UploadLogoAsync(int companyId, string base64Image);

    /// <summary>
    /// جلب كل فروع الشركة
    /// </summary>
    Task<List<CompanyLocationDto>> GetLocationsAsync();

    /// <summary>
    /// إضافة فرع جديد
    /// </summary>
    Task<CompanyLocationDto> AddLocationAsync(CompanyLocationFormDto dto);

    /// <summary>
    /// تعديل فرع
    /// </summary>
    Task<CompanyLocationDto> UpdateLocationAsync(CompanyLocationFormDto dto);

    /// <summary>
    /// حذف فرع
    /// </summary>
    Task<bool> DeleteLocationAsync(int locationId);
}