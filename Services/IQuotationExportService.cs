namespace COCOBOLOERPNEW.Services;

/// <summary>
/// خدمة توليد ملفات PDF / Excel لعروض الأسعار.
/// </summary>
public interface IQuotationExportService
{
    /// <summary>توليد PDF للعرض. يرجع bytes جاهزة للإرسال.</summary>
    Task<(bool Success, string? Error, byte[]? Pdf, string FileName)> GeneratePdfAsync(int quotationId);

    /// <summary>توليد Excel للعرض.</summary>
    Task<(bool Success, string? Error, byte[]? Excel, string FileName)> GenerateExcelAsync(int quotationId);
}
