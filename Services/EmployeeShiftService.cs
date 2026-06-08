using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class EmployeeShiftService : IEmployeeShiftService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;

    public EmployeeShiftService(db24804Context db, IAuditService audit, NotificationService notify)
    {
        _db = db;
        _audit = audit;
        _notify = notify;
    }

    // ============================================================
    //  جلب قائمة الشيفتات
    // ============================================================
    public async Task<PagedResult<EmployeeShiftListDto>> GetShiftsAsync(EmployeeShiftFilterDto filter)
    {
        var query = _db.EmployeeShifts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.ShiftType))
            query = query.Where(s => s.ShiftType == filter.ShiftType);

        if (filter.EmployeeId.HasValue)
            query = query.Where(s => s.EmployeeId == filter.EmployeeId.Value);

        if (filter.EffectiveFrom.HasValue)
            query = query.Where(s => s.EffectiveFrom >= filter.EffectiveFrom.Value.Date);

        if (filter.EffectiveTo.HasValue)
            query = query.Where(s => s.EffectiveTo == null || s.EffectiveTo.Value <= filter.EffectiveTo.Value.Date);

        if (filter.ActiveOnly == true)
            query = query.Where(s => s.EffectiveTo == null || s.EffectiveTo.Value >= DateTime.Today);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
{
    var s = filter.SearchText.Trim();
    
    var matchingEmployeeIds = await _db.Employees.AsNoTracking()
        .Where(e => e.FullName.Contains(s)
            || (e.Department != null && e.Department.Contains(s))
            || (e.NationalId != null && e.NationalId.Contains(s))
            || (e.BioEmployeeId.HasValue && e.BioEmployeeId.ToString().Contains(s)))
        .Select(e => e.EmployeeId)
        .ToListAsync();

    if (matchingEmployeeIds.Any())
    {
        query = query.Where(sh => matchingEmployeeIds.Contains(sh.EmployeeId));
    }
    else
    {
        // لو مفيش نتائج - رجّع قائمة فاضية
        query = query.Where(sh => false);
    }
}

        var totalCount = await query.CountAsync();

        query = filter.SortDescending
            ? query.OrderByDescending(s => s.EffectiveFrom).ThenByDescending(s => s.EmployeeShiftId)
            : query.OrderBy(s => s.EffectiveFrom).ThenBy(s => s.EmployeeShiftId);

        var items = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(s => new EmployeeShiftListDto
            {
                EmployeeShiftId = s.EmployeeShiftId,
                EmployeeId = s.EmployeeId,
                BiometricCode = s.BiometricCode,
                ShiftType = s.ShiftType,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                EffectiveFrom = s.EffectiveFrom,
                EffectiveTo = s.EffectiveTo,
                CreatedBy = s.CreatedBy,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync();

        var employeeIds = items.Select(i => i.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.EmployeeId))
            .Select(e => new { e.EmployeeId, e.FullName, e.Department })
            .ToListAsync();

        foreach (var item in items)
        {
            var emp = employees.FirstOrDefault(e => e.EmployeeId == item.EmployeeId);
            item.EmployeeName = emp?.FullName ?? "غير معروف";
            item.Department = emp?.Department;
        }

        return new PagedResult<EmployeeShiftListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    // ============================================================
    //  جلب شيفت واحد
    // ============================================================
    public async Task<EmployeeShiftFormDto?> GetShiftByIdAsync(int shiftId)
    {
        var shift = await _db.EmployeeShifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EmployeeShiftId == shiftId);

        if (shift == null) return null;

        return new EmployeeShiftFormDto
        {
            EmployeeShiftId = shift.EmployeeShiftId,
            EmployeeId = shift.EmployeeId,
            BiometricCode = shift.BiometricCode,
            ShiftType = shift.ShiftType,
            StartTime = shift.StartTime,
            EndTime = shift.EndTime,
            EffectiveFrom = shift.EffectiveFrom,
            EffectiveTo = shift.EffectiveTo,
            OffDay1         = shift.OffDay1,   
            OffDay2         = shift.OffDay2
        };
    }

    // ============================================================
    //  بحث الموظفين
    // ============================================================
    public async Task<List<ShiftEmployeeLookupDto>> SearchEmployeesAsync(string search, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(search))
            return await _db.Employees.AsNoTracking()
                .Where(e => e.Status == "نشط")
                .OrderBy(e => e.FullName)
                .Take(20)
                .Select(e => new ShiftEmployeeLookupDto
                {
                    EmployeeId = e.EmployeeId,
                    FullName = e.FullName,
                    Department = e.Department,
                    BiometricCode = e.BioEmployeeId,
                    NationalId = e.NationalId
                })
                .ToListAsync(cancellationToken);

        var s = search.Trim();
        return await _db.Employees.AsNoTracking()
            .Where(e => e.Status == "نشط"
                && (e.FullName.ContainsArabic(s)
                    || (e.Department != null && e.Department.ContainsArabic(s))
                    || (e.NationalId != null && e.NationalId.Contains(s))))
            .OrderBy(e => e.FullName)
            .Take(20)
            .Select(e => new ShiftEmployeeLookupDto
            {
                EmployeeId = e.EmployeeId,
                FullName = e.FullName,
                Department = e.Department,
                BiometricCode = e.BioEmployeeId,
                NationalId = e.NationalId
            })
            .ToListAsync(cancellationToken);
    }

    // ============================================================
    //  شيفتات الموظف الحالي
    // ============================================================
    public async Task<List<EmployeeShiftListDto>> GetMyShiftsAsync(string userName)
{
    if (string.IsNullOrWhiteSpace(userName))
        return new List<EmployeeShiftListDto>();

    userName = userName.Trim();

    var appUser = await _db.Users.AsNoTracking()
        .FirstOrDefaultAsync(u => u.Username == userName);

    if (appUser == null || appUser.EmployeeId == null)
        return new List<EmployeeShiftListDto>();

    var employee = await _db.Employees.AsNoTracking()
        .FirstOrDefaultAsync(e => e.EmployeeId == appUser.EmployeeId.Value);

    if (employee == null)
        return new List<EmployeeShiftListDto>();

    return await _db.EmployeeShifts.AsNoTracking()
        .Where(s => s.EmployeeId == employee.EmployeeId)
        .OrderByDescending(s => s.EffectiveFrom)
        .Select(s => new EmployeeShiftListDto
        {
            EmployeeShiftId = s.EmployeeShiftId,
            EmployeeId = s.EmployeeId,
            EmployeeName = employee.FullName,
            Department = employee.Department,
            BiometricCode = s.BiometricCode,
            ShiftType = s.ShiftType,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            EffectiveFrom = s.EffectiveFrom,
            EffectiveTo = s.EffectiveTo,
            OffDay1         = s.OffDay1,   // ✅ الجديد
            OffDay2         = s.OffDay2, 
            CreatedBy = s.CreatedBy,
            CreatedAt = s.CreatedAt

        })
        .ToListAsync();
}

    // ============================================================
    //  إضافة شيفت
    // ============================================================
    public async Task<(bool Success, string Message)> AddShiftAsync(AddEmployeeShiftDto dto, string currentUserName)
    {
        if (!dto.EmployeeId.HasValue)
            return (false, "يرجى اختيار الموظف.");

        var employee = await _db.Employees.FindAsync(dto.EmployeeId.Value);
        if (employee == null)
            return (false, "الموظف غير موجود.");

        var hasConflict = await _db.EmployeeShifts
            .AnyAsync(s => s.EmployeeId == dto.EmployeeId.Value
                && s.EffectiveFrom <= dto.EffectiveFrom
                && (s.EffectiveTo == null || s.EffectiveTo >= dto.EffectiveFrom));

        if (hasConflict)
            return (false, "يوجد شيفت آخر نشط لهذا الموظف في نفس الفترة.");

        try
        {
            var shift = new EmployeeShift
            {
                EmployeeId = dto.EmployeeId.Value,
                BiometricCode = dto.BiometricCode ?? employee.BioEmployeeId,
                ShiftType = dto.ShiftType,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                EffectiveFrom = dto.EffectiveFrom,
                EffectiveTo = dto.EffectiveTo,
                OffDay1       = dto.OffDay1,   // ✅ الجديد
                OffDay2       = dto.OffDay2,
                CreatedBy = currentUserName,
                CreatedAt = DateTime.Now
            };

            _db.EmployeeShifts.Add(shift);
            await _db.SaveChangesAsync();

            await _audit.LogAsync<object?>("EmployeeShifts", "Insert",
                shift.EmployeeShiftId.ToString(), null,
                new { shift.EmployeeId, EmployeeName = employee.FullName, shift.ShiftType, shift.EffectiveFrom },
                currentUserName);

            await _notify.NotifyRoleAsync("⏰ شيفتات", $"تم إضافة شيفت {dto.ShiftType} للموظف {employee.FullName}",
                SystemRoles.Admin, currentUserName, "shifts", "EmployeeShifts", shift.EmployeeShiftId);

            return (true, $"تم إضافة الشيفت بنجاح للموظف {employee.FullName}");
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    // ============================================================
    //  تعديل شيفت
    // ============================================================
    public async Task<(bool Success, string Message)> UpdateShiftAsync(int shiftId, EmployeeShiftFormDto dto, string currentUserName)
    {
        var shift = await _db.EmployeeShifts.FindAsync(shiftId);
        if (shift == null)
            return (false, "الشيفت غير موجود.");

        try
        {
            shift.ShiftType = dto.ShiftType;
            shift.StartTime = dto.StartTime;
            shift.EndTime = dto.EndTime;
            shift.EffectiveFrom = dto.EffectiveFrom;
            shift.EffectiveTo = dto.EffectiveTo;
            if (dto.BiometricCode.HasValue)
                shift.BiometricCode = dto.BiometricCode;

            await _db.SaveChangesAsync();

            await _audit.LogAsync<object?>("EmployeeShifts", "Update",
                shiftId.ToString(), null,
                new { shift.ShiftType, shift.StartTime, shift.EndTime, shift.EffectiveFrom },
                currentUserName);

            return (true, "تم تعديل الشيفت بنجاح.");
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    // ============================================================
    //  حذف شيفت
    // ============================================================
    public async Task<(bool Success, string Message)> DeleteShiftAsync(int shiftId, string currentUserName)
    {
        var shift = await _db.EmployeeShifts.FindAsync(shiftId);
        if (shift == null)
            return (false, "الشيفت غير موجود.");

        try
        {
            _db.EmployeeShifts.Remove(shift);
            await _db.SaveChangesAsync();

            await _audit.LogAsync<object?>("EmployeeShifts", "Delete",
                shiftId.ToString(), new { shift.EmployeeId, shift.ShiftType, shift.EffectiveFrom }, null, currentUserName);

            return (true, "تم حذف الشيفت بنجاح.");
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    // ============================================================
    //  تصدير Excel
    // ============================================================
    public async Task<byte[]> ExportToExcelAsync(EmployeeShiftFilterDto filter)
    {
        var exportFilter = new EmployeeShiftFilterDto
        {
            SearchText = filter.SearchText,
            ShiftType = filter.ShiftType,
            EmployeeId = filter.EmployeeId,
            EffectiveFrom = filter.EffectiveFrom,
            EffectiveTo = filter.EffectiveTo,
            ActiveOnly = filter.ActiveOnly,
            PageNumber = 1,
            PageSize = 100000,
            SortBy = filter.SortBy,
            SortDescending = filter.SortDescending
        };

        var result = await GetShiftsAsync(exportFilter);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("شيفتات الموظفين");

        var headers = new[] { "م", "اسم الموظف", "القسم", "كود البصمة", "نوع الشيفت", "وقت البدء", "وقت الانتهاء", "المدة", "تاريخ البدء", "تاريخ الانتهاء", "الحالة" };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1A, 0x23, 0x7E);
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (int i = 0; i < result.Items.Count; i++)
        {
            var item = result.Items[i];
            var row = i + 2;
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = item.EmployeeName;
            ws.Cell(row, 3).Value = item.Department ?? "";
            ws.Cell(row, 4).Value = item.BiometricCode?.ToString() ?? "";
            ws.Cell(row, 5).Value = item.ShiftType;
            ws.Cell(row, 6).Value = item.StartTimeDisplay;
            ws.Cell(row, 7).Value = item.EndTimeDisplay;
            ws.Cell(row, 8).Value = item.DurationDisplay;
            ws.Cell(row, 9).Value = item.EffectiveFromDisplay;
            ws.Cell(row, 10).Value = item.EffectiveToDisplay ?? "مستمر";
            ws.Cell(row, 11).Value = item.IsActive ? "نشط" : "منتهي";

            for (int col = 1; col <= headers.Length; col++)
                ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ============================================================
//  استيراد Excel - مُحدّث
// ============================================================
public async Task<ShiftImportResultDto> ImportFromExcelAsync(Stream fileStream, string currentUserName)
{
    var result = new ShiftImportResultDto();

    try
    {
        using var workbook = new XLWorkbook(fileStream);
        var ws = workbook.Worksheet(1);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        if (lastRow < 2)
        {
            result.Errors.Add("الملف فارغ.");
            return result;
        }

        // ✅ جلب الموظفين النشطين بكود البصمة
        var activeEmployees = await _db.Employees.AsNoTracking()
            .Where(e => e.Status == "نشط" && e.BioEmployeeId.HasValue)
            .Select(e => new { e.EmployeeId, e.FullName, e.BioEmployeeId })
            .ToListAsync();

        var newShifts = new List<EmployeeShift>();

        for (int row = 2; row <= lastRow; row++)
        {
            result.TotalRows++;
            try
            {
                // ✅ القراءة بالترتيب الجديد
                var biometricCodeStr = ws.Cell(row, 1).GetString().Trim();
                var shiftType = ws.Cell(row, 2).GetString().Trim();
                var startTimeStr = ws.Cell(row, 3).GetString().Trim();
                var endTimeStr = ws.Cell(row, 4).GetString().Trim();
                var effectiveFromStr = ws.Cell(row, 5).GetString().Trim();
                var effectiveToStr = ws.Cell(row, 6).GetString().Trim();

                // Validation
                if (string.IsNullOrWhiteSpace(biometricCodeStr))
                {
                    result.Errors.Add($"صف {row}: كود البصمة مطلوب.");
                    continue;
                }

                if (!int.TryParse(biometricCodeStr, out var biometricCode))
                {
                    result.Errors.Add($"صف {row}: كود البصمة يجب أن يكون رقماً.");
                    continue;
                }

                if (shiftType != ShiftTypes.Morning && shiftType != ShiftTypes.Evening)
                {
                    result.Errors.Add($"صف {row}: نوع الشيفت يجب أن يكون (صباحي) أو (مسائي).");
                    continue;
                }

                // ✅ البحث بكود البصمة
                var emp = activeEmployees.FirstOrDefault(e => e.BioEmployeeId == biometricCode);
                if (emp == null)
                {
                    result.Errors.Add($"صف {row}: لا يوجد موظف بكود البصمة ({biometricCode}).");
                    continue;
                }

                // Parse times
                var startTime = TimeSpan.TryParse(startTimeStr, out var st)
                    ? TimeOnly.FromTimeSpan(st)
                    : TimeOnly.FromTimeSpan(ShiftTypes.DefaultStartTimes[shiftType]);
                    
                var endTime = TimeSpan.TryParse(endTimeStr, out var et)
                    ? TimeOnly.FromTimeSpan(et)
                    : TimeOnly.FromTimeSpan(ShiftTypes.DefaultEndTimes[shiftType]);

                // Parse effective from
                if (!DateTime.TryParse(effectiveFromStr, out var effectiveFrom))
                {
                    result.Errors.Add($"صف {row}: تاريخ البدء غير صحيح.");
                    continue;
                }

                // Parse effective to (optional)
                DateTime? effectiveTo = null;
                if (!string.IsNullOrWhiteSpace(effectiveToStr) && DateTime.TryParse(effectiveToStr, out var parsedEnd))
                    effectiveTo = parsedEnd;

                // ✅ Check for existing active shift and close it
                var existingActiveShift = await _db.EmployeeShifts
                    .FirstOrDefaultAsync(s => s.EmployeeId == emp.EmployeeId 
                        && (s.EffectiveTo == null || s.EffectiveTo >= DateTime.Today));

                if (existingActiveShift != null)
                {
                    existingActiveShift.EffectiveTo = effectiveFrom.AddDays(-1);
                    result.Warnings.Add($"صف {row}: تم إغلاق الشيفت السابق للموظف ({emp.FullName}).");
                }

                newShifts.Add(new EmployeeShift
                {
                    EmployeeId = emp.EmployeeId,
                    BiometricCode = biometricCode,
                    ShiftType = shiftType,
                    StartTime = startTime,
                    EndTime = endTime,
                    EffectiveFrom = effectiveFrom,
                    EffectiveTo = effectiveTo,
                    CreatedBy = currentUserName,
                    CreatedAt = DateTime.Now
                });

                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"صف {row}: {ex.Message}");
            }
        }

        if (newShifts.Any())
        {
            _db.EmployeeShifts.AddRange(newShifts);
            await _db.SaveChangesAsync();
        }

        result.ErrorCount = result.Errors.Count;
    }
    catch (Exception ex)
    {
        result.Errors.Add($"خطأ في الملف: {ex.Message}");
    }

    return result;
}

    // ============================================================
//  قالب الاستيراد - مُحدّث
// ============================================================
public async Task<byte[]> GetImportTemplateAsync()
{
    await Task.CompletedTask;

    using var workbook = new XLWorkbook();
    var ws = workbook.Worksheets.Add("قالب الاستيراد");

    // ✅ الأعمدة المُحدّثة - كود البصمة هو الأساسي
    var headers = new[] 
    { 
        "كود البصمة *", 
        "نوع الشيفت *", 
        "وقت البدء", 
        "وقت الانتهاء", 
        "تاريخ البدء *", 
        "تاريخ الانتهاء" 
    };

    // Header Row
    for (int i = 0; i < headers.Length; i++)
    {
        var cell = ws.Cell(1, i + 1);
        cell.Value = headers[i];
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1A, 0x23, 0x7E);
        cell.Style.Font.FontColor = XLColor.White;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    // ✅ بيانات نموذجية
    // Row 2
    ws.Cell(2, 1).Value = 101;  // كود البصمة
    ws.Cell(2, 2).Value = ShiftTypes.Morning;
    ws.Cell(2, 3).Value = "10:00";
    ws.Cell(2, 4).Value = "18:00";
    ws.Cell(2, 5).Value = DateTime.Today.ToString("yyyy/MM/dd");
    ws.Cell(2, 6).Value = ""; // مستمر

    // Row 3
    ws.Cell(3, 1).Value = 102;
    ws.Cell(3, 2).Value = ShiftTypes.Evening;
    ws.Cell(3, 3).Value = "14:00";
    ws.Cell(3, 4).Value = "22:00";
    ws.Cell(3, 5).Value = DateTime.Today.ToString("yyyy/MM/dd");
    ws.Cell(3, 6).Value = DateTime.Today.AddMonths(3).ToString("yyyy/MM/dd");

    // ✅ إضافة sheet التعليمات
    var wsInstructions = workbook.Worksheets.Add("تعليمات");
    wsInstructions.Cell(1, 1).Value = "تعليمات الاستيراد";
    wsInstructions.Cell(1, 1).Style.Font.Bold = true;
    wsInstructions.Cell(1, 1).Style.Font.FontSize = 14;

    wsInstructions.Cell(3, 1).Value = "1. كود البصمة: مطلوب - رقم كود البصمة الخاص بالموظف";
    wsInstructions.Cell(4, 1).Value = "2. نوع الشيفت: مطلوب - (صباحي أو مسائي)";
    wsInstructions.Cell(5, 1).Value = "3. وقت البدء: اختياري - الصيغة: HH:mm (مثال: 10:00)";
    wsInstructions.Cell(6, 1).Value = "4. وقت الانتهاء: اختياري - الصيغة: HH:mm (مثال: 18:00)";
    wsInstructions.Cell(7, 1).Value = "5. تاريخ البدء: مطلوب - الصيغة: yyyy/MM/dd";
    wsInstructions.Cell(8, 1).Value = "6. تاريخ الانتهاء: اختياري - اتركه فارغاً للشيفت المستمر";

    wsInstructions.Cell(10, 1).Value = "ملاحظات:";
    wsInstructions.Cell(10, 1).Style.Font.Bold = true;
    wsInstructions.Cell(11, 1).Value = "• إذا لم تحدد وقت البدء والانتهاء سيتم استخدام الأوقات الافتراضية حسب نوع الشيفت";
    wsInstructions.Cell(12, 1).Value = "• صباحى: 10:00 - 18:00";
    wsInstructions.Cell(13, 1).Value = "• مسائى: 14:00 - 22:00";

    wsInstructions.Columns().AdjustToContents();

    // Adjust columns
    ws.Columns().AdjustToContents();
    ws.SheetView.FreezeRows(1);
    ws.RightToLeft = true;

    using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    return stream.ToArray();
}
    public async Task<ShiftStatisticsDto> GetStatisticsAsync()
{
    var today = DateTime.Today;
    
    var stats = await _db.EmployeeShifts.AsNoTracking()
        .GroupBy(s => 1)
        .Select(g => new ShiftStatisticsDto
        {
            TotalCount = g.Count(),
            ActiveCount = g.Count(s => s.EffectiveTo == null || s.EffectiveTo >= today),
            MorningCount = g.Count(s => s.ShiftType == ShiftTypes.Morning),
            EveningCount = g.Count(s => s.ShiftType == ShiftTypes.Evening),
            DailyCount = g.Count(s => s.ShiftType == ShiftTypes.DailyWork)
        })
        .FirstOrDefaultAsync();
    
    return stats ?? new ShiftStatisticsDto();
}
}