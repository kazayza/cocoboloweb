using COCOBOLOERPNEW.Helpers;
using COCOBOLOERPNEW.Services;
using Microsoft.AspNetCore.Mvc;

namespace COCOBOLOERPNEW.Endpoints;

/// <summary>
/// Endpoints لتوليد ومشاركة عروض الأسعار.
/// 
/// المسارات:
///   GET  /api/quotations/{id}/pdf                  → محمي (للموظفين)
///   GET  /api/quotations/{id}/excel                → محمي
///   POST /api/quotations/{id}/share-link           → محمي (يولّد رابط عام)
///   GET  /api/public/quotations/{token}/pdf        → عام بـ token
///   GET  /api/public/quotations/{token}/excel      → عام بـ token
///   GET  /api/public/quotations/{token}/info       → معلومات سريعة للـ landing page
/// </summary>
public static class QuotationEndpoints
{
    public static IEndpointRouteBuilder MapQuotationExports(this IEndpointRouteBuilder app)
    {
        // ============================================================
        // محمية للموظفين فقط
        // ============================================================
        app.MapGet("/api/quotations/{id:int}/pdf", async (
            int id,
            IQuotationExportService export) =>
        {
            var (ok, error, pdf, fileName) = await export.GeneratePdfAsync(id);
            if (!ok || pdf == null)
                return Results.NotFound(new { error });

            return Results.File(pdf, "application/pdf", fileName, enableRangeProcessing: true);
        }).RequireAuthorization();

        app.MapGet("/api/quotations/{id:int}/excel", async (
            int id,
            IQuotationExportService export) =>
        {
            var (ok, error, xlsx, fileName) = await export.GenerateExcelAsync(id);
            if (!ok || xlsx == null)
                return Results.NotFound(new { error });

            return Results.File(xlsx,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }).RequireAuthorization();

        // ============================================================
        // توليد رابط عام للمشاركة (محمي)
        // ============================================================
        app.MapPost("/api/quotations/{id:int}/share-link", async (
            int id,
            [FromQuery] int? days,
            IQuotationService quotations,
            ShareTokenService tokens,
            HttpContext http) =>
        {
            // تحقق سريع إن العرض موجود
            var q = await quotations.GetQuotationForEditAsync(id);
            if (q == null) return Results.NotFound(new { error = "عرض السعر غير موجود." });

            var validity = TimeSpan.FromDays(Math.Clamp(days ?? 30, 1, 90));
            var token = tokens.GenerateToken("quotation", id, validity);

            // ابني الرابط الكامل
            var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
            var publicUrl = $"{baseUrl}/q/{token}";

            return Results.Ok(new
            {
                token,
                publicUrl,
                pdfUrl = $"{baseUrl}/api/public/quotations/{token}/pdf",
                excelUrl = $"{baseUrl}/api/public/quotations/{token}/excel",
                expiresInDays = validity.TotalDays,
                expiresAt = DateTime.UtcNow.Add(validity)
            });
        }).RequireAuthorization();

        // ============================================================
        // المسارات العامة (بـ token موقّع، بدون login)
        // ============================================================
        app.MapGet("/api/public/quotations/{token}/info", async (
            string token,
            IQuotationService quotations,
            ShareTokenService tokens) =>
        {
            var payload = tokens.ValidateToken(token);
            if (payload == null || payload.Type != "quotation")
                return Results.NotFound(new { error = "الرابط غير صحيح أو منتهي الصلاحية." });

            var q = await quotations.GetQuotationForEditAsync(payload.Id);
            if (q == null) return Results.NotFound(new { error = "عرض السعر غير موجود." });

            return Results.Ok(new
            {
                referenceNumber = q.ReferenceNumber,
                date = q.QuotationDate,
                validUntil = q.ValidUntil,
                partyName = q.PartyName,
                grandTotal = q.GrandTotal,
                itemsCount = q.Items.Count,
                isExpired = q.IsExpired,
                isConverted = q.IsConverted
            });
        });

        app.MapGet("/api/public/quotations/{token}/pdf", async (
            string token,
            IQuotationExportService export,
            ShareTokenService tokens) =>
        {
            var payload = tokens.ValidateToken(token);
            if (payload == null || payload.Type != "quotation")
                return Results.NotFound("الرابط غير صحيح أو منتهي الصلاحية.");

            var (ok, error, pdf, fileName) = await export.GeneratePdfAsync(payload.Id);
            if (!ok || pdf == null)
                return Results.NotFound(error);

            // inline = يفتح في المتصفح مباشرة بدل ما يتنزّل
            return Results.File(pdf, "application/pdf", fileName,
                enableRangeProcessing: true);
        });

        app.MapGet("/api/public/quotations/{token}/excel", async (
            string token,
            IQuotationExportService export,
            ShareTokenService tokens) =>
        {
            var payload = tokens.ValidateToken(token);
            if (payload == null || payload.Type != "quotation")
                return Results.NotFound("الرابط غير صحيح أو منتهي الصلاحية.");

            var (ok, error, xlsx, fileName) = await export.GenerateExcelAsync(payload.Id);
            if (!ok || xlsx == null)
                return Results.NotFound(error);

            return Results.File(xlsx,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        });

        return app;
    }
}
