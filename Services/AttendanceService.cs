using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class AttendanceService : IAttendanceService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;

    public AttendanceService(db24804Context db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ════════════════════════════════════════════════════════════
    //  جلب قائمة الحضور
    // ════════════════════════════════════════════════════════════
    public async Task<PagedResult<AttendanceListDto>> GetAttendanceAsync(AttendanceFilterDto filter)
    {
        var query = _db.Attendances.AsNoTracking().AsQueryable();

        // === الفلاتر ===
        if (filter.DateFrom.HasValue)
            query = query.Where(a => a.LogDate >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
            query = query.Where(a => a.LogDate <= filter.DateTo.Value.Date);

        if (filter.BiometricCode.HasValue)
            query = query.Where(a => a.BiometricCode == filter.BiometricCode.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(a => a.Status == filter.Status);

        if (filter.LateOnly == true)
            query = query.Where(a => a.LateMinutes > 0);

        if (filter.AbsentOnly == true)
            query = query.Where(a => a.Status == AttendanceStatus.Absent || (a.TimeIn == null && a.TimeOut == null));

        // === البحث بالاسم أو القسم ===
        List<int>? matchingBiometricCodes = null;
        if (!string.IsNullOrWhiteSpace(filter.SearchText) || 
            !string.IsNullOrWhiteSpace(filter.Department) ||
            filter.EmployeeId.HasValue)
        {
            var employeesQuery = _db.Employees.AsNoTracking().Where(e => e.BioEmployeeId.HasValue);

            if (!string.IsNullOrWhiteSpace(filter.SearchText))
            {
                var searchText = filter.SearchText.Trim();
                employeesQuery = employeesQuery.Where(e =>
                    e.FullName.Contains(searchText) ||
                    (e.Department != null && e.Department.Contains(searchText)) ||
                    (e.NationalId != null && e.NationalId.Contains(searchText)));
            }

            if (!string.IsNullOrWhiteSpace(filter.Department))
                employeesQuery = employeesQuery.Where(e => e.Department == filter.Department);

            if (filter.EmployeeId.HasValue)
                employeesQuery = employeesQuery.Where(e => e.EmployeeId == filter.EmployeeId.Value);

            matchingBiometricCodes = await employeesQuery
                .Where(e => e.BioEmployeeId.HasValue)
                .Select(e => e.BioEmployeeId!.Value)
                .ToListAsync();

            if (matchingBiometricCodes.Any())
                query = query.Where(a => matchingBiometricCodes.Contains(a.BiometricCode));
            else
                return new PagedResult<AttendanceListDto> { Items = new(), TotalCount = 0 };
        }

        // === العدد الكلي ===
        var totalCount = await query.CountAsync();

        // === الترتيب ===
        query = filter.SortDescending
            ? query.OrderByDescending(a => a.LogDate).ThenByDescending(a => a.AttendanceId)
            : query.OrderBy(a => a.LogDate).ThenBy(a => a.AttendanceId);

        // === الصفحات ===
        var items = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(a => new AttendanceListDto
            {
                AttendanceId = a.AttendanceId,
                BiometricCode = a.BiometricCode,
                LogDate = a.LogDate,
                TimeIn = a.TimeIn,
                TimeOut = a.TimeOut,
                Status = a.Status,
                TotalHours = a.TotalHours,
                LateMinutes = a.LateMinutes,
                EarlyLeaveMinutes = a.EarlyLeaveMinutes,
                PenaltyHours = a.PenaltyHours
            })
            .ToListAsync();

        // === ربط بيانات الموظفين ===
        await FillEmployeeData(items);

        return new PagedResult<AttendanceListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    // ════════════════════════════════════════════════════════════
    //  جلب سجل حضور واحد
    // ════════════════════════════════════════════════════════════
    public async Task<AttendanceListDto?> GetAttendanceByIdAsync(int attendanceId)
    {
        var attendance = await _db.Attendances.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AttendanceId == attendanceId);

        if (attendance == null) return null;

        var dto = new AttendanceListDto
        {
            AttendanceId = attendance.AttendanceId,
            BiometricCode = attendance.BiometricCode,
            LogDate = attendance.LogDate,
            TimeIn = attendance.TimeIn,
            TimeOut = attendance.TimeOut,
            Status = attendance.Status,
            TotalHours = attendance.TotalHours,
            LateMinutes = attendance.LateMinutes,
            EarlyLeaveMinutes = attendance.EarlyLeaveMinutes,
            PenaltyHours = attendance.PenaltyHours
        };

        await FillEmployeeData(new List<AttendanceListDto> { dto });
        return dto;
    }

    // ════════════════════════════════════════════════════════════
    //  الإحصائيات
    // ════════════════════════════════════════════════════════════
    public async Task<AttendanceStatisticsDto> GetStatisticsAsync(AttendanceFilterDto filter)
    {
        var query = _db.Attendances.AsNoTracking().AsQueryable();

        if (filter.DateFrom.HasValue)
            query = query.Where(a => a.LogDate >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
            query = query.Where(a => a.LogDate <= filter.DateTo.Value.Date);

        if (filter.BiometricCode.HasValue)
            query = query.Where(a => a.BiometricCode == filter.BiometricCode.Value);

        var records = await query.ToListAsync();

        if (!records.Any())
            return new AttendanceStatisticsDto();

        var totalRecords = records.Count;
        var totalEmployees = records.Select(r => r.BiometricCode).Distinct().Count();
        var totalDays = records.Select(r => r.LogDate.Date).Distinct().Count();
        
        var presentCount = records.Count(a => a.TimeIn != null);
        var absentCount = records.Count(a => a.TimeIn == null && a.TimeOut == null);
        var lateCount = records.Count(a => a.LateMinutes > 0);
        var earlyLeaveCount = records.Count(a => a.EarlyLeaveMinutes > 0);
        
        var totalHours = records.Sum(a => a.TotalHours ?? 0);
        var totalLateMinutes = records.Sum(a => a.LateMinutes ?? 0);
        var totalPenaltyHours = records.Sum(a => a.PenaltyHours ?? 0);

        return new AttendanceStatisticsDto
        {
            TotalRecords = totalRecords,
            TotalEmployees = totalEmployees,
            TotalDays = totalDays,
            PresentCount = presentCount,
            AbsentCount = absentCount,
            LateCount = lateCount,
            EarlyLeaveCount = earlyLeaveCount,
            TotalHours = totalHours,
            AverageHours = presentCount > 0 ? Math.Round(totalHours / presentCount, 1) : 0,
            AverageHoursPerEmployee = totalEmployees > 0 ? Math.Round(totalHours / totalEmployees, 1) : 0,
            TotalLateMinutes = totalLateMinutes,
            TotalPenaltyHours = totalPenaltyHours,
            AttendanceRate = totalRecords > 0 ? Math.Round((decimal)presentCount / totalRecords * 100, 1) : 0,
            AbsenceRate = totalRecords > 0 ? Math.Round((decimal)absentCount / totalRecords * 100, 1) : 0,
            LateRate = presentCount > 0 ? Math.Round((decimal)lateCount / presentCount * 100, 1) : 0
        };
    }
    public async Task<AttendanceStatisticsDto> GetTodayStatisticsAsync()
    {
        var today = DateTime.Today;
        
        var todayRecords = await _db.Attendances.AsNoTracking()
            .Where(a => a.LogDate == today)
            .ToListAsync();

        // عدد الموظفين النشطين
        var activeEmployees = await _db.Employees.AsNoTracking()
            .CountAsync(e => e.Status == "نشط" && e.BioEmployeeId.HasValue);

        var presentCount = todayRecords.Count(a => a.TimeIn != null);
        var lateCount = todayRecords.Count(a => a.LateMinutes > 0);
        var absentCount = activeEmployees - presentCount;

        return new AttendanceStatisticsDto
        {
            TotalRecords = todayRecords.Count,
            TotalEmployees = activeEmployees,
            TotalDays = 1,
            PresentCount = presentCount,
            AbsentCount = Math.Max(0, absentCount),
            LateCount = lateCount,
            TodayPresent = presentCount,
            TodayAbsent = Math.Max(0, absentCount),
            TodayLate = lateCount,
            TotalHours = todayRecords.Sum(a => a.TotalHours ?? 0),
            AverageHours = presentCount > 0 ? Math.Round(todayRecords.Sum(a => a.TotalHours ?? 0) / presentCount, 1) : 0,
            AttendanceRate = activeEmployees > 0 ? Math.Round((decimal)presentCount / activeEmployees * 100, 1) : 0,
            LateRate = presentCount > 0 ? Math.Round((decimal)lateCount / presentCount * 100, 1) : 0
        };
    }

    // ════════════════════════════════════════════════════════════
    //  Dashboard Data
    // ════════════════════════════════════════════════════════════
    public async Task<AttendanceDashboardDto> GetDashboardDataAsync(DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        var fromDate = dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var toDate = dateTo ?? DateTime.Today;

        var dashboard = new AttendanceDashboardDto();

        // 1. الإحصائيات العامة
        dashboard.Statistics = await GetStatisticsAsync(new AttendanceFilterDto
        {
            DateFrom = fromDate,
            DateTo = toDate
        });

        // إضافة إحصائيات اليوم
        var todayStats = await GetTodayStatisticsAsync();
        dashboard.Statistics.TodayPresent = todayStats.TodayPresent;
        dashboard.Statistics.TodayAbsent = todayStats.TodayAbsent;
        dashboard.Statistics.TodayLate = todayStats.TodayLate;

        // 2. الاتجاه اليومي
        var dailyData = await _db.Attendances.AsNoTracking()
            .Where(a => a.LogDate >= fromDate && a.LogDate <= toDate)
            .GroupBy(a => a.LogDate)
            .Select(g => new DailyAttendanceSummary
            {
                Date = g.Key,
                PresentCount = g.Count(a => a.TimeIn != null),
                AbsentCount = g.Count(a => a.TimeIn == null),
                LateCount = g.Count(a => a.LateMinutes > 0)
            })
            .OrderBy(d => d.Date)
            .ToListAsync();

        foreach (var day in dailyData)
        {
            day.DayName = day.Date.ToString("ddd", new System.Globalization.CultureInfo("ar-EG"));
            var total = day.PresentCount + day.AbsentCount;
            day.AttendanceRate = total > 0 ? Math.Round((decimal)day.PresentCount / total * 100, 1) : 0;
        }
        dashboard.DailyTrend = dailyData;

        // 3. حسب القسم
        var employeesWithDept = await _db.Employees.AsNoTracking()
            .Where(e => e.Status == "نشط" && e.BioEmployeeId.HasValue)
            .Select(e => new { e.BioEmployeeId, e.Department })
            .ToListAsync();

        var attendanceInPeriod = await _db.Attendances.AsNoTracking()
            .Where(a => a.LogDate >= fromDate && a.LogDate <= toDate)
            .ToListAsync();

        var byDepartment = employeesWithDept
            .GroupBy(e => e.Department ?? "بدون قسم")
            .Select(g =>
            {
                var deptCodes = g.Select(e => e.BioEmployeeId!.Value).ToList();
                var deptAttendance = attendanceInPeriod.Where(a => deptCodes.Contains(a.BiometricCode)).ToList();
                var presentCount = deptAttendance.Count(a => a.TimeIn != null);
                var totalRecords = deptAttendance.Count;

                return new DepartmentAttendanceSummary
                {
                    Department = g.Key,
                    EmployeeCount = g.Count(),
                    PresentCount = presentCount,
                    AbsentCount = totalRecords - presentCount,
                    AttendanceRate = totalRecords > 0 ? Math.Round((decimal)presentCount / totalRecords * 100, 1) : 0,
                    AverageHours = presentCount > 0 ? Math.Round(deptAttendance.Sum(a => a.TotalHours ?? 0) / presentCount, 1) : 0
                };
            })
            .OrderByDescending(d => d.AttendanceRate)
            .ToList();

        dashboard.ByDepartment = byDepartment;

        // 4. أفضل الموظفين
        var employeeRanks = await GetEmployeeRanksAsync(fromDate, toDate);
        dashboard.TopEmployees = employeeRanks.OrderByDescending(e => e.AttendanceRate).Take(5).ToList();
        dashboard.BottomEmployees = employeeRanks.Where(e => e.AttendanceRate < 100).OrderBy(e => e.AttendanceRate).Take(5).ToList();

        // 5. آخر البصمات
        dashboard.RecentLogs = await GetRecentLogsAsync();

        // 6. توزيع ساعات اليوم
        dashboard.TodayHourlyDistribution = await GetTodayHourlyDistributionAsync();

        return dashboard;
    }

    private async Task<List<EmployeeAttendanceRank>> GetEmployeeRanksAsync(DateTime fromDate, DateTime toDate)
    {
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.Status == "نشط" && e.BioEmployeeId.HasValue)
            .Select(e => new { e.EmployeeId, e.FullName, e.Department, e.BioEmployeeId })
            .ToListAsync();

        var attendanceData = await _db.Attendances.AsNoTracking()
            .Where(a => a.LogDate >= fromDate && a.LogDate <= toDate)
            .ToListAsync();

        var workingDays = GetWorkingDays(fromDate, toDate, false);

        var ranks = employees.Select(emp =>
        {
            var empAttendance = attendanceData.Where(a => a.BiometricCode == emp.BioEmployeeId!.Value).ToList();
            var presentDays = empAttendance.Count(a => a.TimeIn != null);

            return new EmployeeAttendanceRank
            {
                EmployeeId = emp.EmployeeId,
                EmployeeName = emp.FullName,
                Department = emp.Department,
                PresentDays = presentDays,
                AbsentDays = workingDays - presentDays,
                LateDays = empAttendance.Count(a => a.LateMinutes > 0),
                AttendanceRate = workingDays > 0 ? Math.Round((decimal)presentDays / workingDays * 100, 1) : 0,
                TotalHours = empAttendance.Sum(a => a.TotalHours ?? 0)
            };
        }).ToList();

        return ranks;
    }

    private async Task<List<RecentAttendanceLog>> GetRecentLogsAsync()
    {
        var today = DateTime.Today;
        
        var logs = await _db.BiometricLogs.AsNoTracking()
            .Where(b => b.LogDate == today)
            .OrderByDescending(b => b.LogTime)
            .Take(10)
            .ToListAsync();

        var biometricCodes = logs.Select(l => l.BiometricCode).Distinct().ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.BioEmployeeId.HasValue && biometricCodes.Contains(e.BioEmployeeId.Value))
            .Select(e => new { e.BioEmployeeId, e.FullName, e.Department })
            .ToListAsync();

        var result = logs.Select(l =>
        {
            var emp = employees.FirstOrDefault(e => e.BioEmployeeId == l.BiometricCode);
            var isFirstLog = !logs.Any(x => x.BiometricCode == l.BiometricCode && x.LogTime < l.LogTime);
            
            return new RecentAttendanceLog
            {
                BiometricCode = l.BiometricCode,
                EmployeeName = emp?.FullName ?? $"كود: {l.BiometricCode}",
                Department = emp?.Department,
                LogTime = l.LogTime,
                LogType = isFirstLog ? "حضور" : "انصراف",
                IsLate = isFirstLog && l.LogTime > new TimeOnly(8, 0)
            };
        }).ToList();

        return result;
    }

    private async Task<List<HourlyDistribution>> GetTodayHourlyDistributionAsync()
    {
        var today = DateTime.Today;
        
        var logs = await _db.BiometricLogs.AsNoTracking()
            .Where(b => b.LogDate == today)
            .ToListAsync();

        var distribution = Enumerable.Range(6, 12) // من 6 صباحاً حتى 6 مساءً
            .Select(hour => new HourlyDistribution
            {
                Hour = hour,
                Count = logs.Count(l => l.LogTime.Hour == hour)
            })
            .ToList();

        return distribution;
    }

    // ════════════════════════════════════════════════════════════
    //  تعديل سجل حضور
    // ════════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message)> UpdateAttendanceAsync(AttendanceEditDto dto, string currentUserName)
    {
        var attendance = await _db.Attendances.FindAsync(dto.AttendanceId);
        if (attendance == null)
            return (false, "سجل الحضور غير موجود.");

        try
        {
            var oldData = new
            {
                attendance.TimeIn,
                attendance.TimeOut,
                attendance.Status,
                attendance.TotalHours,
                attendance.LateMinutes
            };

            // تحديث البيانات
            attendance.TimeIn = dto.TimeIn;
            attendance.TimeOut = dto.TimeOut;
            attendance.Status = dto.Status;

            // إعادة حساب الساعات
            if (dto.TimeIn.HasValue && dto.TimeOut.HasValue)
            {
                var start = dto.TimeIn.Value.ToTimeSpan();
                var end = dto.TimeOut.Value.ToTimeSpan();
                var duration = end > start ? end - start : TimeSpan.FromHours(24) - start + end;
                attendance.TotalHours = (decimal)duration.TotalHours;
            }
            else
            {
                attendance.TotalHours = null;
            }

            await _db.SaveChangesAsync();

            // تسجيل في الـ Audit
            await _audit.LogAsync<object?>("Attendance", "Update",
                dto.AttendanceId.ToString(),
                oldData,
                new
                {
                    dto.TimeIn,
                    dto.TimeOut,
                    dto.Status,
                    attendance.TotalHours,
                    dto.EditReason
                },
                currentUserName);

            return (true, "تم تعديل سجل الحضور بنجاح.");
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════
    //  حذف سجل حضور
    // ════════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message)> DeleteAttendanceAsync(int attendanceId, string currentUserName)
    {
        var attendance = await _db.Attendances.FindAsync(attendanceId);
        if (attendance == null)
            return (false, "سجل الحضور غير موجود.");

        try
        {
            var oldData = new
            {
                attendance.BiometricCode,
                attendance.LogDate,
                attendance.TimeIn,
                attendance.TimeOut,
                attendance.Status
            };

            _db.Attendances.Remove(attendance);
            await _db.SaveChangesAsync();

            await _audit.LogAsync<object?>("Attendance", "Delete",
                attendanceId.ToString(), oldData, null, currentUserName);

            return (true, "تم حذف سجل الحضور بنجاح.");
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════
    //  تقرير الحضور الشهري
    // ════════════════════════════════════════════════════════════
    public async Task<List<AttendanceReportDto>> GetAttendanceReportAsync(AttendanceReportFilterDto filter)
    {
        // جلب الموظفين
        var employeesQuery = _db.Employees.AsNoTracking()
            .Where(e => e.Status == "نشط" && e.BioEmployeeId.HasValue);

        if (filter.EmployeeId.HasValue)
            employeesQuery = employeesQuery.Where(e => e.EmployeeId == filter.EmployeeId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Department))
            employeesQuery = employeesQuery.Where(e => e.Department == filter.Department);

        var employees = await employeesQuery
            .Select(e => new { e.EmployeeId, e.FullName, e.Department, e.BioEmployeeId })
            .ToListAsync();

        // جلب سجلات الحضور
        var biometricCodes = employees.Select(e => e.BioEmployeeId!.Value).ToList();
        
        var attendanceRecords = await _db.Attendances.AsNoTracking()
            .Where(a => a.LogDate >= filter.DateFrom.Date && 
                        a.LogDate <= filter.DateTo.Date &&
                        biometricCodes.Contains(a.BiometricCode))
            .ToListAsync();

        // حساب أيام العمل
        var workingDays = GetWorkingDays(filter.DateFrom, filter.DateTo, filter.IncludeWeekends);

        var report = new List<AttendanceReportDto>();

        foreach (var emp in employees)
        {
            var empAttendance = attendanceRecords
                .Where(a => a.BiometricCode == emp.BioEmployeeId!.Value)
                .ToList();

            var dto = new AttendanceReportDto
            {
                EmployeeId = emp.EmployeeId,
                BiometricCode = emp.BioEmployeeId!.Value,
                EmployeeName = emp.FullName,
                Department = emp.Department,
                WorkingDays = workingDays,
                PresentDays = empAttendance.Count(a => a.TimeIn != null),
                AbsentDays = workingDays - empAttendance.Count(a => a.TimeIn != null),
                LateDays = empAttendance.Count(a => a.LateMinutes > 0),
                EarlyLeaveDays = empAttendance.Count(a => a.EarlyLeaveMinutes > 0),
                TotalHours = empAttendance.Sum(a => a.TotalHours ?? 0),
                RequiredHours = workingDays * 8, // افتراضي 8 ساعات
                TotalLateMinutes = empAttendance.Sum(a => a.LateMinutes ?? 0),
                TotalEarlyLeaveMinutes = empAttendance.Sum(a => a.EarlyLeaveMinutes ?? 0),
                TotalPenaltyHours = empAttendance.Sum(a => a.PenaltyHours ?? 0)
            };

            dto.OvertimeHours = Math.Max(0, dto.TotalHours - dto.RequiredHours);
            dto.ShortageHours = Math.Max(0, dto.RequiredHours - dto.TotalHours);

            report.Add(dto);
        }

        return report.OrderBy(r => r.EmployeeName).ToList();
    }

    // ════════════════════════════════════════════════════════════
    //  تصدير الحضور إلى Excel
    // ════════════════════════════════════════════════════════════
    public async Task<byte[]> ExportAttendanceToExcelAsync(AttendanceFilterDto filter)
    {
        var exportFilter = new AttendanceFilterDto
        {
            DateFrom = filter.DateFrom,
            DateTo = filter.DateTo,
            BiometricCode = filter.BiometricCode,
            Department = filter.Department,
            Status = filter.Status,
            SearchText = filter.SearchText,
            PageNumber = 1,
            PageSize = 100000
        };

        var result = await GetAttendanceAsync(exportFilter);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("سجل الحضور");

        // Headers
        var headers = new[] { "م", "الموظف", "القسم", "كود البصمة", "التاريخ", "اليوم", "الحضور", "الانصراف", "الساعات", "التأخير", "خروج مبكر", "الحالة" };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1A, 0x23, 0x7E);
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Data
        for (int i = 0; i < result.Items.Count; i++)
        {
            var item = result.Items[i];
            var row = i + 2;

            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = item.EmployeeName;
            ws.Cell(row, 3).Value = item.Department ?? "";
            ws.Cell(row, 4).Value = item.BiometricCode;
            ws.Cell(row, 5).Value = item.LogDateDisplay;
            ws.Cell(row, 6).Value = item.DayName;
            ws.Cell(row, 7).Value = item.TimeInDisplay;
            ws.Cell(row, 8).Value = item.TimeOutDisplay;
            ws.Cell(row, 9).Value = item.TotalHoursDisplay;
            ws.Cell(row, 10).Value = item.LateDisplay;
            ws.Cell(row, 11).Value = item.EarlyLeaveDisplay;
            ws.Cell(row, 12).Value = item.Status ?? "";

            // تلوين الحالة
            if (item.IsAbsent)
                ws.Cell(row, 12).Style.Font.FontColor = XLColor.Red;
            else if (item.IsLate)
                ws.Cell(row, 12).Style.Font.FontColor = XLColor.Orange;

            for (int col = 1; col <= headers.Length; col++)
                ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
        ws.RightToLeft = true;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ════════════════════════════════════════════════════════════
    //  تصدير التقرير إلى Excel
    // ════════════════════════════════════════════════════════════
    public async Task<byte[]> ExportReportToExcelAsync(AttendanceReportFilterDto filter)
    {
        var report = await GetAttendanceReportAsync(filter);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("تقرير الحضور");

        // العنوان
        ws.Cell(1, 1).Value = $"تقرير الحضور والانصراف من {filter.DateFrom:yyyy/MM/dd} إلى {filter.DateTo:yyyy/MM/dd}";
        ws.Range(1, 1, 1, 12).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Headers
        var headers = new[] { "م", "الموظف", "القسم", "أيام العمل", "حضور", "غياب", "تأخير", "خروج مبكر", "الساعات", "نسبة الحضور", "دقائق التأخير", "الجزاءات" };
        var headerRow = 3;

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1A, 0x23, 0x7E);
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Data
        for (int i = 0; i < report.Count; i++)
        {
            var item = report[i];
            var row = headerRow + i + 1;

            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = item.EmployeeName;
            ws.Cell(row, 3).Value = item.Department ?? "";
            ws.Cell(row, 4).Value = item.WorkingDays;
            ws.Cell(row, 5).Value = item.PresentDays;
            ws.Cell(row, 6).Value = item.AbsentDays;
            ws.Cell(row, 7).Value = item.LateDays;
            ws.Cell(row, 8).Value = item.EarlyLeaveDays;
            ws.Cell(row, 9).Value = Math.Round(item.TotalHours, 1);
            ws.Cell(row, 10).Value = $"{item.AttendancePercentage}%";
            ws.Cell(row, 11).Value = item.TotalLateMinutes;
            ws.Cell(row, 12).Value = Math.Round(item.TotalPenaltyHours, 2);

            // تلوين نسبة الحضور
            if (item.AttendancePercentage < 80)
                ws.Cell(row, 10).Style.Font.FontColor = XLColor.Red;
            else if (item.AttendancePercentage < 95)
                ws.Cell(row, 10).Style.Font.FontColor = XLColor.Orange;
            else
                ws.Cell(row, 10).Style.Font.FontColor = XLColor.Green;

            for (int col = 1; col <= headers.Length; col++)
                ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // الإجماليات
        var totalRow = headerRow + report.Count + 1;
        ws.Cell(totalRow, 1).Value = "الإجمالي";
        ws.Cell(totalRow, 5).Value = report.Sum(r => r.PresentDays);
        ws.Cell(totalRow, 6).Value = report.Sum(r => r.AbsentDays);
        ws.Cell(totalRow, 7).Value = report.Sum(r => r.LateDays);
        ws.Cell(totalRow, 9).Value = Math.Round(report.Sum(r => r.TotalHours), 1);
        ws.Cell(totalRow, 11).Value = report.Sum(r => r.TotalLateMinutes);
        ws.Row(totalRow).Style.Font.Bold = true;
        ws.Row(totalRow).Style.Fill.BackgroundColor = XLColor.LightGray;

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(headerRow);
        ws.RightToLeft = true;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ════════════════════════════════════════════════════════════
    //  سجلات البصمة الخام
    // ════════════════════════════════════════════════════════════
        public async Task<PagedResult<BiometricLogDto>> GetBiometricLogsAsync(BiometricLogFilterDto filter)
    {
        var query = _db.BiometricLogs.AsNoTracking().AsQueryable();

        if (filter.DateFrom.HasValue)
            query = query.Where(b => b.LogDate >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
            query = query.Where(b => b.LogDate <= filter.DateTo.Value.Date);

        if (filter.BiometricCode.HasValue)
            query = query.Where(b => b.BiometricCode == filter.BiometricCode.Value);

        // ✅ البحث بالاسم
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var searchText = filter.SearchText.Trim();
            
            var matchingCodes = await _db.Employees.AsNoTracking()
                .Where(e => e.BioEmployeeId.HasValue &&
                           (e.FullName.Contains(searchText) ||
                            (e.Department != null && e.Department.Contains(searchText))))
                .Select(e => e.BioEmployeeId!.Value)
                .ToListAsync();

            if (matchingCodes.Any())
                query = query.Where(b => matchingCodes.Contains(b.BiometricCode));
            else
                return new PagedResult<BiometricLogDto> { Items = new(), TotalCount = 0 };
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(b => b.LogDate)
            .ThenByDescending(b => b.LogTime)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(b => new BiometricLogDto
            {
                BiometricLogId = b.BiometricLogId,
                BiometricCode = b.BiometricCode,
                LogDate = b.LogDate,
                LogTime = b.LogTime
            })
            .ToListAsync();

        // ربط بيانات الموظفين
        var biometricCodes = items.Select(i => i.BiometricCode).Distinct().ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.BioEmployeeId.HasValue && biometricCodes.Contains(e.BioEmployeeId.Value))
            .Select(e => new { e.BioEmployeeId, e.FullName, e.Department })
            .ToListAsync();

        foreach (var item in items)
        {
            var emp = employees.FirstOrDefault(e => e.BioEmployeeId == item.BiometricCode);
            item.EmployeeName = emp?.FullName;
            item.Department = emp?.Department;
        }

        return new PagedResult<BiometricLogDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    // ════════════════════════════════════════════════════════════
    //  معالجة سجلات البصمة (تحويل الخام إلى حضور)
    // ════════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message)>
    ProcessBiometricLogsAsync(DateTime date, string currentUserName)
{
    try
    {
        // ── 1. جيب سجلات البصمة الخام لليوم ──────────────────
        var logs = await _db.BiometricLogs.AsNoTracking()
            .Where(b => b.LogDate.Date == date.Date)
            .OrderBy(b => b.BiometricCode)
            .ThenBy(b => b.LogTime)
            .ToListAsync();

        if (!logs.Any())
            return (false, "لا توجد سجلات بصمة لهذا اليوم.");

        // ── 2. جيب الشيفتات الفعالة دفعة واحدة ───────────────
        // بدل ما نجيب شيفت كل موظف لوحده
        var shifts = await _db.EmployeeShifts.AsNoTracking()
            .Where(s => s.BiometricCode.HasValue
                     && s.EffectiveFrom <= date
                     && (s.EffectiveTo == null || s.EffectiveTo >= date))
            .Select(s => new
            {
                s.BiometricCode,
                s.StartTime,   // ✅ لحساب التأخير
                s.EndTime      // ✅ لحساب الانصراف المبكر
            })
            .ToListAsync();

        // ── 3. جيب الإعفاءات اليومية دفعة واحدة ──────────────
        var exemptedCodes = await _db.DailyExemptions.AsNoTracking()
            .Where(x => x.ExemptionDate.Date == date.Date)
            .Select(x => x.BioEmployeeId)
            .ToListAsync();

        // ── 4. جيب السجلات الموجودة دفعة واحدة للـ Upsert ────
        var bioCodes = logs.Select(l => l.BiometricCode).Distinct().ToList();
        var existingRecords = await _db.Attendances
            .Where(a => a.LogDate.Date == date.Date
                     && bioCodes.Contains(a.BiometricCode))
            .ToListAsync();

        // ── 5. المعالجة ───────────────────────────────────────
        var toAdd    = new List<Attendance>();
        int processed = 0;

        foreach (var group in logs.GroupBy(l => l.BiometricCode))
        {
            var bioCode = group.Key;

            // تخطى المعفيين
            if (exemptedCodes.Contains(bioCode)) continue;

            var dayLogs = group.OrderBy(l => l.LogTime).ToList();
            var timeIn  = dayLogs.First().LogTime;
            var timeOut = dayLogs.Count > 1
                ? dayLogs.Last().LogTime
                : (TimeOnly?)null;

            // ── حساب الساعات ─────────────────────────────────
            decimal? totalHours = null;
            if (timeOut.HasValue)
            {
                var dur = timeOut.Value.ToTimeSpan() - timeIn.ToTimeSpan();
                if (dur < TimeSpan.Zero) dur += TimeSpan.FromHours(24);
                totalHours = (decimal)dur.TotalHours;
            }

            // ✅ التأخير من وقت الشيفت الفعلي مش ثابت 8 صباحاً
            var shift      = shifts.FirstOrDefault(s => s.BiometricCode == bioCode);
            var expectedIn = shift?.StartTime ?? new TimeOnly(8, 0);
            int lateMinutes = timeIn > expectedIn
                ? (int)(timeIn - expectedIn).TotalMinutes
                : 0;

            // ✅ الانصراف المبكر من وقت الشيفت الفعلي
            var expectedOut = shift?.EndTime ?? new TimeOnly(17, 0);
            int earlyLeaveMinutes = timeOut.HasValue && timeOut.Value < expectedOut
                ? (int)(expectedOut - timeOut.Value).TotalMinutes
                : 0;

            var status = lateMinutes > 0
                ? AttendanceStatus.Late
                : AttendanceStatus.Present;

            // ── Upsert بدون SaveChanges داخل الـ loop ─────────
            var existing = existingRecords
                .FirstOrDefault(a => a.BiometricCode == bioCode);

            if (existing != null)
            {
                existing.TimeIn           = timeIn;
                existing.TimeOut          = timeOut;
                existing.TotalHours       = totalHours;
                existing.LateMinutes      = lateMinutes;
                existing.EarlyLeaveMinutes = earlyLeaveMinutes;
                existing.Status           = status;
            }
            else
            {
                toAdd.Add(new Attendance
                {
                    BiometricCode        = bioCode,
                    LogDate              = date.Date,
                    TimeIn               = timeIn,
                    TimeOut              = timeOut,
                    TotalHours           = totalHours,
                    LateMinutes          = lateMinutes,
                    EarlyLeaveMinutes    = earlyLeaveMinutes,
                    Status               = status
                });
            }

            processed++;
        }

        // ✅ Save مرة واحدة بعد الـ loop كله (أسرع بكتير)
        if (toAdd.Any()) _db.Attendances.AddRange(toAdd);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("Attendance", "Process",
            date.ToString("yyyy-MM-dd"), null,
            new { ProcessedCount = processed, Date = date },
            currentUserName);

        return (true, $"✅ تمت معالجة {processed} سجل بنجاح.");
    }
    catch (Exception ex)
    {
        return (false, $"حدث خطأ: {ex.Message}");
    }
}

    // ════════════════════════════════════════════════════════════
    //  بصمتي الشخصية
    // ════════════════════════════════════════════════════════════
    public async Task<List<AttendanceListDto>> GetMyAttendanceAsync(string userName, DateTime? month = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return new List<AttendanceListDto>();

        // جلب الموظف من اليوزر
        var appUser = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == userName);

        if (appUser?.EmployeeId == null)
            return new List<AttendanceListDto>();

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == appUser.EmployeeId.Value);

        if (employee?.BioEmployeeId == null)
            return new List<AttendanceListDto>();

        var targetMonth = month ?? DateTime.Today;
        var startDate = new DateTime(targetMonth.Year, targetMonth.Month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var items = await _db.Attendances.AsNoTracking()
            .Where(a => a.BiometricCode == employee.BioEmployeeId.Value &&
                        a.LogDate >= startDate &&
                        a.LogDate <= endDate)
            .OrderByDescending(a => a.LogDate)
            .Select(a => new AttendanceListDto
            {
                AttendanceId = a.AttendanceId,
                BiometricCode = a.BiometricCode,
                EmployeeId = employee.EmployeeId,
                EmployeeName = employee.FullName,
                Department = employee.Department,
                LogDate = a.LogDate,
                TimeIn = a.TimeIn,
                TimeOut = a.TimeOut,
                Status = a.Status,
                TotalHours = a.TotalHours,
                LateMinutes = a.LateMinutes,
                EarlyLeaveMinutes = a.EarlyLeaveMinutes,
                PenaltyHours = a.PenaltyHours
            })
            .ToListAsync();

        return items;
    }

    // ════════════════════════════════════════════════════════════
    //  إحصائياتي الشخصية
    // ════════════════════════════════════════════════════════════
    public async Task<AttendanceStatisticsDto> GetMyStatisticsAsync(string userName, DateTime? month = null)
    {
        var myAttendance = await GetMyAttendanceAsync(userName, month);

        if (!myAttendance.Any())
            return new AttendanceStatisticsDto();

        return new AttendanceStatisticsDto
        {
            TotalRecords = myAttendance.Count,
            PresentCount = myAttendance.Count(a => a.TimeIn != null),
            AbsentCount = myAttendance.Count(a => a.IsAbsent),
            LateCount = myAttendance.Count(a => a.IsLate),
            EarlyLeaveCount = myAttendance.Count(a => a.HasEarlyLeave),
            TotalHours = myAttendance.Sum(a => a.TotalHours ?? 0),
            AverageHours = myAttendance.Where(a => a.TotalHours > 0).Any()
                ? Math.Round(myAttendance.Where(a => a.TotalHours > 0).Average(a => a.TotalHours ?? 0), 1)
                : 0,
            TotalLateMinutes = myAttendance.Sum(a => a.LateMinutes ?? 0),
            TotalPenaltyHours = myAttendance.Sum(a => a.PenaltyHours ?? 0)
        };
    }

    // ════════════════════════════════════════════════════════════
    //  Helper Methods
    // ════════════════════════════════════════════════════════════
    private async Task FillEmployeeData(List<AttendanceListDto> items)
    {
        var biometricCodes = items.Select(i => i.BiometricCode).Distinct().ToList();
        
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.BioEmployeeId.HasValue && biometricCodes.Contains(e.BioEmployeeId.Value))
            .Select(e => new { e.EmployeeId, e.BioEmployeeId, e.FullName, e.Department })
            .ToListAsync();

        foreach (var item in items)
        {
            var emp = employees.FirstOrDefault(e => e.BioEmployeeId == item.BiometricCode);
            item.EmployeeId = emp?.EmployeeId;
            item.EmployeeName = emp?.FullName ?? $"كود: {item.BiometricCode}";
            item.Department = emp?.Department;
        }
    }

    private int GetWorkingDays(DateTime from, DateTime to, bool includeWeekends)
    {
        int count = 0;
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            if (includeWeekends || (date.DayOfWeek != DayOfWeek.Friday && date.DayOfWeek != DayOfWeek.Saturday))
                count++;
        }
        return count;
    }

}