using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// خدمة توليد ملفات PDF / Excel لعروض الأسعار.
/// </summary>
public interface IQuotationExportService
{
    // ============ Single Quotation Exports ============

    /// <summary>توليد PDF لعرض سعر واحد. publicAccess = روابط التوكن العامة (بدون فحص صلاحية وبدون أسعار شراء).</summary>
    Task<(bool Success, string? Error, byte[]? Pdf, string FileName)> 
        GeneratePdfAsync(int quotationId, bool publicAccess = false);

    /// <summary>توليد Excel لعرض سعر واحد. publicAccess = روابط التوكن العامة (بدون فحص صلاحية وبدون أسعار شراء).</summary>
    Task<(bool Success, string? Error, byte[]? Excel, string FileName)> 
        GenerateExcelAsync(int quotationId, bool publicAccess = false);

    // ============ List Exports (مع الفلاتر) ============

    /// <summary>⭐ تصدير قائمة عروض الأسعار لـ Excel (مع تطبيق الفلاتر).</summary>
    Task<(bool Success, string? Error, byte[]? Excel, string FileName)> 
        ExportQuotationsToExcelAsync(QuotationFilterDto filter);

    /// <summary>⭐ تصدير قائمة عروض الأسعار لـ PDF (مع تطبيق الفلاتر).</summary>
    Task<(bool Success, string? Error, byte[]? Pdf, string FileName)> 
        ExportQuotationsToPdfAsync(QuotationFilterDto filter);
}