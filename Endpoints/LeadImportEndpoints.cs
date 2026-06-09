using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace COCOBOLOERPNEW.Endpoints;

public static class LeadImportEndpoints
{
    public static void MapLeadImportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/leads")
            .WithTags("Lead Import");

        // ═══════════════════════════════════════════════════════════
        // استيراد Lead واحد (Real-Time من Apps Script onEdit)
        // ═══════════════════════════════════════════════════════════
        group.MapPost("/import", async (
            LeadImportRequest request,
            IAuditService auditService,
            IConfiguration config,
            db24804Context db) =>
        {
            // 1. التحقق من الـ API Key
            var apiKey = config["LeadImport:ApiKey"];
            if (string.IsNullOrEmpty(apiKey) || request.ApiKey != apiKey)
                return Results.Json(new LeadImportResult
                {
                    Success = false,
                    Message = "API Key غير صالح"
                }, statusCode: 401);

            // 2. تنظيف الموبايل
            request.Phone = CleanMetaPhone(request.Phone);
            if (!string.IsNullOrWhiteSpace(request.Phone2))
                request.Phone2 = CleanMetaPhone(request.Phone2);

            // 3. التحقق من البيانات المطلوبة
            if (string.IsNullOrWhiteSpace(request.FullName))
                return Results.Json(new LeadImportResult
                {
                    Success = false,
                    Message = "اسم العميل مطلوب",
                    SheetTabName = request.SheetTabName,
                    SheetRowNumber = request.SheetRowNumber
                }, statusCode: 400);

            if (string.IsNullOrWhiteSpace(request.Phone))
                return Results.Json(new LeadImportResult
                {
                    Success = false,
                    Message = "رقم الهاتف مطلوب",
                    SheetTabName = request.SheetTabName,
                    SheetRowNumber = request.SheetRowNumber
                }, statusCode: 400);

            // 4. التحقق من التكرار في جدول LeadsCRM
            var existingLead = await db.LeadsCRMs
                .AnyAsync(l => l.Phone == request.Phone);

            if (existingLead)
            {
                return Results.Ok(new LeadImportResult
                {
                    Success = true,
                    Message = "العميل موجود بالفعل في Leads - تم تخطيه",
                    IsDuplicate = true,
                    SheetTabName = request.SheetTabName,
                    SheetRowNumber = request.SheetRowNumber
                });
            }

            // 5. ترجمة أسئلة الفورم
            var projectType = MetaFormTranslations.Translate(
                MetaFormTranslations.ProjectTypeMap, request.ProjectType);
            var projectStage = MetaFormTranslations.Translate(
                MetaFormTranslations.ProjectStageMap, request.ProjectStage);
            var budget = MetaFormTranslations.Translate(
                MetaFormTranslations.BudgetMap, request.Budget);
            var decisionMaker = MetaFormTranslations.Translate(
                MetaFormTranslations.DecisionMakerMap, request.DecisionMaker);
            var nextAction = MetaFormTranslations.Translate(
                MetaFormTranslations.NextActionMap, request.NextAction);
            var bestTime = MetaFormTranslations.Translate(
                MetaFormTranslations.BestTimeMap, request.BestTimeToReach);

            // 6. إنشاء سجل في LeadsCRM فقط
            var lead = new LeadsCrm
            {
                FullName = request.FullName.Trim(),
                Phone = request.Phone.Trim(),
                Phone2 = request.Phone2?.Trim(),
                Email = request.Email?.Trim(),
                City = request.City?.Trim(),
                Area = request.Area?.Trim(),
                Address = BuildAddress(request),
                //MetaLeadId = request.LeadId?.ToString(),
                CampaignName = request.CampaignName?.Trim(),
                AdName = request.AdName?.Trim(),
                //AdSetName = request.AdSet?.Trim(),
                FormName = request.FormName?.Trim(),
                FormId = request.FormId?.Trim(),
                Platform = request.Platform?.Trim(),
                ProjectType = projectType,
                ProjectStage = projectStage,
                Budget = budget,
                DecisionMaker = decisionMaker,
                NextAction = nextAction,
                BestTimeToReach = bestTime,
                LeadDate = ParseLeadDate(request.LeadDate) ?? DateTime.Now,
                //LeadStatus = "New",
                LeadStatus = "جديد",  // ✅ عربي زي باقي السيستم
                SheetTabName = request.SheetTabName,
                SheetRowNumber = request.SheetRowNumber,
                Notes = BuildSummary(request, projectType, projectStage,
                    budget, decisionMaker, nextAction, bestTime),
                CreatedBy = "MetaIntegration"
            };

            db.LeadsCRMs.Add(lead);
            await db.SaveChangesAsync();

            await auditService.LogAsync("LeadsCRM", "Created",
                lead.LeadId.ToString(), null, lead, "MetaIntegration");

            return Results.Ok(new LeadImportResult
            {
                Success = true,
                Message = "تم استيراد Lead جديد بنجاح - في انتظار المراجعة",
                IsDuplicate = false,
                PartyId = lead.LeadId,
                SheetTabName = request.SheetTabName,
                SheetRowNumber = request.SheetRowNumber
            });
        });

