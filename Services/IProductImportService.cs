using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

/// <summary>🌐 استيراد منتجات الموقع من Excel (2026-09-17)</summary>
public interface IProductImportService
{
    /// <summary>قراءة ملف Excel والتحقق — بدون حفظ (للمعاينة)</summary>
    Task<(bool Success, string Message, List<ProductWebsiteImportRowDto> Rows)> ParseAsync(Stream excelStream);

    /// <summary>تنفيذ الاستيراد: إنشاء المنتجات معلَّمة IsWebsite=true (Admin/AccountManager فقط)</summary>
    Task<(bool Success, string Message, ProductWebsiteImportResultDto? Result)> ImportAsync(List<ProductWebsiteImportRowDto> rows, string currentUsername);

    /// <summary>قالب Excel جاهز بالأعمدة المعتمدة + سطر مثال</summary>
    byte[] GetTemplateBytes();
}
