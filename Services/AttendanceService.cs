using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class AttendanceService : IAttendanceService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;

    public AttendanceService(db24804Context db, IAuditService audit, NotificationService notify)
    {
        _db = db;
        _audit = audit;
        _notify = notify;
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
            query = query.Where(a => a.Status == AttendanceStatus.Absent ||
                                     ((a.Status == null || a.Status == "") && a.TimeIn == null && a.TimeOut == null));

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

        var workingRecords = records.Where(IsWorkingAttendanceRecord).ToList();
        var presentCount = workingRecords.Count(IsPresentAttendanceRecord);
        var absentCount = workingRecords.Count(IsAbsentAttendanceRecord);
        var lateCount = workingRecords.Count(a => a.Status == AttendanceStatus.Late || (a.LateMinutes ?? 0) > 0);
        var earlyLeaveCount = workingRecords.Count(a => a.Status == AttendanceStatus.EarlyLeave || (a.EarlyLeaveMinutes ?? 0) > 0);

        var totalHours = workingRecords.Sum(a => a.TotalHours ?? 0);
        var totalLateMinutes = workingRecords.Sum(a => a.LateMinutes ?? 0);
        var totalPenaltyHours = workingRecords.Sum(a => a.PenaltyHours ?? 0);
        var attendanceBase = workingRecords.Count;

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
            AttendanceRate = attendanceBase > 0 ? Math.Round((decimal)presentCount / attendanceBase * 100, 1) : 0,
            AbsenceRate = attendanceBase > 0 ? Math.Round((decimal)absentCount / attendanceBase * 100, 1) : 0,
            LateRate = presentCount > 0 ? Math.Round((decimal)lateCount / presentCount * 100, 1) : 0
        };
    }
    public async Task<AttendanceStatisticsDto> GetTodayStatisticsAsync()
    {
        var today = DateTime.Today;
        
        var todayRecords = await _db.Attendances.AsNoTracking()
            .Where(a => a.LogDate == today)
            .ToListAsync();

        var workingToday = todayRecords.Where(IsWorkingAttendanceRecord).ToList();
        var trackedEmployees = await _db.Employees.AsNoTracking()
            .CountAsync(e => (e.Status == "نشط" || e.Status == "Active") && e.BioEmployeeId.HasValue);

        var presentCount = workingToday.Count(IsPresentAttendanceRecord);
        var lateCount = workingToday.Count(a => a.Status == AttendanceStatus.Late || (a.LateMinutes ?? 0) > 0);
        var absentCount = workingToday.Count(IsAbsentAttendanceRecord);

        return new AttendanceStatisticsDto
        {
            TotalRecords = todayRecords.Count,
            TotalEmployees = trackedEmployees,
            TotalDays = 1,
            PresentCount = presentCount,
            AbsentCount = absentCount,
            LateCount = lateCount,
            TodayPresent = presentCount,
            TodayAbsent = absentCount,
            TodayLate = lateCount,
            TotalHours = workingToday.Sum(a => a.TotalHours ?? 0),
            AverageHours = presentCount > 0 ? Math.Round(workingToday.Sum(a => a.TotalHours ?? 0) / presentCount, 1) : 0,
            AttendanceRate = workingToday.Count > 0 ? Math.Round((decimal)presentCount / workingToday.Count * 100, 1) : 0,
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
                PresentCount = g.Count(a => (a.TimeIn != null) || a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late || a.Status == AttendanceStatus.Errand || a.Status == AttendanceStatus.EarlyLeave),
                AbsentCount = g.Count(a => a.Status == AttendanceStatus.Absent),
                LateCount = g.Count(a => a.Status == AttendanceStatus.Late || (a.LateMinutes ?? 0) > 0)
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
                var workingRecords = deptAttendance.Where(IsWorkingAttendanceRecord).ToList();
                var presentCount = workingRecords.Count(IsPresentAttendanceRecord);
                var absentCount = workingRecords.Count(IsAbsentAttendanceRecord);
                var totalRecords = workingRecords.Count;

                return new DepartmentAttendanceSummary
                {
                    Department = g.Key,
                    EmployeeCount = g.Count(),
                    PresentCount = presentCount,
                    AbsentCount = absentCount,
                    AttendanceRate = totalRecords > 0 ? Math.Round((decimal)presentCount / totalRecords * 100, 1) : 0,
                    AverageHours = presentCount > 0 ? Math.Round(workingRecords.Sum(a => a.TotalHours ?? 0) / presentCount, 1) : 0
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
            var presentDays = empAttendance.Count(IsPresentAttendanceRecord);
            var explicitAbsence = empAttendance.Count(IsAbsentAttendanceRecord);

            return new EmployeeAttendanceRank
            {
                EmployeeId = emp.EmployeeId,
                EmployeeName = emp.FullName,
                Department = emp.Department,
                PresentDays = presentDays,
                AbsentDays = Math.Max(explicitAbsence, workingDays - presentDays),
                LateDays = empAttendance.Count(a => a.Status == AttendanceStatus.Late || (a.LateMinutes ?? 0) > 0),
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
                attendance.LateMinutes,
                attendance.EarlyLeaveMinutes
            };

            var (expectedIn, expectedOut) = await GetExpectedShiftWindowAsync(dto.BiometricCode, dto.LogDate);
            ApplyAttendanceEditValues(attendance, dto, expectedIn, expectedOut);

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

    public async Task<(bool Success, string Message, int? AttendanceId)> UpsertAttendanceAsync(AttendanceEditDto dto, string currentUserName)
    {
        if (dto.BiometricCode <= 0)
            return (false, "كود البصمة مطلوب.", null);

        var employeeExists = await _db.Employees.AsNoTracking()
            .AnyAsync(e => e.BioEmployeeId == dto.BiometricCode);
        if (!employeeExists)
            return (false, "الموظف المرتبط بكود البصمة غير موجود.", null);

        try
        {
            var attendance = dto.AttendanceId > 0
                ? await _db.Attendances.FirstOrDefaultAsync(a => a.AttendanceId == dto.AttendanceId)
                : await _db.Attendances.FirstOrDefaultAsync(a => a.BiometricCode == dto.BiometricCode && a.LogDate.Date == dto.LogDate.Date);

            var isNew = attendance == null;
            object? oldData = null;

            if (attendance == null)
            {
                attendance = new Attendance
                {
                    BiometricCode = dto.BiometricCode,
                    LogDate = dto.LogDate.Date
                };
                _db.Attendances.Add(attendance);
            }
            else
            {
                oldData = new
                {
                    attendance.TimeIn,
                    attendance.TimeOut,
                    attendance.Status,
                    attendance.TotalHours,
                    attendance.LateMinutes,
                    attendance.EarlyLeaveMinutes
                };
            }

            attendance.BiometricCode = dto.BiometricCode;
            attendance.LogDate = dto.LogDate.Date;

            var (expectedIn, expectedOut) = await GetExpectedShiftWindowAsync(dto.BiometricCode, dto.LogDate);
            ApplyAttendanceEditValues(attendance, dto, expectedIn, expectedOut);

            await _db.SaveChangesAsync();

            await _audit.LogAsync("Attendance", isNew ? "Insert" : "Update",
                attendance.AttendanceId.ToString(),
                oldData,
                new
                {
                    attendance.BiometricCode,
                    attendance.LogDate,
                    attendance.TimeIn,
                    attendance.TimeOut,
                    attendance.Status,
                    attendance.TotalHours,
                    attendance.LateMinutes,
                    attendance.EarlyLeaveMinutes,
                    dto.EditReason,
                    Source = "ManualDailyEntry"
                },
                currentUserName);

            return (true,
                isNew ? "تم إنشاء سجل الحضور اليدوي بنجاح." : "تم تحديث سجل الحضور اليدوي بنجاح.",
                attendance.AttendanceId);
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}", null);
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

        var report = new List<AttendanceReportDto>();

        foreach (var emp in employees)
        {
            var workingDays = filter.IncludeWeekends
                ? (filter.DateTo.Date - filter.DateFrom.Date).Days + 1
                : await GetWorkingDaysForEmployeeAsync(emp.EmployeeId, filter.DateFrom, filter.DateTo);

            var empAttendance = attendanceRecords
                .Where(a => a.BiometricCode == emp.BioEmployeeId!.Value)
                .ToList();

            var presentDays = empAttendance.Count(IsPresentAttendanceRecord);
            var explicitAbsence = empAttendance.Count(IsAbsentAttendanceRecord);
            var dto = new AttendanceReportDto
            {
                EmployeeId = emp.EmployeeId,
                BiometricCode = emp.BioEmployeeId!.Value,
                EmployeeName = emp.FullName,
                Department = emp.Department,
                WorkingDays = workingDays,
                PresentDays = presentDays,
                AbsentDays = Math.Max(explicitAbsence, workingDays - presentDays),
                LateDays = empAttendance.Count(a => a.Status == AttendanceStatus.Late || (a.LateMinutes ?? 0) > 0),
                EarlyLeaveDays = empAttendance.Count(a => a.Status == AttendanceStatus.EarlyLeave || (a.EarlyLeaveMinutes ?? 0) > 0),
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

        // ── 2. جيب الشيفتات الفعالة دفعة واحدة ───────────────
        var shifts = await _db.EmployeeShifts.AsNoTracking()
            .Where(s => s.BiometricCode.HasValue
                     && s.EffectiveFrom <= date
                     && (s.EffectiveTo == null || s.EffectiveTo >= date))
            .Select(s => new
            {
                s.EmployeeId,
                s.BiometricCode,
                s.StartTime,
                s.EndTime,
                s.OffDay1,
                s.OffDay2
            })
            .ToListAsync();

        // ── 3. جميع الموظفين النشطين الذين لديهم كود بصمة ────
        var trackedEmployees = await _db.Employees.AsNoTracking()
            .Where(e => (e.Status == "نشط" || e.Status == "Active") && e.BioEmployeeId.HasValue)
            .Select(e => new
            {
                e.EmployeeId,
                BioEmployeeId = e.BioEmployeeId!.Value,
                e.FullName
            })
            .ToListAsync();

        if (!trackedEmployees.Any())
            return (false, "لا يوجد موظفون مرتبطون بالبصمة لمعالجة هذا اليوم.");

        // ── 4. الاستثناءات المعتمدة لليوم (مع دعم البيانات القديمة) ────
        var approvedExemptions = await _db.DailyExemptions
            .Where(x => x.ExemptionDate.Date == date.Date)
            .ToListAsync();

        approvedExemptions = approvedExemptions
            .Where(x => ExemptionStatus.InferStatus(x.Status, x.ApprovedBy) == ExemptionStatus.Approved)
            .ToList();

        // ── 5. جيب السجلات الموجودة دفعة واحدة للـ Upsert ────
        var trackedBioCodes = trackedEmployees.Select(e => e.BioEmployeeId).Distinct().ToList();
        var existingRecords = await _db.Attendances
            .Where(a => a.LogDate.Date == date.Date && trackedBioCodes.Contains(a.BiometricCode))
            .ToListAsync();

        // ── 6. معالجة من لديه بصمة فعلاً ─────────────────────
        var toAdd = new List<Attendance>();
        int processed = 0;

        foreach (var group in logs.GroupBy(l => l.BiometricCode))
        {
            var bioCode = group.Key;

            var dayLogs = group.OrderBy(l => l.LogTime).ToList();
            var timeIn = dayLogs.First().LogTime;
            var timeOut = dayLogs.Count > 1
                ? dayLogs.Last().LogTime
                : (TimeOnly?)null;

            decimal? totalHours = null;
            if (timeOut.HasValue)
            {
                var dur = timeOut.Value.ToTimeSpan() - timeIn.ToTimeSpan();
                if (dur < TimeSpan.Zero) dur += TimeSpan.FromHours(24);
                totalHours = (decimal)dur.TotalHours;
            }

            var shift = shifts.FirstOrDefault(s => s.BiometricCode == bioCode);
            var expectedIn = shift?.StartTime ?? new TimeOnly(8, 0);
            var expectedOut = shift?.EndTime ?? new TimeOnly(17, 0);

            int lateMinutes = timeIn > expectedIn
                ? (int)(timeIn - expectedIn).TotalMinutes
                : 0;

            int earlyLeaveMinutes = timeOut.HasValue && timeOut.Value < expectedOut
                ? (int)(expectedOut - timeOut.Value).TotalMinutes
                : 0;

            var status = lateMinutes > 0 ? AttendanceStatus.Late : AttendanceStatus.Present;

            var existing = existingRecords.FirstOrDefault(a => a.BiometricCode == bioCode);
            if (existing != null)
            {
                existing.TimeIn = timeIn;
                existing.TimeOut = timeOut;
                existing.TotalHours = totalHours;
                existing.LateMinutes = lateMinutes;
                existing.EarlyLeaveMinutes = earlyLeaveMinutes;
                existing.Status = status;
            }
            else
            {
                toAdd.Add(new Attendance
                {
                    BiometricCode = bioCode,
                    LogDate = date.Date,
                    TimeIn = timeIn,
                    TimeOut = timeOut,
                    TotalHours = totalHours,
                    LateMinutes = lateMinutes,
                    EarlyLeaveMinutes = earlyLeaveMinutes,
                    Status = status
                });
            }

            processed++;
        }

        if (toAdd.Any())
            _db.Attendances.AddRange(toAdd);

        await _db.SaveChangesAsync();

        // ── 7. طبّق الاستثناءات المعتمدة على السجلات الموجودة/المفقودة ────
        foreach (var exemption in approvedExemptions)
            await CreateOrUpdateExemptionAttendanceAsync(exemption);

        await _db.SaveChangesAsync();

        // ── 8. أكمل التغطية اليومية: راحة / إجازة / غياب ─────────
        var isHoliday = await _db.Calendars.AsNoTracking()
            .AnyAsync(c => c.CalendarDate.Date == date.Date && c.IsHoliday == true);

        var finalRecords = await _db.Attendances
            .Where(a => a.LogDate.Date == date.Date && trackedBioCodes.Contains(a.BiometricCode))
            .ToListAsync();

        var attendanceByCode = finalRecords.ToDictionary(a => a.BiometricCode, a => a);
        int generatedCoverage = 0;

        foreach (var employee in trackedEmployees)
        {
            if (attendanceByCode.ContainsKey(employee.BioEmployeeId))
                continue;

            var shift = shifts.FirstOrDefault(s => s.BiometricCode == employee.BioEmployeeId);
            var isOffDay = IsEmployeeOffDay(date, shift?.OffDay1, shift?.OffDay2);

            var status = isHoliday
                ? AttendanceStatus.Holiday
                : isOffDay
                    ? AttendanceStatus.OffDay
                    : AttendanceStatus.Absent;

            _db.Attendances.Add(new Attendance
            {
                BiometricCode = employee.BioEmployeeId,
                LogDate = date.Date,
                TimeIn = null,
                TimeOut = null,
                TotalHours = null,
                LateMinutes = 0,
                EarlyLeaveMinutes = 0,
                Status = status
            });

            generatedCoverage++;
        }

        if (generatedCoverage > 0)
            await _db.SaveChangesAsync();

        await _audit.LogAsync("Attendance", "Process",
            date.ToString("yyyy-MM-dd"), null,
            new
            {
                ProcessedCount = processed,
                GeneratedCoverage = generatedCoverage,
                RawLogs = logs.Count,
                Date = date
            },
            currentUserName);

        return (true,
            $"✅ تمت معالجة {processed} موظف من البصمة" +
            (generatedCoverage > 0 ? $" + إنشاء {generatedCoverage} سجل (غياب/راحة/إجازة)" : string.Empty));
    }
    catch (Exception ex)
    {
        return (false, $"حدث خطأ: {ex.Message}");
    }
}

    public async Task<BiometricImportResultDto> ImportBiometricLogsAsync(Stream fileStream, string fileName, BiometricImportOptionsDto options, string currentUserName)
    {
        var result = new BiometricImportResultDto();

        try
        {
            var rows = await ParseBiometricImportRowsAsync(fileStream, fileName, options);
            result.TotalRows = rows.Count;

            if (!rows.Any())
            {
                result.Warnings.Add("لم يتم العثور على أي صفوف صالحة داخل الملف.");
                return result;
            }

            var uniqueRows = rows
                .GroupBy(x => new { x.BiometricCode, Date = x.LogDate.Date, x.LogTime })
                .Select(g => g.First())
                .ToList();

            result.SkippedCount += rows.Count - uniqueRows.Count;
            if (rows.Count != uniqueRows.Count)
                result.Warnings.Add("تم تجاهل بعض الصفوف المكررة داخل الملف نفسه.");

            var allCodes = uniqueRows.Select(x => x.BiometricCode).Distinct().ToList();
            var knownCodes = await _db.Employees.AsNoTracking()
                .Where(e => e.BioEmployeeId.HasValue && allCodes.Contains(e.BioEmployeeId.Value))
                .Select(e => e.BioEmployeeId!.Value)
                .Distinct()
                .ToListAsync();
            var knownCodeSet = knownCodes.ToHashSet();

            var validRows = uniqueRows.Where(x => knownCodeSet.Contains(x.BiometricCode)).ToList();
            var unknownRows = uniqueRows.Where(x => !knownCodeSet.Contains(x.BiometricCode)).ToList();
            foreach (var invalid in unknownRows.Take(20))
                result.Errors.Add($"كود بصمة غير معروف: {invalid.BiometricCode} بتاريخ {invalid.LogDate:yyyy/MM/dd} {invalid.LogTime:HH:mm:ss}");
            result.ErrorCount += unknownRows.Count;

            if (!validRows.Any())
                return result;

            var minDate = validRows.Min(x => x.LogDate.Date);
            var maxDate = validRows.Max(x => x.LogDate.Date);
            var existingRows = await _db.BiometricLogs.AsNoTracking()
                .Where(b => b.LogDate >= minDate && b.LogDate <= maxDate && allCodes.Contains(b.BiometricCode))
                .Select(b => new { b.BiometricCode, Date = b.LogDate.Date, b.LogTime })
                .ToListAsync();
            var existingSet = existingRows
                .Select(x => $"{x.BiometricCode}|{x.Date:yyyyMMdd}|{x.LogTime:HHmmss}")
                .ToHashSet();

            var toInsert = new List<BiometricLog>();
            foreach (var row in validRows)
            {
                var key = $"{row.BiometricCode}|{row.LogDate:yyyyMMdd}|{row.LogTime:HHmmss}";
                if (existingSet.Contains(key))
                {
                    result.SkippedCount++;
                    continue;
                }

                toInsert.Add(new BiometricLog
                {
                    BiometricCode = row.BiometricCode,
                    LogDate = row.LogDate.Date,
                    LogTime = row.LogTime
                });
                existingSet.Add(key);
            }

            if (toInsert.Any())
            {
                _db.BiometricLogs.AddRange(toInsert);
                await _db.SaveChangesAsync();
            }

            result.ImportedCount = toInsert.Count;
            result.ImportedDates = toInsert.Select(x => x.LogDate.Date).Distinct().OrderBy(x => x).ToList();

            if (options.ProcessAfterImport && toInsert.Any())
            {
                if (options.GenerateDailyCoverage)
                {
                    foreach (var date in result.ImportedDates)
                    {
                        var processResult = await ProcessBiometricLogsAsync(date, currentUserName);
                        if (!processResult.Success)
                            result.Warnings.Add($"تعذر معالجة تاريخ {date:yyyy/MM/dd}: {processResult.Message}");
                    }
                }
                else
                {
                    foreach (var dateGroup in toInsert.GroupBy(x => x.LogDate.Date))
                    {
                        await ProcessBiometricLogsForBiometricCodesAsync(
                            dateGroup.Key,
                            dateGroup.Select(x => x.BiometricCode).Distinct().ToList(),
                            currentUserName);
                    }
                }
            }

            await _audit.LogAsync("BiometricLog", "Import", fileName, null,
                new
                {
                    fileName,
                    result.TotalRows,
                    result.ImportedCount,
                    result.SkippedCount,
                    result.ErrorCount,
                    options.ProcessAfterImport,
                    options.GenerateDailyCoverage,
                    options.SpecificBiometricCode
                },
                currentUserName);
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            result.ErrorCount++;
        }

        return result;
    }

    public async Task<byte[]> GetBiometricImportTemplateAsync()
    {
        await Task.CompletedTask;

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("BiometricImport");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = "BiometricCode";
        ws.Cell(1, 2).Value = "LogDate";
        ws.Cell(1, 3).Value = "LogTime";

        ws.Cell(2, 1).Value = 101;
        ws.Cell(2, 2).Value = DateTime.Today.ToString("yyyy/MM/dd");
        ws.Cell(2, 3).Value = "08:55:00";
        ws.Cell(3, 1).Value = 101;
        ws.Cell(3, 2).Value = DateTime.Today.ToString("yyyy/MM/dd");
        ws.Cell(3, 3).Value = "17:04:00";

        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromArgb(0x1A, 0x23, 0x7E);
        ws.Row(1).Style.Font.FontColor = XLColor.White;
        ws.Columns().AdjustToContents();

        var help = workbook.Worksheets.Add("Instructions");
        help.RightToLeft = true;
        help.Cell(1, 1).Value = "تعليمات استيراد البصمة";
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(3, 1).Value = "1) الأعمدة الافتراضية: BiometricCode, LogDate, LogTime";
        help.Cell(4, 1).Value = "2) يمكن أيضًا استخدام عمود واحد باسم DateTime أو Timestamp";
        help.Cell(5, 1).Value = "3) إذا كان الملف لموظف واحد فقط يمكنك تحديد كود البصمة من الشاشة";
        help.Cell(6, 1).Value = "4) لو فعلت خيار إنشاء تغطية يومية سيقوم النظام بإنشاء غياب/راحة/إجازة لباقي الموظفين في نفس اليوم";
        help.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
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

        var workingRecords = myAttendance.Where(a => a.IsWorkingDayRecord).ToList();

        return new AttendanceStatisticsDto
        {
            TotalRecords = myAttendance.Count,
            PresentCount = workingRecords.Count(a => a.IsPresent),
            AbsentCount = workingRecords.Count(a => a.IsAbsent),
            LateCount = workingRecords.Count(a => a.IsLate),
            EarlyLeaveCount = workingRecords.Count(a => a.HasEarlyLeave),
            TotalHours = workingRecords.Sum(a => a.TotalHours ?? 0),
            AverageHours = workingRecords.Where(a => a.TotalHours > 0).Any()
                ? Math.Round(workingRecords.Where(a => a.TotalHours > 0).Average(a => a.TotalHours ?? 0), 1)
                : 0,
            TotalLateMinutes = workingRecords.Sum(a => a.LateMinutes ?? 0),
            TotalPenaltyHours = workingRecords.Sum(a => a.PenaltyHours ?? 0)
        };
    }

    // ════════════════════════════════════════════════════════════
    //  Helper Methods
    // ════════════════════════════════════════════════════════════
    private sealed class ImportedBiometricRow
    {
        public int BiometricCode { get; set; }
        public DateTime LogDate { get; set; }
        public TimeOnly LogTime { get; set; }
    }

    private async Task<List<ImportedBiometricRow>> ParseBiometricImportRowsAsync(Stream fileStream, string fileName, BiometricImportOptionsDto options)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension switch
        {
            ".xlsx" or ".xls" => await ParseBiometricRowsFromExcelAsync(fileStream, options),
            ".csv" or ".txt" => await ParseBiometricRowsFromDelimitedAsync(fileStream, options),
            _ => throw new Exception("نوع الملف غير مدعوم. استخدم xlsx أو csv أو txt")
        };
    }

    private async Task<List<ImportedBiometricRow>> ParseBiometricRowsFromDelimitedAsync(Stream fileStream, BiometricImportOptionsDto options)
    {
        using var reader = new StreamReader(fileStream, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        fileStream.Position = 0;

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var rows = new List<ImportedBiometricRow>();
        if (!lines.Any()) return rows;

        var separators = new[] { ',', ';', '\t', '|' };
        var firstLine = lines[0];
        var separator = separators.OrderByDescending(s => firstLine.Count(c => c == s)).FirstOrDefault();
        if (separator == default) separator = ',';

        var headerCells = firstLine.Split(separator).Select(x => x.Trim()).ToList();
        var hasHeader = headerCells.Any(c => c.Equals("BiometricCode", StringComparison.OrdinalIgnoreCase)
                                          || c.Equals("Code", StringComparison.OrdinalIgnoreCase)
                                          || c.Equals("DateTime", StringComparison.OrdinalIgnoreCase)
                                          || c.Equals("LogDate", StringComparison.OrdinalIgnoreCase));

        int startIndex = hasHeader ? 1 : 0;
        var codeIndex = hasHeader ? FindColumnIndex(headerCells, "BiometricCode", "Code", "EmployeeCode", "UserId") : 0;
        var dateIndex = hasHeader ? FindColumnIndex(headerCells, "LogDate", "Date") : 1;
        var timeIndex = hasHeader ? FindColumnIndex(headerCells, "LogTime", "Time") : 2;
        var dateTimeIndex = hasHeader ? FindColumnIndex(headerCells, "DateTime", "Timestamp", "LogDateTime") : -1;

        for (int i = startIndex; i < lines.Length; i++)
        {
            var cells = lines[i].Split(separator).Select(x => x.Trim()).ToArray();
            var row = TryParseImportedRow(cells, codeIndex, dateIndex, timeIndex, dateTimeIndex, options);
            if (row != null) rows.Add(row);
        }

        return rows;
    }

    private async Task<List<ImportedBiometricRow>> ParseBiometricRowsFromExcelAsync(Stream fileStream, BiometricImportOptionsDto options)
    {
        await Task.CompletedTask;
        using var workbook = new XLWorkbook(fileStream);
        var ws = workbook.Worksheet(1);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
        var rows = new List<ImportedBiometricRow>();
        if (lastRow < 1) return rows;

        var firstRowCells = Enumerable.Range(1, lastCol).Select(c => ws.Cell(1, c).GetString().Trim()).ToList();
        var hasHeader = firstRowCells.Any(c => c.Equals("BiometricCode", StringComparison.OrdinalIgnoreCase)
                                            || c.Equals("Code", StringComparison.OrdinalIgnoreCase)
                                            || c.Equals("DateTime", StringComparison.OrdinalIgnoreCase)
                                            || c.Equals("LogDate", StringComparison.OrdinalIgnoreCase));

        int startRow = hasHeader ? 2 : 1;
        var codeIndex = hasHeader ? FindColumnIndex(firstRowCells, "BiometricCode", "Code", "EmployeeCode", "UserId") : 0;
        var dateIndex = hasHeader ? FindColumnIndex(firstRowCells, "LogDate", "Date") : 1;
        var timeIndex = hasHeader ? FindColumnIndex(firstRowCells, "LogTime", "Time") : 2;
        var dateTimeIndex = hasHeader ? FindColumnIndex(firstRowCells, "DateTime", "Timestamp", "LogDateTime") : -1;

        for (int rowNumber = startRow; rowNumber <= lastRow; rowNumber++)
        {
            var cells = Enumerable.Range(1, lastCol).Select(c => ws.Cell(rowNumber, c).GetFormattedString().Trim()).ToArray();
            var row = TryParseImportedRow(cells, codeIndex, dateIndex, timeIndex, dateTimeIndex, options);
            if (row != null) rows.Add(row);
        }

        fileStream.Position = 0;
        return rows;
    }

    private static int FindColumnIndex(IReadOnlyList<string> headers, params string[] aliases)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            if (aliases.Any(a => headers[i].Equals(a, StringComparison.OrdinalIgnoreCase)))
                return i;
        }
        return -1;
    }

    private static ImportedBiometricRow? TryParseImportedRow(string[] cells, int codeIndex, int dateIndex, int timeIndex, int dateTimeIndex, BiometricImportOptionsDto options)
    {
        if (cells.Length == 0) return null;

        int biometricCode;
        if (codeIndex >= 0 && codeIndex < cells.Length && int.TryParse(cells[codeIndex], out var parsedCode))
        {
            biometricCode = parsedCode;
        }
        else if (options.SpecificBiometricCode.HasValue)
        {
            biometricCode = options.SpecificBiometricCode.Value;
        }
        else
        {
            return null;
        }

        if (options.SpecificBiometricCode.HasValue && biometricCode != options.SpecificBiometricCode.Value)
            return null;

        DateTime logDate;
        TimeOnly logTime;

        if (dateTimeIndex >= 0 && dateTimeIndex < cells.Length && DateTime.TryParse(cells[dateTimeIndex], out var dateTimeValue))
        {
            logDate = dateTimeValue.Date;
            logTime = TimeOnly.FromDateTime(dateTimeValue);
        }
        else
        {
            var actualDateIndex = dateIndex;
            var actualTimeIndex = timeIndex;

            // ملف خاص بموظف واحد بدون عمود كود بصمة: Date | Time
            if (options.SpecificBiometricCode.HasValue && cells.Length == 2)
            {
                actualDateIndex = 0;
                actualTimeIndex = 1;
            }

            if (actualDateIndex < 0 || actualDateIndex >= cells.Length || !DateTime.TryParse(cells[actualDateIndex], out var dateValue))
                return null;

            if (actualTimeIndex < 0 || actualTimeIndex >= cells.Length)
                return null;

            if (!TimeOnly.TryParse(cells[actualTimeIndex], out var timeValue))
            {
                if (TimeSpan.TryParse(cells[actualTimeIndex], out var timeSpanValue))
                    timeValue = TimeOnly.FromTimeSpan(timeSpanValue);
                else
                    return null;
            }

            logDate = dateValue.Date;
            logTime = timeValue;
        }

        return new ImportedBiometricRow
        {
            BiometricCode = biometricCode,
            LogDate = logDate,
            LogTime = logTime
        };
    }

    private async Task ProcessBiometricLogsForBiometricCodesAsync(DateTime date, IReadOnlyCollection<int> biometricCodes, string currentUserName)
    {
        if (!biometricCodes.Any()) return;

        var logs = await _db.BiometricLogs.AsNoTracking()
            .Where(b => b.LogDate.Date == date.Date && biometricCodes.Contains(b.BiometricCode))
            .OrderBy(b => b.BiometricCode)
            .ThenBy(b => b.LogTime)
            .ToListAsync();

        var shifts = await _db.EmployeeShifts.AsNoTracking()
            .Where(s => s.BiometricCode.HasValue
                     && biometricCodes.Contains(s.BiometricCode.Value)
                     && s.EffectiveFrom <= date
                     && (s.EffectiveTo == null || s.EffectiveTo >= date))
            .Select(s => new { s.BiometricCode, s.StartTime, s.EndTime })
            .ToListAsync();

        var exemptions = await _db.DailyExemptions
            .Where(x => x.ExemptionDate.Date == date.Date && biometricCodes.Contains(x.BioEmployeeId))
            .ToListAsync();
        exemptions = exemptions.Where(x => ExemptionStatus.InferStatus(x.Status, x.ApprovedBy) == ExemptionStatus.Approved).ToList();

        var existingRecords = await _db.Attendances
            .Where(a => a.LogDate.Date == date.Date && biometricCodes.Contains(a.BiometricCode))
            .ToListAsync();

        foreach (var group in logs.GroupBy(l => l.BiometricCode))
        {
            var bioCode = group.Key;
            var dayLogs = group.OrderBy(l => l.LogTime).ToList();
            var timeIn = dayLogs.First().LogTime;
            var timeOut = dayLogs.Count > 1 ? dayLogs.Last().LogTime : (TimeOnly?)null;

            var shift = shifts.FirstOrDefault(s => s.BiometricCode == bioCode);
            var expectedIn = shift?.StartTime ?? new TimeOnly(8, 0);
            var expectedOut = shift?.EndTime ?? new TimeOnly(17, 0);
            int lateMinutes = timeIn > expectedIn ? (int)(timeIn - expectedIn).TotalMinutes : 0;
            int earlyLeaveMinutes = timeOut.HasValue && timeOut.Value < expectedOut ? (int)(expectedOut - timeOut.Value).TotalMinutes : 0;

            decimal? totalHours = null;
            if (timeOut.HasValue)
            {
                var duration = timeOut.Value.ToTimeSpan() - timeIn.ToTimeSpan();
                if (duration < TimeSpan.Zero) duration += TimeSpan.FromHours(24);
                totalHours = (decimal)duration.TotalHours;
            }

            var status = lateMinutes > 0 ? AttendanceStatus.Late : AttendanceStatus.Present;
            var existing = existingRecords.FirstOrDefault(a => a.BiometricCode == bioCode);
            if (existing == null)
            {
                existing = new Attendance { BiometricCode = bioCode, LogDate = date.Date };
                _db.Attendances.Add(existing);
                existingRecords.Add(existing);
            }

            existing.TimeIn = timeIn;
            existing.TimeOut = timeOut;
            existing.TotalHours = totalHours;
            existing.LateMinutes = lateMinutes;
            existing.EarlyLeaveMinutes = earlyLeaveMinutes;
            existing.Status = status;
        }

        await _db.SaveChangesAsync();

        foreach (var exemption in exemptions)
            await CreateOrUpdateExemptionAttendanceAsync(exemption);

        await _db.SaveChangesAsync();
    }

    private async Task<(TimeOnly StartTime, TimeOnly EndTime)> GetExpectedShiftWindowAsync(int biometricCode, DateTime date)
    {
        var shift = await _db.EmployeeShifts.AsNoTracking()
            .Where(s => s.BiometricCode == biometricCode
                     && s.EffectiveFrom <= date
                     && (s.EffectiveTo == null || s.EffectiveTo >= date))
            .Select(s => new { s.StartTime, s.EndTime })
            .FirstOrDefaultAsync();

        return (shift?.StartTime ?? new TimeOnly(8, 0), shift?.EndTime ?? new TimeOnly(17, 0));
    }

    private static void ApplyAttendanceEditValues(Attendance attendance, AttendanceEditDto dto, TimeOnly expectedIn, TimeOnly expectedOut)
    {
        var status = dto.Status ?? AttendanceStatus.Present;
        attendance.Status = status;
        attendance.TimeIn = dto.TimeIn;
        attendance.TimeOut = dto.TimeOut;

        if (status == AttendanceStatus.Absent || status == AttendanceStatus.Holiday || status == AttendanceStatus.OffDay || status == AttendanceStatus.Weekend)
        {
            attendance.TimeIn = null;
            attendance.TimeOut = null;
            attendance.TotalHours = null;
            attendance.LateMinutes = 0;
            attendance.EarlyLeaveMinutes = 0;
            attendance.PenaltyHours = 0;
            return;
        }

        attendance.TotalHours = null;
        attendance.LateMinutes = 0;
        attendance.EarlyLeaveMinutes = 0;
        attendance.PenaltyHours = 0;

        if (attendance.TimeIn.HasValue)
        {
            attendance.LateMinutes = attendance.TimeIn.Value > expectedIn
                ? (int)(attendance.TimeIn.Value - expectedIn).TotalMinutes
                : 0;
        }

        if (attendance.TimeIn.HasValue && attendance.TimeOut.HasValue)
        {
            var duration = attendance.TimeOut.Value.ToTimeSpan() - attendance.TimeIn.Value.ToTimeSpan();
            if (duration < TimeSpan.Zero) duration += TimeSpan.FromHours(24);
            attendance.TotalHours = (decimal)duration.TotalHours;

            attendance.EarlyLeaveMinutes = attendance.TimeOut.Value < expectedOut
                ? (int)(expectedOut - attendance.TimeOut.Value).TotalMinutes
                : 0;
        }

        if (status == AttendanceStatus.Present || status == AttendanceStatus.Late)
            attendance.Status = attendance.LateMinutes > 0 ? AttendanceStatus.Late : AttendanceStatus.Present;
    }

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

    private static bool IsPresentAttendanceRecord(Attendance attendance)
    {
        return attendance.TimeIn != null
            || attendance.Status == AttendanceStatus.Present
            || attendance.Status == AttendanceStatus.Late
            || attendance.Status == AttendanceStatus.Errand
            || attendance.Status == AttendanceStatus.EarlyLeave;
    }

    private static bool IsAbsentAttendanceRecord(Attendance attendance)
    {
        return attendance.Status == AttendanceStatus.Absent;
    }

    private static bool IsWorkingAttendanceRecord(Attendance attendance)
    {
        return attendance.Status != AttendanceStatus.OffDay
            && attendance.Status != AttendanceStatus.Weekend
            && attendance.Status != AttendanceStatus.Holiday;
    }

    private static bool IsEmployeeOffDay(DateTime date, byte? offDay1, byte? offDay2)
    {
        var offDays = new List<DayOfWeek>();
        if (offDay1.HasValue) offDays.Add((DayOfWeek)offDay1.Value);
        if (offDay2.HasValue) offDays.Add((DayOfWeek)offDay2.Value);

        if (!offDays.Any())
            offDays.AddRange(new[] { DayOfWeek.Friday, DayOfWeek.Saturday });

        return offDays.Contains(date.DayOfWeek);
    }

    private int GetWorkingDays(DateTime from, DateTime to, bool includeWeekends)
    {
        // Fallback عام للشاشات التي لا تعتمد على شيفت موظف بعينه
        var holidayDates = _db.Calendars.AsNoTracking()
            .Where(c => c.CalendarDate >= from.Date && c.CalendarDate <= to.Date && c.IsHoliday == true)
            .Select(c => c.CalendarDate.Date)
            .ToList();
        var holidaySet = holidayDates.ToHashSet();

        int count = 0;
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            var isWeekend = date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday;
            if ((includeWeekends || !isWeekend) && !holidaySet.Contains(date))
                count++;
        }
        return count;
    }

    // ════════════════════════════════════════════════════════════
    //  أيام العمل حسب راحات الشفت (OffDay1/OffDay2)
    // ════════════════════════════════════════════════════════════
    public async Task<int> GetWorkingDaysForEmployeeAsync(int employeeId, DateTime from, DateTime to)
    {
        // جيب شيفت الموظف الفعال
        var shift = await _db.EmployeeShifts.AsNoTracking()
            .Where(s => s.EmployeeId == employeeId
                     && s.EffectiveFrom <= to
                     && (s.EffectiveTo == null || s.EffectiveTo >= from))
            .OrderByDescending(s => s.EffectiveFrom)
            .Select(s => new { s.OffDay1, s.OffDay2 })
            .FirstOrDefaultAsync();

        var offDays = new List<DayOfWeek>();
        if (shift?.OffDay1 != null) offDays.Add((DayOfWeek)shift.OffDay1.Value);
        if (shift?.OffDay2 != null) offDays.Add((DayOfWeek)shift.OffDay2.Value);

        var holidayDates = await _db.Calendars.AsNoTracking()
            .Where(c => c.CalendarDate >= from.Date && c.CalendarDate <= to.Date && c.IsHoliday == true)
            .Select(c => c.CalendarDate.Date)
            .ToListAsync();

        var holidaySet = holidayDates.ToHashSet();
        int count = 0;
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            if (!offDays.Contains(date.DayOfWeek) && !holidaySet.Contains(date))
                count++;
        }
        return count;
    }

    // ════════════════════════════════════════════════════════════
    //  الاستثناءات - CRUD كامل مع Status workflow
    // ════════════════════════════════════════════════════════════
    public async Task<PagedResult<ExemptionListDto>> GetExemptionsAsync(ExemptionFilterDto filter)
    {
        var query = await BuildExemptionsQueryAsync(filter);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(e => e.ExemptionDate)
            .ThenByDescending(e => e.ExemptionId)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var employeeIds = items
            .Where(i => i.EmployeeId.HasValue)
            .Select(i => i.EmployeeId!.Value)
            .Distinct()
            .ToList();

        var allBioCodes = items.Select(i => i.BioEmployeeId).Distinct().ToList();

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.EmployeeId) ||
                        (e.BioEmployeeId.HasValue && allBioCodes.Contains(e.BioEmployeeId.Value)))
            .Select(e => new { e.EmployeeId, e.BioEmployeeId, e.FullName, e.Department })
            .ToListAsync();

        var dtos = items.Select(item =>
        {
            var emp = item.EmployeeId.HasValue
                ? employees.FirstOrDefault(e => e.EmployeeId == item.EmployeeId.Value)
                : employees.FirstOrDefault(e => e.BioEmployeeId == item.BioEmployeeId);

            var normalizedStatus = ExemptionStatus.InferStatus(item.Status, item.ApprovedBy);

            return new ExemptionListDto
            {
                ExemptionId = item.ExemptionId,
                BioEmployeeId = item.BioEmployeeId,
                EmployeeId = item.EmployeeId ?? emp?.EmployeeId,
                EmployeeName = emp?.FullName ?? $"كود: {item.BioEmployeeId}",
                Department = emp?.Department,
                ExemptionDate = item.ExemptionDate,
                ReasonCode = item.ReasonCode,
                Description = item.Description,
                ApprovedBy = item.ApprovedBy,
                CreatedDate = item.CreatedDate,
                IsFullDay = item.IsFullDay,
                Hours = item.Hours,
                IsDeducted = item.IsDeducted,
                Notes = item.Notes,
                CreatedBy = item.CreatedBy,
                Status = normalizedStatus
            };
        }).ToList();

        return new PagedResult<ExemptionListDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    public async Task<ExemptionStatsDto> GetExemptionStatsAsync(ExemptionFilterDto filter)
    {
        var query = await BuildExemptionsQueryAsync(filter);

        var all = await query
            .Select(e => new
            {
                e.ReasonCode,
                e.IsDeducted,
                e.Status,
                e.ApprovedBy
            })
            .ToListAsync();

        var normalized = all
            .Select(e => new
            {
                e.ReasonCode,
                e.IsDeducted,
                Status = ExemptionStatus.InferStatus(e.Status, e.ApprovedBy)
            })
            .ToList();

        return new ExemptionStatsDto
        {
            TotalCount = normalized.Count,
            ApprovedCount = normalized.Count(e => e.Status == ExemptionStatus.Approved),
            PendingCount = normalized.Count(e => e.Status == ExemptionStatus.Pending),
            RejectedCount = normalized.Count(e => e.Status == ExemptionStatus.Rejected),
            PermissionCount = normalized.Count(e => ExemptionReasonCodes.IsPermissionType(e.ReasonCode)),
            ErrandCount = normalized.Count(e => ExemptionReasonCodes.IsErrandType(e.ReasonCode)),
            EarlyLeaveCount = normalized.Count(e => ExemptionReasonCodes.IsEarlyLeaveType(e.ReasonCode)),
            DeductedCount = normalized.Count(e => e.IsDeducted),
            NotDeductedCount = normalized.Count(e => !e.IsDeducted)
        };
    }

    private async Task<IQueryable<DailyExemption>> BuildExemptionsQueryAsync(ExemptionFilterDto filter)
    {
        var query = _db.DailyExemptions.AsNoTracking().AsQueryable();

        if (filter.DateFrom.HasValue)
        {
            var fromDate = filter.DateFrom.Value.Date;
            query = query.Where(e => e.ExemptionDate >= fromDate);
        }

        if (filter.DateTo.HasValue)
        {
            var toExclusive = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(e => e.ExemptionDate < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(filter.ReasonCode))
        {
            var reasonCode = filter.ReasonCode.Trim();
            query = query.Where(e => e.ReasonCode.Contains(reasonCode));
        }

        if (filter.IsDeducted.HasValue)
            query = query.Where(e => e.IsDeducted == filter.IsDeducted.Value);

        var desiredStatus = !string.IsNullOrWhiteSpace(filter.Status)
            ? filter.Status!.Trim()
            : filter.IsApproved == true
                ? ExemptionStatus.Approved
                : filter.IsApproved == false
                    ? ExemptionStatus.Pending
                    : null;

        if (!string.IsNullOrWhiteSpace(desiredStatus))
        {
            if (desiredStatus == ExemptionStatus.Pending)
            {
                query = query.Where(e =>
                    e.Status == ExemptionStatus.Pending ||
                    ((e.Status == null || e.Status == "") && (e.ApprovedBy == null || e.ApprovedBy == "")));
            }
            else if (desiredStatus == ExemptionStatus.Approved)
            {
                query = query.Where(e =>
                    e.Status == ExemptionStatus.Approved ||
                    ((e.Status == null || e.Status == "") && (e.ApprovedBy != null && e.ApprovedBy != "")));
            }
            else if (desiredStatus == ExemptionStatus.Rejected)
            {
                query = query.Where(e => e.Status == ExemptionStatus.Rejected);
            }
            else
            {
                query = query.Where(e => e.Status == desiredStatus);
            }
        }

        if (filter.EmployeeId.HasValue ||
            !string.IsNullOrWhiteSpace(filter.SearchText) ||
            !string.IsNullOrWhiteSpace(filter.Department))
        {
            var employeesQuery = _db.Employees.AsNoTracking().AsQueryable();

            if (filter.EmployeeId.HasValue)
                employeesQuery = employeesQuery.Where(e => e.EmployeeId == filter.EmployeeId.Value);

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

            var employees = await employeesQuery
                .Select(e => new { e.EmployeeId, e.BioEmployeeId })
                .ToListAsync();

            if (!employees.Any())
                return query.Where(e => false);

            var employeeIds = employees.Select(e => e.EmployeeId).Distinct().ToList();
            var bioIds = employees
                .Where(e => e.BioEmployeeId.HasValue)
                .Select(e => e.BioEmployeeId!.Value)
                .Distinct()
                .ToList();

            query = query.Where(e =>
                (e.EmployeeId.HasValue && employeeIds.Contains(e.EmployeeId.Value)) ||
                bioIds.Contains(e.BioEmployeeId));
        }

        return query;
    }

    public async Task<(bool Success, string Message)> CreateExemptionAsync(
        ExemptionCreateDto dto, string currentUserName)
    {
        // جلب بيانات الموظف
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == dto.EmployeeId);
        if (employee == null)
            return (false, "الموظف غير موجود.");

        if (employee.BioEmployeeId == null)
            return (false, "الموظف ليس لديه كود بصمة.");

        // التحقق من عدم وجود استثناء لنفس اليوم (مش المرفوض)
        var exists = await _db.DailyExemptions.AnyAsync(e =>
            e.BioEmployeeId == employee.BioEmployeeId.Value &&
            e.ExemptionDate.Date == dto.ExemptionDate.Date &&
            e.Status != ExemptionStatus.Rejected);
        if (exists)
            return (false, "يوجد استثناء مسجل بالفعل لهذا الموظف في هذا اليوم.");

        // التحقق من أن اليوم ليس راحة للموظف
        var shift = await _db.EmployeeShifts.AsNoTracking()
            .Where(s => s.EmployeeId == dto.EmployeeId
                     && s.EffectiveFrom <= dto.ExemptionDate
                     && (s.EffectiveTo == null || s.EffectiveTo >= dto.ExemptionDate))
            .OrderByDescending(s => s.EffectiveFrom)
            .Select(s => new { s.OffDay1, s.OffDay2 })
            .FirstOrDefaultAsync();

        var offDays = new List<DayOfWeek>();
        if (shift?.OffDay1 != null) offDays.Add((DayOfWeek)shift.OffDay1.Value);
        if (shift?.OffDay2 != null) offDays.Add((DayOfWeek)shift.OffDay2.Value);
        if (offDays.Contains(dto.ExemptionDate.DayOfWeek))
            return (false, "هذا اليوم هو راحة أسبوعية للموظف - لا حاجة لاستثناء.");

        // تحديد IsDeducted
        var isDeducted = dto.IsDeducted ?? ExemptionReasonCodes.DefaultIsDeducted(dto.ReasonCode);

        // التحقق من Hours لو مش يوم كامل
        if (!dto.IsFullDay && (!dto.Hours.HasValue || dto.Hours.Value <= 0))
            return (false, "يرجى تحديد عدد الساعات للاستثناء الجزئي.");

        // ✅ Admin + AccountManager + HrManager → معتمد مباشرة
        // غير كده → Pending (قيد الاعتماد)
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == currentUserName);
        var canAutoApprove = IsExemptionManagerRole(user?.Role);

        var status = canAutoApprove ? ExemptionStatus.Approved : ExemptionStatus.Pending;
        var approvedBy = canAutoApprove ? currentUserName : null;

        // إنشاء الاستثناء
        var exemption = new DailyExemption
        {
            EmployeeId = dto.EmployeeId,
            BioEmployeeId = employee.BioEmployeeId.Value,
            ExemptionDate = dto.ExemptionDate.Date,
            ReasonCode = dto.ReasonCode,
            Description = dto.Description,
            IsFullDay = dto.IsFullDay,
            Hours = dto.IsFullDay ? null : dto.Hours,
            IsDeducted = isDeducted,
            Notes = dto.Notes,
            CreatedBy = currentUserName,
            Status = status,
            ApprovedBy = approvedBy,
            CreatedDate = DateTime.Now
        };

        _db.DailyExemptions.Add(exemption);
        await _db.SaveChangesAsync();

        // ✅ لو معتمد مباشرة → إنشاء/تحديث سجل الحضور
        if (status == ExemptionStatus.Approved)
        {
            await CreateOrUpdateExemptionAttendanceAsync(exemption);
            await _db.SaveChangesAsync();
        }

        await _audit.LogAsync("DailyExemption", "Create",
            exemption.ExemptionId.ToString(), null,
            new { BioEmployeeId = exemption.BioEmployeeId, exemption.ExemptionDate, exemption.ReasonCode, Status = status, exemption.IsDeducted },
            currentUserName);

        if (status == ExemptionStatus.Pending)
            await NotifyPendingExemptionAsync(exemption, employee.FullName, currentUserName);
        else
            await NotifyAutoApprovedExemptionAsync(exemption, employee.FullName, currentUserName);

        return (true, status == ExemptionStatus.Approved
            ? " تم تسجيل الاستثناء واعتماده بنجاح."
            : " تم تسجيل الاستثناء وهو قيد الاعتماد من المدير.");
    }

    // ════════════════════════════════════════════════════════════
    //  اعتماد استثناء
    // ════════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message)> ApproveExemptionAsync(
        int exemptionId, string currentUserName)
    {
        var approver = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == currentUserName);
        if (!IsExemptionManagerRole(approver?.Role))
            return (false, "ليس لديك صلاحية اعتماد الاستثناءات.");

        var exemption = await _db.DailyExemptions.FindAsync(exemptionId);
        if (exemption == null)
            return (false, "الاستثناء غير موجود.");

        if (exemption.Status == ExemptionStatus.Approved)
            return (false, "الاستثناء معتمد بالفعل.");

        if (exemption.Status == ExemptionStatus.Rejected)
            return (false, "لا يمكن اعتماد استثناء مرفوض.");

        exemption.Status = ExemptionStatus.Approved;
        exemption.ApprovedBy = currentUserName;

        // ✅ إنشاء/تحديث سجل الحضور
        await CreateOrUpdateExemptionAttendanceAsync(exemption);
        await _db.SaveChangesAsync();

        await _audit.LogAsync<object>("DailyExemption", "Approve",
    exemptionId.ToString(),
    new { Status = ExemptionStatus.Pending },
    new { Status = ExemptionStatus.Approved, ApprovedBy = currentUserName },
    currentUserName);

        var employeeName = await GetEmployeeNameForExemptionAsync(exemption);
        await NotifyExemptionDecisionAsync(exemption, employeeName, true, currentUserName);

        return (true, "تم اعتماد الاستثناء بنجاح.");
    }

    // ════════════════════════════════════════════════════════════
    //  رفض استثناء
    // ════════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message)> RejectExemptionAsync(
        int exemptionId, string currentUserName, string? reason = null)
    {
        var approver = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == currentUserName);
        if (!IsExemptionManagerRole(approver?.Role))
            return (false, "ليس لديك صلاحية رفض الاستثناءات.");

        var exemption = await _db.DailyExemptions.FindAsync(exemptionId);
        if (exemption == null)
            return (false, "الاستثناء غير موجود.");

        if (exemption.Status == ExemptionStatus.Rejected)
            return (false, "الاستثناء مرفوض بالفعل.");

        if (exemption.Status == ExemptionStatus.Approved)
            return (false, "لا يمكن رفض استثناء معتمد. احذفه بدلاً من ذلك.");

        exemption.Status = ExemptionStatus.Rejected;
        // نحفظ سبب الرفض في Notes
        if (!string.IsNullOrWhiteSpace(reason))
            exemption.Notes = string.IsNullOrWhiteSpace(exemption.Notes)
                ? $"[سبب الرفض: {reason}]"
                : $"{exemption.Notes}\n[سبب الرفض: {reason}]";

        await _db.SaveChangesAsync();

        await _audit.LogAsync<object>("DailyExemption", "Reject",
    exemptionId.ToString(),
    new { Status = ExemptionStatus.Pending },
    new { Status = ExemptionStatus.Rejected, RejectionReason = reason },
    currentUserName);

        var employeeName = await GetEmployeeNameForExemptionAsync(exemption);
        await NotifyExemptionDecisionAsync(exemption, employeeName, false, currentUserName, reason);

        return (true, "تم رفض الاستثناء.");
    }

    public async Task<(bool Success, string Message)> DeleteExemptionAsync(
        int exemptionId, string currentUserName)
    {
        var exemption = await _db.DailyExemptions.FindAsync(exemptionId);
        if (exemption == null)
            return (false, "الاستثناء غير موجود.");

        // لو معتمد، شيل سجل الحضور المرتبط بيه
        var normalizedStatus = ExemptionStatus.InferStatus(exemption.Status, exemption.ApprovedBy);
        if (normalizedStatus == ExemptionStatus.Approved)
        {
            var relatedAttendance = await _db.Attendances.FirstOrDefaultAsync(a =>
                a.BiometricCode == exemption.BioEmployeeId &&
                a.LogDate.Date == exemption.ExemptionDate.Date &&
                (a.Status == AttendanceStatus.Errand ||
                 a.Status == AttendanceStatus.Permission ||
                 a.Status == AttendanceStatus.EarlyLeave));

            if (relatedAttendance != null)
                _db.Attendances.Remove(relatedAttendance);
        }

        _db.DailyExemptions.Remove(exemption);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("DailyExemption", "Delete",
            exemptionId.ToString(),
            new { exemption.BioEmployeeId, exemption.ExemptionDate, exemption.ReasonCode, exemption.Status }, null,
            currentUserName);

        return (true, "✅ تم حذف الاستثناء.");
    }

    // ════════════════════════════════════════════════════════════
    //  Helper: إنشاء/تحديث سجل حضور للاستثناء
    // ════════════════════════════════════════════════════════════
    private async Task CreateOrUpdateExemptionAttendanceAsync(DailyExemption exemption)
    {
        var date = exemption.ExemptionDate.Date;

        // جيب شيفت الموظف
        var shift = await _db.EmployeeShifts.AsNoTracking()
            .Where(s => s.BiometricCode == exemption.BioEmployeeId
                     && s.EffectiveFrom <= date
                     && (s.EffectiveTo == null || s.EffectiveTo >= date))
            .Select(s => new { s.StartTime, s.EndTime })
            .FirstOrDefaultAsync();

        var startTime = shift?.StartTime ?? new TimeOnly(8, 0);
        var endTime = shift?.EndTime ?? new TimeOnly(17, 0);

        // هل في سجل حضور موجود بالفعل (من البصمة)؟
        var existing = await _db.Attendances.FirstOrDefaultAsync(a =>
            a.BiometricCode == exemption.BioEmployeeId &&
            a.LogDate.Date == date);

        var isErrand = ExemptionReasonCodes.IsErrandType(exemption.ReasonCode);
        var isPermission = ExemptionReasonCodes.IsPermissionType(exemption.ReasonCode);
        var isEarlyLeave = ExemptionReasonCodes.IsEarlyLeaveType(exemption.ReasonCode);
        var isDeducted = exemption.IsDeducted;

        if (existing != null)
        {
            // لو في سجل حضور فعلي (من البصمة)، نعدّل الحالة
            if (existing.TimeIn != null)
            {
                if (isErrand)
                {
                    existing.Status = AttendanceStatus.Errand;
                }
                else if (isPermission && !isDeducted)
                {
                    // إذن بدون خصم → نعتبره حاضر
                    existing.Status = AttendanceStatus.Present;
                    existing.LateMinutes = 0;
                }
                else if (isEarlyLeave)
                {
                    // انصراف مبكر → نشيل خصم الانصراف
                    existing.EarlyLeaveMinutes = 0;
                    existing.Status = AttendanceStatus.EarlyLeave;
                }
                // إذن مع خصم → نسيب سجل البصمة زي هو
            }
            else
            {
                ApplyExemptionToAttendanceRecord(existing, isErrand, isPermission, isEarlyLeave, isDeducted, startTime, endTime);
            }
        }
        else
        {
            // مفيش سجل حضور → نعمل سجل جديد
            var attendance = new Attendance
            {
                BiometricCode = exemption.BioEmployeeId,
                LogDate = date,
                LateMinutes = 0,
                EarlyLeaveMinutes = 0
            };

            ApplyExemptionToAttendanceRecord(attendance, isErrand, isPermission, isEarlyLeave, isDeducted, startTime, endTime);
            _db.Attendances.Add(attendance);
        }
    }

    private static void ApplyExemptionToAttendanceRecord(
        Attendance attendance,
        bool isErrand,
        bool isPermission,
        bool isEarlyLeave,
        bool isDeducted,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        attendance.LateMinutes = 0;
        attendance.EarlyLeaveMinutes = 0;
        attendance.PenaltyHours = 0;

        if (isErrand)
        {
            attendance.TimeIn = startTime;
            attendance.TimeOut = endTime;
            attendance.TotalHours = (decimal)(endTime.ToTimeSpan() - startTime.ToTimeSpan()).TotalHours;
            attendance.Status = AttendanceStatus.Errand;
        }
        else if (isPermission && !isDeducted)
        {
            attendance.TimeIn = startTime;
            attendance.TimeOut = endTime;
            attendance.TotalHours = (decimal)(endTime.ToTimeSpan() - startTime.ToTimeSpan()).TotalHours;
            attendance.Status = AttendanceStatus.Permission;
        }
        else if (isPermission && isDeducted)
        {
            attendance.TimeIn = null;
            attendance.TimeOut = null;
            attendance.TotalHours = null;
            attendance.Status = AttendanceStatus.Permission;
        }
        else if (isEarlyLeave)
        {
            attendance.TimeIn = startTime;
            attendance.TimeOut = endTime;
            attendance.TotalHours = (decimal)(endTime.ToTimeSpan() - startTime.ToTimeSpan()).TotalHours;
            attendance.Status = AttendanceStatus.EarlyLeave;
        }
        else
        {
            attendance.TimeIn = null;
            attendance.TimeOut = null;
            attendance.TotalHours = null;
            attendance.Status = AttendanceStatus.Permission;
        }
    }

    private static bool IsExemptionManagerRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;

        return role == SystemRoles.Admin
            || role == SystemRoles.AccountManager
            || role == SystemRoles.HrManager
            || role == "HRManager";
    }

    private async Task<string> GetEmployeeNameForExemptionAsync(DailyExemption exemption)
    {
        var employeeName = await _db.Employees.AsNoTracking()
            .Where(e => (exemption.EmployeeId.HasValue && e.EmployeeId == exemption.EmployeeId.Value)
                     || (e.BioEmployeeId.HasValue && e.BioEmployeeId.Value == exemption.BioEmployeeId))
            .Select(e => e.FullName)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(employeeName)
            ? $"كود: {exemption.BioEmployeeId}"
            : employeeName!;
    }

    private async Task NotifyExemptionManagersAsync(string title, string message, string actor, int exemptionId)
    {
        var roles = new[] { SystemRoles.Admin, SystemRoles.AccountManager, SystemRoles.HrManager }
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct();

        foreach (var role in roles)
        {
            await _notify.NotifyRoleAsync(title, message, role, actor,
                "frmDailyExemptions", "DailyExemptions", exemptionId);
        }
    }

    private async Task NotifyPendingExemptionAsync(DailyExemption exemption, string employeeName, string actor)
    {
        var title = "📝 استثناء جديد قيد الاعتماد";
        var message = $"تم تسجيل استثناء جديد للموظف {employeeName} بتاريخ {exemption.ExemptionDate:yyyy/MM/dd} " +
                      $"بسبب ({exemption.ReasonCode}) وهو الآن قيد الاعتماد.";

        await NotifyExemptionManagersAsync(title, message, actor, exemption.ExemptionId);
    }

    private async Task NotifyAutoApprovedExemptionAsync(DailyExemption exemption, string employeeName, string actor)
    {
        var title = "✅ تم اعتماد استثناء مباشرة";
        var message = $"تم تسجيل واعتماد استثناء للموظف {employeeName} بتاريخ {exemption.ExemptionDate:yyyy/MM/dd} " +
                      $"بسبب ({exemption.ReasonCode}) بواسطة {actor}.";

        await NotifyExemptionManagersAsync(title, message, actor, exemption.ExemptionId);
    }

    private async Task NotifyExemptionDecisionAsync(DailyExemption exemption, string employeeName, bool isApproved, string actor, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(exemption.CreatedBy))
            return;

        var title = isApproved ? "✅ تم اعتماد الاستثناء" : "❌ تم رفض الاستثناء";
        var message = isApproved
            ? $"تم اعتماد الاستثناء الخاص بالموظف {employeeName} بتاريخ {exemption.ExemptionDate:yyyy/MM/dd} " +
              $"بسبب ({exemption.ReasonCode}) بواسطة {actor}."
            : $"تم رفض الاستثناء الخاص بالموظف {employeeName} بتاريخ {exemption.ExemptionDate:yyyy/MM/dd} " +
              $"بسبب ({exemption.ReasonCode}) بواسطة {actor}." +
              (!string.IsNullOrWhiteSpace(reason) ? $" سبب الرفض: {reason}" : string.Empty);

        await _notify.AddAsync(title, message, exemption.CreatedBy, actor,
            "frmDailyExemptions", "DailyExemptions", exemption.ExemptionId);
    }

}