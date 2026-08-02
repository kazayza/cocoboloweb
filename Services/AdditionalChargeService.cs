using COCOBOLOERPNEW.Components;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class AdditionalChargeService : IAdditionalChargeService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _http;

    private async Task<int> GetDefaultCashBoxIdAsync()
    {
        var cashBox = await _db.CashBoxes.AsNoTracking().FirstOrDefaultAsync();
        return cashBox?.CashBoxId ?? 1;
    }

    public AdditionalChargeService(db24804Context db, IAuditService audit, IPaymentService paymentService, IHttpContextAccessor http)
    {
        _db = db;
        _audit = audit;
        _paymentService = paymentService;
        _http = http;
    }

    public async Task<PagedResult<AdditionalChargeListDto>> GetChargesAsync(AdditionalChargeFilterDto filter)
{
    // جلب البيانات بدون TryGetValue في الـ Expression
    var charges = await (from c in _db.AdditionalCharges.AsNoTracking()
                         join p in _db.Parties.AsNoTracking() on c.PartyId equals p.PartyId into pp
                         from p in pp.DefaultIfEmpty()
                         join t in _db.Transactions.AsNoTracking() on c.TransactionId equals t.TransactionId into tt
                         from t in tt.DefaultIfEmpty()
                         join at in _db.Transactions.AsNoTracking() on c.AppliedToTransactionId equals at.TransactionId into att
                         from at in att.DefaultIfEmpty()
                         select new AdditionalChargeListDto
                         {
                             ChargeId = c.ChargeId,
                             ChargeType = c.ChargeType,
                             ChargeTypeName = c.ChargeType,
                             ChargeDescription = c.ChargeDescription,
                             ChargeAmount = c.ChargeAmount ?? 0,
                             Status = c.Status,
                             StatusName = c.Status,
                             PartyId = c.PartyId,
                             PartyName = p != null ? p.PartyName : null,
                             PartyPhone = p != null ? p.Phone : null,
                             TransactionId = c.TransactionId,
                             TransactionRef = t != null ? t.ReferenceNumber : null,
                             AppliedToTransactionId = c.AppliedToTransactionId,
                             AppliedToTransactionRef = at != null ? at.ReferenceNumber : null,
                             Notes = c.Notes,
                             CreatedBy = c.CreatedBy,
                             CreatedAt = c.CreatedAt
                         }).OrderByDescending(x => x.CreatedAt).ToListAsync();

    // ⭐ تحويل الأسماء بعد التحميل
    foreach (var item in charges)
    {
        item.ChargeTypeName = ChargeTypes.All.TryGetValue(item.ChargeType ?? "", out var ct) ? ct : item.ChargeType;
        item.StatusName = ChargeStatuses.All.TryGetValue(item.Status ?? "", out var st) ? st : item.Status;
    }

    // فلاتر
    if (!string.IsNullOrWhiteSpace(filter.SearchText))
{
    var s = filter.SearchText.Trim();
    charges = charges.Where(x =>
        (x.PartyName ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) ||
        (x.PartyPhone ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) ||
        (x.ChargeDescription ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) ||
        (x.TransactionRef ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) ||
        (x.AppliedToTransactionRef ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
}

    if (!string.IsNullOrWhiteSpace(filter.ChargeType))
        charges = charges.Where(x => x.ChargeType == filter.ChargeType).ToList();

    if (!string.IsNullOrWhiteSpace(filter.Status))
        charges = charges.Where(x => x.Status == filter.Status).ToList();

    if (filter.DateFrom.HasValue)
        charges = charges.Where(x => x.CreatedAt >= filter.DateFrom.Value).ToList();

    if (filter.DateTo.HasValue)
        charges = charges.Where(x => x.CreatedAt <= filter.DateTo.Value.AddDays(1)).ToList();

    var total = charges.Count;
    var items = charges
        .Skip((filter.PageNumber - 1) * filter.PageSize)
        .Take(filter.PageSize)
        .ToList();

    return new PagedResult<AdditionalChargeListDto>
    {
        Items = items,
        TotalCount = total,
        PageNumber = filter.PageNumber,
        PageSize = filter.PageSize
    };
}

    public async Task<AdditionalChargeStatsDto> GetStatsAsync()
    {
        var charges = await _db.AdditionalCharges.AsNoTracking().ToListAsync();

        return new AdditionalChargeStatsDto
        {
            TotalAmount = charges.Sum(c => c.ChargeAmount ?? 0),
            PaidAmount = charges.Where(c => c.Status == ChargeStatuses.Paid).Sum(c => c.ChargeAmount ?? 0),
            AppliedAmount = charges.Where(c => c.Status == ChargeStatuses.Applied).Sum(c => c.ChargeAmount ?? 0),
            NonRefundableAmount = charges.Where(c => c.Status == ChargeStatuses.NonRefundable).Sum(c => c.ChargeAmount ?? 0),
            PendingAmount = charges.Where(c => c.Status == ChargeStatuses.Paid).Sum(c => c.ChargeAmount ?? 0),
            TotalCount = charges.Count
        };
    }

    public async Task<AdditionalChargeFormDto?> GetChargeForEditAsync(int chargeId)
    {
        var dto = await _db.AdditionalCharges.AsNoTracking()
            .Where(c => c.ChargeId == chargeId)
            .Select(c => new AdditionalChargeFormDto
            {
                ChargeId = c.ChargeId,
                PartyId = c.PartyId,
                ChargeType = c.ChargeType,
                ChargeDescription = c.ChargeDescription,
                ChargeAmount = c.ChargeAmount ?? 0,
                Status = c.Status,
                Notes = c.Notes
            }).FirstOrDefaultAsync();

        if (dto == null) return null;

        dto.CashBoxId = await _db.CashboxTransactions.AsNoTracking()
            .Where(ct => ct.ReferenceId == chargeId && ct.ReferenceType == "Charge")
            .OrderByDescending(ct => ct.CashboxTransactionId)
            .Select(ct => (int?)ct.CashBoxId)
            .FirstOrDefaultAsync();

        return dto;
    }

    public async Task<AdditionalChargeReceiptDto?> GetChargeReceiptAsync(int chargeId)
    {
        var charge = await _db.AdditionalCharges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChargeId == chargeId);
        if (charge == null) return null;

        var party = charge.PartyId.HasValue
            ? await _db.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.PartyId == charge.PartyId.Value)
            : null;

        Transaction? appliedTransaction = null;
        if (charge.AppliedToTransactionId.HasValue)
        {
            appliedTransaction = await _db.Transactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TransactionId == charge.AppliedToTransactionId.Value);
        }
        else if (charge.TransactionId.HasValue)
        {
            appliedTransaction = await _db.Transactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TransactionId == charge.TransactionId.Value);
        }

        var cashRef = await (from ct in _db.CashboxTransactions.AsNoTracking()
                             join cb in _db.CashBoxes.AsNoTracking() on ct.CashBoxId equals cb.CashBoxId
                             where ct.ReferenceId == charge.ChargeId && ct.ReferenceType == "Charge"
                             orderby ct.CashboxTransactionId descending
                             select new { ct.CashBoxId, cb.CashBoxName, ct.TransactionDate })
            .FirstOrDefaultAsync();

        var company = await _db.CompanyInfos.AsNoTracking().FirstOrDefaultAsync();
        var chargeTypeName = ChargeTypes.All.TryGetValue(charge.ChargeType ?? "", out var mappedType)
            ? mappedType
            : ResolveAdvanceReceiptCategory(charge.ChargeDescription, charge.ChargeType);
        var statusName = ChargeStatuses.All.TryGetValue(charge.Status ?? "", out var mappedStatus)
            ? mappedStatus
            : charge.Status ?? "—";
        var receiptDate = cashRef?.TransactionDate ?? charge.CreatedAt ?? DateTime.Now;

        var createdByDisplayName = charge.CreatedBy;
        if (!string.IsNullOrWhiteSpace(charge.CreatedBy))
        {
            var userInfo = await (from u in _db.Users.AsNoTracking()
                                  join e in _db.Employees.AsNoTracking() on u.EmployeeId equals e.EmployeeId into ee
                                  from e in ee.DefaultIfEmpty()
                                  where u.Username == charge.CreatedBy
                                  select new
                                  {
                                      EmployeeName = e != null ? e.FullName : null,
                                      UserFullName = u.FullName
                                  })
                .FirstOrDefaultAsync();

            createdByDisplayName = !string.IsNullOrWhiteSpace(userInfo?.EmployeeName)
                ? userInfo!.EmployeeName
                : (!string.IsNullOrWhiteSpace(userInfo?.UserFullName) ? userInfo!.UserFullName : charge.CreatedBy);
        }

        return new AdditionalChargeReceiptDto
        {
            ChargeId = charge.ChargeId,
            ReceiptNumber = GenerateChargeReceiptNumber(charge.ChargeId, charge.ChargeType, charge.ChargeDescription, receiptDate),
            ReceiptTitle = ResolveReceiptTitle(charge.ChargeDescription, charge.ChargeType),
            ReceiptCategoryLabel = chargeTypeName,
            ReceiptTypeAr = "سند قبض",
            CompanyName = company?.CompanyName ?? "COCOBOLO",
            CompanyPhone = string.Join(" - ", new[] { company?.Phone1, company?.Phone2 }.Where(x => !string.IsNullOrWhiteSpace(x))),
            CompanyAddress = company?.Address,
            CompanyTaxNumber = null,
            CompanyLogoPath = company?.LogoPath,
            PartyId = charge.PartyId,
            PartyName = party?.PartyName ?? "غير محدد",
            PartyPhone = party?.Phone,
            CustomerAddress = party?.Address,
            CustomerCity = party?.City,
            CustomerEmail = party?.Email,
            ChargeType = charge.ChargeType,
            ChargeTypeName = chargeTypeName,
            ChargeDescription = charge.ChargeDescription,
            ChargeAmount = charge.ChargeAmount ?? 0,
            AmountInWords = _paymentService.ConvertNumberToArabicWords(charge.ChargeAmount ?? 0),
            Status = charge.Status,
            StatusName = statusName,
            Notes = charge.Notes,
            AppliedToTransactionId = charge.AppliedToTransactionId ?? charge.TransactionId,
            AppliedToReferenceNumber = appliedTransaction?.ReferenceNumber,
            CashBoxId = cashRef?.CashBoxId,
            CashBoxName = cashRef?.CashBoxName,
            ReceiptDate = receiptDate,
            CreatedBy = charge.CreatedBy,
            CreatedByDisplayName = createdByDisplayName
        };
    }

    public async Task<(bool Success, string Message, int? ChargeId)> CreateChargeAsync(AdditionalChargeFormDto dto, string currentUserName)
{
    if (dto.ChargeAmount <= 0)
        return (false, "المبلغ يجب أن يكون أكبر من صفر.", null);

    if (dto.PartyId == null || dto.PartyId == 0)
        return (false, "يرجى اختيار العميل.", null);

    var partyName = await _db.Parties.Where(p => p.PartyId == dto.PartyId)
        .Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";

    var chargeTypeName = ChargeTypes.All.TryGetValue(dto.ChargeType ?? "", out var ct) ? ct : "رسوم";

    // ⭐ الحالة الافتراضية = مدفوعة
    var charge = new AdditionalCharge
    {
        PartyId = dto.PartyId,
        ChargeType = dto.ChargeType,
        ChargeDescription = dto.ChargeDescription,
        ChargeAmount = dto.ChargeAmount,
        Status = ChargeStatuses.Paid,
        Notes = dto.Notes,
        CreatedBy = currentUserName,
        CreatedAt = DateTime.Now
    };

    _db.AdditionalCharges.Add(charge);
    await _db.SaveChangesAsync();

    // ⭐ إضافة حركة الخزينة
    var cashBoxId = dto.CashBoxId ?? await GetDefaultCashBoxIdAsync();
    var cashNote = $"تحصيل {dto.ChargeAmount:N2} ج - {chargeTypeName} - {partyName}";

    var cashTrans = new CashboxTransaction
    {
        CashBoxId = cashBoxId,
        ReferenceId = charge.ChargeId,
        ReferenceType = "Charge",
        TransactionType = "قبض",
        Amount = dto.ChargeAmount,
        TransactionDate = DateTime.Now,
        Notes = cashNote,
        CreatedBy = currentUserName,
        CreatedAt = DateTime.Now
    };
    _db.CashboxTransactions.Add(cashTrans);
    await _db.SaveChangesAsync();

    await _audit.LogAsync("AdditionalCharges", "Insert",
        charge.ChargeId.ToString(), null, charge, currentUserName);

    return (true, "تم إضافة الرسوم وتسجيل التحصيل في الخزينة بنجاح.", charge.ChargeId);
}

    public async Task<(bool Success, string Message)> UpdateChargeAsync(int chargeId, AdditionalChargeFormDto dto, string currentUserName)
{
    var charge = await _db.AdditionalCharges.FirstOrDefaultAsync(c => c.ChargeId == chargeId);
    if (charge == null) return (false, "الرسوم غير موجودة.");

    if (!CanEditUnlinkedCharge())
        return (false, "ليس لديك صلاحية تعديل الرسوم. التعديل متاح فقط للإدارة أو الحسابات.");

    if (IsChargeLocked(charge))
        return (false, "لا يمكن تعديل رسوم مرتبطة أو مطبقة على فاتورة أو محددة كغير مستردة.");

    var oldData = new { charge.ChargeType, charge.ChargeAmount, charge.Status };

    charge.ChargeType = dto.ChargeType;
    charge.ChargeDescription = dto.ChargeDescription;
    charge.ChargeAmount = dto.ChargeAmount;
    charge.Status = dto.Status ?? charge.Status;
    charge.Notes = dto.Notes;

    // ⭐ تحديث حركة الخزينة
    var cashTrans = await _db.CashboxTransactions
        .FirstOrDefaultAsync(ct => ct.ReferenceId == chargeId && ct.ReferenceType == "Charge");

    if (cashTrans != null)
    {
        var partyName = await _db.Parties.Where(p => p.PartyId == charge.PartyId)
            .Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";
        var chargeTypeName = ChargeTypes.All.TryGetValue(dto.ChargeType ?? "", out var ct) ? ct : dto.ChargeType ?? "رسوم";

        cashTrans.Amount = dto.ChargeAmount;
        cashTrans.Notes = $"تحصيل {dto.ChargeAmount:N2} ج - {chargeTypeName} - {partyName}";
        if (dto.CashBoxId.HasValue && dto.CashBoxId.Value > 0)
            cashTrans.CashBoxId = dto.CashBoxId.Value;
    }

    await _db.SaveChangesAsync();

    await _audit.LogAsync("AdditionalCharges", "Update",
        chargeId.ToString(), oldData, new { charge.ChargeType, charge.ChargeAmount, charge.Status }, currentUserName);

    return (true, "تم تعديل الرسوم وتحديث الخزينة بنجاح.");
}

    public async Task<(bool Success, string Message)> DeleteChargeAsync(int chargeId, string currentUserName)
{
    var charge = await _db.AdditionalCharges.FirstOrDefaultAsync(c => c.ChargeId == chargeId);
    if (charge == null) return (false, "الرسوم غير موجودة.");

    if (!CanDeleteUnlinkedCharge())
        return (false, "ليس لديك صلاحية حذف الرسوم. الحذف متاح فقط للإدارة أو مدير الحسابات.");

    if (IsChargeLocked(charge))
        return (false, "لا يمكن حذف رسوم مرتبطة أو مطبقة على فاتورة أو محددة كغير مستردة.");

    // ⭐ حذف حركة الخزينة المرتبطة
    var cashTrans = await _db.CashboxTransactions
        .Where(ct => ct.ReferenceId == chargeId && ct.ReferenceType == "Charge")
        .ToListAsync();
    _db.CashboxTransactions.RemoveRange(cashTrans);

    _db.AdditionalCharges.Remove(charge);
    await _db.SaveChangesAsync();

    await _audit.LogAsync("AdditionalCharges", "Delete",
        chargeId.ToString(), charge, null, currentUserName);

    return (true, "تم حذف الرسوم وإلغاء حركة الخزينة بنجاح.");
}

    public async Task<(bool Success, string Message)> ApplyToInvoiceAsync(int chargeId, int transactionId, string currentUserName)
{
    var charge = await _db.AdditionalCharges.FirstOrDefaultAsync(c => c.ChargeId == chargeId);
    if (charge == null) return (false, "الرسوم غير موجودة.");

    if (charge.Status != ChargeStatuses.Paid)
        return (false, "يمكن تطبيق رسوم مدفوعة فقط.");

    var transaction = await _db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == transactionId);
    if (transaction == null) return (false, "الفاتورة غير موجودة.");

    // ⭐ معاينة → تزود المدفوع
    if (charge.ChargeType == ChargeTypes.Inspection)
    {
        transaction.PaidAmount += charge.ChargeAmount ?? 0;
    }
    // ⭐ شحن/تركيب/أخرى → تزود الإجمالي
    else
    {
        transaction.GrandTotal += charge.ChargeAmount ?? 0;
        transaction.TotalChargesAmount += charge.ChargeAmount ?? 0m;
    }

    // ⭐ تحديث حالة الفاتورة
    if (transaction.PaidAmount >= transaction.GrandTotal && transaction.GrandTotal > 0)
        transaction.InvoiceStatus = "Paid";
    else if (transaction.PaidAmount > 0)
        transaction.InvoiceStatus = "PartiallyPaid";

    // ⭐ ربط الرسوم بالفاتورة
    charge.Status = ChargeStatuses.Applied;
    charge.AppliedToTransactionId = transactionId;
    charge.TransactionId = transactionId;

    await _db.SaveChangesAsync();

    await _audit.LogAsync("AdditionalCharges", "Apply",
        chargeId.ToString(), null, new { transactionId, charge.ChargeType }, currentUserName);

    var typeLabel = charge.ChargeType == ChargeTypes.Inspection ? "مدفوع" : "الإجمالي";
    return (true, $"تم تطبيق الرسوم على فاتورة {transaction.ReferenceNumber} (أُضيف على {typeLabel}) بنجاح.");
}

   public async Task<(bool Success, string Message)> MarkAsNonRefundableAsync(int chargeId, string reason, string currentUserName)
{
    var charge = await _db.AdditionalCharges.FirstOrDefaultAsync(c => c.ChargeId == chargeId);
    if (charge == null) return (false, "الرسوم غير موجودة.");

    if (charge.Status == ChargeStatuses.Applied)
        return (false, "لا يمكن تغيير رسوم مطبقة.");

    charge.Status = ChargeStatuses.NonRefundable;
    if (!string.IsNullOrWhiteSpace(reason))
        charge.Notes = string.IsNullOrWhiteSpace(charge.Notes)
            ? $"غير مستردة: {reason}"
            : $"{charge.Notes} | غير مستردة: {reason}";

    // ⭐ الفلوس فضلت في الخزينة - بس نحدّث الملاحظة
    var cashTrans = await _db.CashboxTransactions
        .FirstOrDefaultAsync(ct => ct.ReferenceId == chargeId && ct.ReferenceType == "Charge");

    if (cashTrans != null)
    {
        cashTrans.Notes += $" | غير مستردة: {reason}";
    }

    await _db.SaveChangesAsync();

    await _audit.LogAsync("AdditionalCharges", "NonRefundable",
        chargeId.ToString(), null, new { reason }, currentUserName);

    return (true, "تم تحديد الرسوم كغير مستردة.");
}

    private bool CanEditUnlinkedCharge()
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return false;
        return user.IsInRole(SystemRoles.Admin)
               || user.IsInRole(SystemRoles.AccountManager)
               || user.IsInRole(SystemRoles.Account);
    }

    private bool CanDeleteUnlinkedCharge()
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return false;
        return user.IsInRole(SystemRoles.Admin)
               || user.IsInRole(SystemRoles.AccountManager);
    }

    private static bool IsChargeLocked(AdditionalCharge charge)
        => charge.TransactionId.HasValue
           || charge.AppliedToTransactionId.HasValue
           || charge.Status == ChargeStatuses.Applied
           || charge.Status == ChargeStatuses.NonRefundable;

    private static string ResolveAdvanceReceiptCategory(string? description, string? chargeType)
    {
        if (chargeType == ChargeTypes.Inspection || (description?.Contains("معاينة") ?? false))
            return "رسوم معاينة";

        if (description?.Contains("عربون") ?? false)
            return "عربون";

        if ((description?.Contains("دفعة") ?? false) || (description?.Contains("مقدمة") ?? false))
            return "دفعة مقدمة";

        return "رسوم إضافية";
    }

    private static string ResolveReceiptTitle(string? description, string? chargeType)
    {
        if (chargeType == ChargeTypes.Inspection || (description?.Contains("معاينة") ?? false))
            return "إيصال استلام رسوم معاينة";

        if (description?.Contains("عربون") ?? false)
            return "إيصال استلام عربون";

        if ((description?.Contains("دفعة") ?? false) || (description?.Contains("مقدمة") ?? false))
            return "إيصال استلام دفعة مقدمة";

        return "إيصال استلام رسوم إضافية";
    }

    private static string GenerateChargeReceiptNumber(int chargeId, string? chargeType, string? description, DateTime receiptDate)
    {
        var prefix = chargeType == ChargeTypes.Inspection || (description?.Contains("معاينة") ?? false)
            ? "INS"
            : ((description?.Contains("دفعة") ?? false) || (description?.Contains("عربون") ?? false) || (description?.Contains("مقدمة") ?? false)
                ? "ADV"
                : "CHR");

        return $"{prefix}-{receiptDate.Year}-{chargeId:D6}";
    }
}