        // ═══════════════════════════════════════════════════════════
        // استيراد دفعة من Leads
        // ═══════════════════════════════════════════════════════════
        group.MapPost("/import-batch", async (
            BatchLeadImportRequest batchRequest,
            IAuditService auditService,
            IConfiguration config,
            db24804Context db) =>
        {
            var apiKey = config["LeadImport:ApiKey"];
            if (string.IsNullOrEmpty(apiKey) || batchRequest.ApiKey != apiKey)
                return Results.Json(new BatchLeadImportResult
                {
                    TotalReceived = batchRequest.Leads.Count,
                    TotalFailed = batchRequest.Leads.Count
                }, statusCode: 401);

            var result = new BatchLeadImportResult
            {
                TotalReceived = batchRequest.Leads.Count
            };

            foreach (var req in batchRequest.Leads)
            {
                try
                {
                    req.Phone = CleanMetaPhone(req.Phone);
                    if (!string.IsNullOrWhiteSpace(req.Phone2))
                        req.Phone2 = CleanMetaPhone(req.Phone2);

                    if (string.IsNullOrWhiteSpace(req.FullName) ||
                        string.IsNullOrWhiteSpace(req.Phone))
                    {
                        result.TotalFailed++;
                        result.Results.Add(new LeadImportResult
                        {
                            Success = false,
                            Message = "بيانات ناقصة",
                            SheetTabName = req.SheetTabName,
                            SheetRowNumber = req.SheetRowNumber
                        });
                        continue;
                    }

                    var exists = await db.LeadsCRMs
                        .AnyAsync(l => l.Phone == req.Phone);
                    if (exists)
                    {
                        result.TotalDuplicates++;
                        result.Results.Add(new LeadImportResult
                        {
                            Success = true,
                            Message = "مكرر - تم تخطيه",
                            IsDuplicate = true,
                            SheetTabName = req.SheetTabName,
                            SheetRowNumber = req.SheetRowNumber
                        });
                        continue;
                    }

                    var projectType = MetaFormTranslations.Translate(
                        MetaFormTranslations.ProjectTypeMap, req.ProjectType);
                    var projectStage = MetaFormTranslations.Translate(
                        MetaFormTranslations.ProjectStageMap, req.ProjectStage);
                    var budget = MetaFormTranslations.Translate(
                        MetaFormTranslations.BudgetMap, req.Budget);
                    var decisionMaker = MetaFormTranslations.Translate(
                        MetaFormTranslations.DecisionMakerMap, req.DecisionMaker);
                    var nextAction = MetaFormTranslations.Translate(
                        MetaFormTranslations.NextActionMap, req.NextAction);
                    var bestTime = MetaFormTranslations.Translate(
                        MetaFormTranslations.BestTimeMap, req.BestTimeToReach);

                    var lead = new LeadsCrm
                    {
                        FullName = req.FullName.Trim(),
                        Phone = req.Phone.Trim(),
                        Phone2 = req.Phone2?.Trim(),
                        Email = req.Email?.Trim(),
                        City = req.City?.Trim(),
                        Area = req.Area?.Trim(),
                        Address = BuildAddress(req),
                        MetaLeadId = req.LeadId?.ToString(),
                        CampaignName = req.CampaignName?.Trim(),
                        AdName = req.AdName?.Trim(),
                        AdSetName = req.AdSet?.Trim(),
                        FormName = req.FormName?.Trim(),
                        FormId = req.FormId?.Trim(),
                        Platform = req.Platform?.Trim(),
                        ProjectType = projectType,
                        ProjectStage = projectStage,
                        Budget = budget,
                        DecisionMaker = decisionMaker,
                        NextAction = nextAction,
                        BestTimeToReach = bestTime,
                        LeadDate = ParseLeadDate(req.LeadDate) ?? DateTime.Now,
                        LeadStatus = "جديد",
                        SheetTabName = req.SheetTabName,
                        SheetRowNumber = req.SheetRowNumber,
                        Notes = BuildSummary(req, projectType, projectStage,
                            budget, decisionMaker, nextAction, bestTime),
                        CreatedBy = "MetaIntegration"
                    };

                    db.LeadsCRMs.Add(lead);
                    await db.SaveChangesAsync();

                    result.TotalCreated++;
                    result.Results.Add(new LeadImportResult
                    {
                        Success = true,
                        Message = "تم الاستيراد بنجاح",
                        PartyId = lead.LeadId,
                        SheetTabName = req.SheetTabName,
                        SheetRowNumber = req.SheetRowNumber
                    });
                }
                catch (Exception ex)
                {
                    result.TotalFailed++;
                    result.Results.Add(new LeadImportResult
                    {
                        Success = false,
                        Message = $"خطأ: {ex.Message}",
                        SheetTabName = req.SheetTabName,
                        SheetRowNumber = req.SheetRowNumber
                    });
                }
            }

            return Results.Ok(result);
        });

