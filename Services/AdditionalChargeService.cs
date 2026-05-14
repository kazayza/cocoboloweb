using COCOBOLOERPNEW.Components;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class AdditionalChargeService : IAdditionalChargeService
{
    private readonly db24804Context _db;
private readonly IAuditService _audit;

    private async Task<int> GetDefaultCashBoxIdAsync()
{
    var cashBox = await _db.CashBoxes.FirstOrDefaultAsync();
    return cashBox?.CashBoxId ?? 1;
}

    public AdditionalChargeService(db24804Context db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
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
        return await _db.AdditionalCharges.AsNoTracking()
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
    }

    public async Task<(bool Success, string Message)> CreateChargeAsync(AdditionalChargeFormDto dto, string currentUserName)
{
    if (dto.ChargeAmount <= 0)
        return (false, "المبلغ يجب أن يكون أكبر من صفر.");

    if (dto.PartyId == null || dto.PartyId == 0)
        return (false, "يرجى اختيار العميل.");

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
    var cashBoxId = await GetDefaultCashBoxIdAsync();
    var cashNote = $"تحصيل {dto.ChargeAmount:N2} ج - {chargeTypeName} - {partyName}";

    var cashTrans = new CashboxTransaction
    {
        CashBoxId = cashBoxId,
        ReferenceId = charge.ChargeId,
        ReferenceType = "AdditionalCharge",
        TransactionType = "In",
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

    return (true, "تم إضافة الرسوم وتسجيل التحصيل في الخزينة بنجاح.");
}

    public async Task<(bool Success, string Message)> UpdateChargeAsync(int chargeId, AdditionalChargeFormDto dto, string currentUserName)
{
    var charge = await _db.AdditionalCharges.FirstOrDefaultAsync(c => c.ChargeId == chargeId);
    if (charge == null) return (false, "الرسوم غير موجودة.");

    if (charge.Status == ChargeStatuses.Applied)
        return (false, "لا يمكن تعديل رسوم مطبقة على فاتورة.");

    if (charge.Status == ChargeStatuses.NonRefundable)
        return (false, "لا يمكن تعديل رسوم غير مستردة.");

    var oldData = new { charge.ChargeType, charge.ChargeAmount, charge.Status };

    charge.ChargeType = dto.ChargeType;
    charge.ChargeDescription = dto.ChargeDescription;
    charge.ChargeAmount = dto.ChargeAmount;
    charge.Status = dto.Status ?? charge.Status;
    charge.Notes = dto.Notes;

    // ⭐ تحديث حركة الخزينة
    var cashTrans = await _db.CashboxTransactions
        .FirstOrDefaultAsync(ct => ct.ReferenceId == chargeId && ct.ReferenceType == "AdditionalCharge");

    if (cashTrans != null)
    {
        var partyName = await _db.Parties.Where(p => p.PartyId == charge.PartyId)
            .Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";
        var chargeTypeName = ChargeTypes.All.TryGetValue(dto.ChargeType ?? "", out var ct) ? ct : dto.ChargeType ?? "رسوم";

        cashTrans.Amount = dto.ChargeAmount;
        cashTrans.Notes = $"تحصيل {dto.ChargeAmount:N2} ج - {chargeTypeName} - {partyName}";
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

    if (charge.Status == ChargeStatuses.Applied)
        return (false, "لا يمكن حذف رسوم مطبقة على فاتورة.");

    if (charge.Status == ChargeStatuses.NonRefundable)
        return (false, "لا يمكن حذف رسوم غير مستردة.");

    // ⭐ حذف حركة الخزينة المرتبطة
    var cashTrans = await _db.CashboxTransactions
        .Where(ct => ct.ReferenceId == chargeId && ct.ReferenceType == "AdditionalCharge")
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
        .FirstOrDefaultAsync(ct => ct.ReferenceId == chargeId && ct.ReferenceType == "AdditionalCharge");

    if (cashTrans != null)
    {
        var partyName = await _db.Parties.Where(p => p.PartyId == charge.PartyId)
            .Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";
        cashTrans.Notes += $" | غير مستردة: {reason}";
    }

    await _db.SaveChangesAsync();

    await _audit.LogAsync("AdditionalCharges", "NonRefundable",
        chargeId.ToString(), null, new { reason }, currentUserName);

    return (true, "تم تحديد الرسوم كغير مستردة.");
}
}