        // ═══════════════════════════════════════════════════════════
        // اختبار الاتصال
        // ═══════════════════════════════════════════════════════════
        group.MapGet("/ping", (IConfiguration config) =>
        {
            var apiKey = config["LeadImport:ApiKey"];
            return Results.Ok(new
            {
                Status = "OK",
                Service = "COCOBOLO Lead Import",
                Configured = !string.IsNullOrEmpty(apiKey),
                Time = DateTime.Now
            });
        });
    }

    // ═══════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════

    private static string CleanMetaPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        var cleaned = phone.Trim();
        if (cleaned.StartsWith("p:", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned.Substring(2);
        cleaned = cleaned.Replace(" ", "").Replace("-", "");
        if (cleaned.StartsWith("01") && cleaned.Length == 11)
            cleaned = "+2" + cleaned;
        return cleaned;
    }
    private static DateTime? ParseLeadDate(string? dateStr)
{
    if (string.IsNullOrWhiteSpace(dateStr)) return null;

    // لو ISO format: 2026-06-09 أو 2026-06-09T14:30:00
    if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        return DateTime.SpecifyKind(iso, DateTimeKind.Local);

    if (DateTime.TryParseExact(dateStr, "yyyy-MM-ddTHH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoFull))
        return DateTime.SpecifyKind(isoFull, DateTimeKind.Local);

    // لو MM/dd/yyyy (أمريكي - الشهر الأول)
    if (DateTime.TryParseExact(dateStr, "MM/dd/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var usDate))
        return DateTime.SpecifyKind(usDate, DateTimeKind.Local);

    // لو dd/MM/yyyy (مصري/أوروبي - اليوم الأول)
    if (DateTime.TryParseExact(dateStr, "dd/MM/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var euDate))
        return DateTime.SpecifyKind(euDate, DateTimeKind.Local);

    // لو dd-MM-yyyy
    if (DateTime.TryParseExact(dateStr, "dd-MM-yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dashDate))
        return DateTime.SpecifyKind(dashDate, DateTimeKind.Local);

    // Fallback: جرّب بأي طريقة
    if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback))
        return DateTime.SpecifyKind(fallback, DateTimeKind.Local);

    return null;
}

    private static string? BuildAddress(LeadImportRequest request)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.City)) parts.Add(request.City.Trim());
        if (!string.IsNullOrWhiteSpace(request.Area)) parts.Add(request.Area.Trim());
        if (!string.IsNullOrWhiteSpace(request.Address)) parts.Add(request.Address.Trim());
        return parts.Count > 0 ? string.Join(" - ", parts) : null;
    }

    private static string BuildSummary(LeadImportRequest request,
        string projectType, string projectStage, string budget,
        string decisionMaker, string nextAction, string bestTime)
    {
        var parts = new List<string>();
        parts.Add("تلقائي من إعلان Meta");
        if (!string.IsNullOrWhiteSpace(request.CampaignName))
            parts.Add($"كامبين: {request.CampaignName}");
        if (!string.IsNullOrWhiteSpace(request.AdName))
            parts.Add($"إعلان: {request.AdName}");
        if (!string.IsNullOrWhiteSpace(request.AdSet))
            parts.Add($"مجموعة: {request.AdSet}");
        if (!string.IsNullOrWhiteSpace(request.FormName))
            parts.Add($"فورم: {request.FormName}");
        if (!string.IsNullOrWhiteSpace(request.Platform))
            parts.Add($"منصة: {request.Platform}");
        if (!string.IsNullOrEmpty(projectType))
            parts.Add($"نوع المشروع: {projectType}");
        if (!string.IsNullOrEmpty(projectStage))
            parts.Add($"المرحلة: {projectStage}");
        if (!string.IsNullOrEmpty(budget))
            parts.Add($"الميزانية: {budget}");
        if (!string.IsNullOrEmpty(decisionMaker))
            parts.Add($"صاحب القرار: {decisionMaker}");
        if (!string.IsNullOrEmpty(nextAction))
            parts.Add($"الاحتياج: {nextAction}");
        if (!string.IsNullOrEmpty(bestTime))
            parts.Add($"أفضل وقت: {bestTime}");
        return string.Join(" | ", parts);
    }
}