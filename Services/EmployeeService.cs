using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services
{
    public class EmployeeService : IEmployeeService
    {
        private readonly db24804Context _db;
        private readonly IAuditService _audit;
        private readonly NotificationService _notify;

        public EmployeeService(
            db24804Context db,
            IAuditService audit,
            NotificationService notify)
        {
            _db = db;
            _audit = audit;
            _notify = notify;
        }

        public async Task<PagedResult<EmployeeListDto>> GetEmployeesAsync(EmployeeFilterDto filter)
        {
            var query = _db.Employees.AsQueryable();

            // بحث بالاسم أو الرقم القومي أو الموبايل أو الوظيفة
            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim();
                query = query.Where(e =>
                    e.FullName.Contains(term) ||
                    e.NationalId.Contains(term) ||
                    (e.MobilePhone != null && e.MobilePhone.Contains(term)) ||
                    (e.JobTitle != null && e.JobTitle.Contains(term)) ||
                    (e.EmployeeId.ToString() == term));
            }

            // فلتر القسم
            if (!string.IsNullOrWhiteSpace(filter.Department))
            {
                query = query.Where(e => e.Department == filter.Department);
            }

            // فلتر الحالة
            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                query = query.Where(e => e.Status == filter.Status);
            }

            // فلتر النوع
            if (!string.IsNullOrWhiteSpace(filter.Gender))
            {
                query = query.Where(e => e.Gender == filter.Gender);
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(e => e.CreatedAt)
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(e => new EmployeeListDto
                {
                    EmployeeId = e.EmployeeId,
                    FullName = e.FullName,
                    JobTitle = e.JobTitle,
                    Department = e.Department,
                    NationalId = e.NationalId,
                    Gender = e.Gender,
                    MobilePhone = e.MobilePhone,
                    EmailAddress = e.EmailAddress,
                    HireDate = e.HireDate,
                    EndDate = e.EndDate,
                    CurrentSalaryBase = e.CurrentSalaryBase,
                    Status = e.Status,
                    BioEmployeeId = e.BioEmployeeId,
                    IsPermanentlyExempt = e.IsPermanentlyExempt,
                    CreatedBy = e.CreatedBy,
                    CreatedAt = e.CreatedAt
                })
                .ToListAsync();

            return new PagedResult<EmployeeListDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize
            };
        }

        public async Task<EmployeeDetailDto?> GetEmployeeDetailAsync(int employeeId)
        {
            var employee = await _db.Employees
                .Include(e => e.SalaryHistories)
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

            if (employee == null) return null;

            var salaryHistory = employee.SalaryHistories
                .OrderByDescending(s => s.ChangeDate)
                .Select(s => new SalaryHistoryDto
                {
                    SalaryHistoryId = s.SalaryHistoryId,
                    EmployeeId = s.EmployeeId,
                    OldSalary = s.OldSalary,
                    NewSalary = s.NewSalary,
                    ChangeDate = s.ChangeDate,
                    Reason = s.Reason,
                    CreatedBy = s.CreatedBy,
                    CreatedAt = s.CreatedAt
                }).ToList();

            // حساب عدد الرواتب المدفوعة
            var paidPayrolls = await _db.Payrolls
                .CountAsync(p => p.EmployeeId == employeeId && p.PaymentStatus == "مدفوع");

            return new EmployeeDetailDto
            {
                EmployeeId = employee.EmployeeId,
                FullName = employee.FullName,
                JobTitle = employee.JobTitle,
                Department = employee.Department,
                NationalId = employee.NationalId,
                Gender = employee.Gender,
                BirthDate = employee.BirthDate,
                Qualification = employee.Qualification,
                YearQualification = employee.Yearqualification,
                Address = employee.Address,
                MobilePhone = employee.MobilePhone,
                MobilePhone2 = employee.MobilePhone2,
                EmailAddress = employee.EmailAddress,
                HireDate = employee.HireDate,
                EndDate = employee.EndDate,
                BioEmployeeId = employee.BioEmployeeId,
                IsPermanentlyExempt = employee.IsPermanentlyExempt,
                CurrentSalaryBase = employee.CurrentSalaryBase,
                Status = employee.Status,
                Notes = employee.Notes,
                CreatedBy = employee.CreatedBy,
                CreatedAt = employee.CreatedAt,
                SalaryHistory = salaryHistory,
                Stats = new EmployeeStatsDto
                {
                    TotalSalaries = employee.CurrentSalaryBase ?? 0
                }
            };
        }

        public async Task<EmployeeFormDto?> GetEmployeeForEditAsync(int employeeId)
        {
            var employee = await _db.Employees.FindAsync(employeeId);
            if (employee == null) return null;

            return new EmployeeFormDto
            {
                EmployeeId = employee.EmployeeId,
                FullName = employee.FullName,
                JobTitle = employee.JobTitle,
                Department = employee.Department,
                NationalId = employee.NationalId,
                Gender = employee.Gender,
                BirthDate = employee.BirthDate,
                Qualification = employee.Qualification,
                YearQualification = employee.Yearqualification,
                Address = employee.Address,
                MobilePhone = employee.MobilePhone,
                MobilePhone2 = employee.MobilePhone2,
                EmailAddress = employee.EmailAddress,
                HireDate = employee.HireDate,
                EndDate = employee.EndDate,
                BioEmployeeId = employee.BioEmployeeId,
                IsPermanentlyExempt = employee.IsPermanentlyExempt,
                CurrentSalaryBase = employee.CurrentSalaryBase,
                Status = employee.Status,
                Notes = employee.Notes,
                CreatedBy = employee.CreatedBy,
                OldSalary = employee.CurrentSalaryBase
            };
        }

        public async Task<(bool Success, string Message, int? EmployeeId)> CreateEmployeeAsync(EmployeeFormDto dto, string userName)
        {
            // التحقق من الرقم القومي المكرر
            var exists = await _db.Employees.AnyAsync(e => e.NationalId == dto.NationalId);
            if (exists)
            {
                return (false, "الرقم القومي مسجل بالفعل لموظف آخر", null);
            }

            // التحقق من كود البصمة المكرر
            if (dto.BioEmployeeId.HasValue)
            {
                var bioExists = await _db.Employees.AnyAsync(e => e.BioEmployeeId == dto.BioEmployeeId);
                if (bioExists)
                {
                    return (false, "كود البصمة مسجل بالفعل لموظف آخر", null);
                }
            }

            using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                var employee = new Employee
                {
                    FullName = dto.FullName.Trim(),
                    JobTitle = dto.JobTitle?.Trim(),
                    Department = dto.Department?.Trim(),
                    NationalId = dto.NationalId.Trim(),
                    Gender = dto.Gender,
                    BirthDate = dto.BirthDate,
                    Qualification = dto.Qualification?.Trim(),
                    Yearqualification = dto.YearQualification?.Trim(),
                    Address = dto.Address?.Trim(),
                    MobilePhone = dto.MobilePhone?.Trim(),
                    MobilePhone2 = dto.MobilePhone2?.Trim(),
                    EmailAddress = dto.EmailAddress?.Trim(),
                    HireDate = dto.HireDate ?? DateTime.Now,
                    EndDate = dto.EndDate,
                    BioEmployeeId = dto.BioEmployeeId,
                    IsPermanentlyExempt = dto.IsPermanentlyExempt ?? false,
                    CurrentSalaryBase = dto.CurrentSalaryBase ?? 0,
                    Status = dto.Status ?? EmployeeStatuses.Active,
                    Notes = dto.Notes?.Trim(),
                    CreatedBy = userName,
                    CreatedAt = DateTime.Now
                };

                _db.Employees.Add(employee);
                await _db.SaveChangesAsync();

                // تسجيل المرتب الأول في تاريخ المرتبات
                if (dto.CurrentSalaryBase.HasValue && dto.CurrentSalaryBase.Value > 0)
                {
                    var salaryHistory = new SalaryHistory
                    {
                        EmployeeId = employee.EmployeeId,
                        OldSalary = 0,
                        NewSalary = dto.CurrentSalaryBase.Value,
                        ChangeDate = DateOnly.FromDateTime(DateTime.Now),
                        Reason = "تعيين جديد",
                        CreatedBy = userName,
                        CreatedAt = DateTime.Now
                    };

                    _db.SalaryHistories.Add(salaryHistory);
                    await _db.SaveChangesAsync();
                }

                // AuditLog
                await _audit.LogAsync(
                    "Employees",
                    "Insert",
                    employee.EmployeeId.ToString(),
                    (object?)null,
                    new
                    {
                        employee.EmployeeId,
                        employee.FullName,
                        employee.JobTitle,
                        employee.Department,
                        employee.NationalId,
                        employee.CurrentSalaryBase,
                        employee.Status,
                        employee.HireDate,
                        CreatedBy = userName
                    },
                    userName
                );

                // إشعارات
                var title = "موظف جديد";
                var message = $"تم إضافة موظف جديد: {employee.FullName} - {employee.JobTitle ?? "بدون وظيفة"} في قسم {employee.Department ?? "غير محدد"}";

                await _notify.NotifyRoleAsync(title, message, SystemRoles.Admin, userName,
                    "frm_Employeeslist", "Employees", employee.EmployeeId);

                await _notify.NotifyRoleAsync(title, message, SystemRoles.AccountManager, userName,
                    "frm_Employeeslist", "Employees", employee.EmployeeId);

                await transaction.CommitAsync();

                return (true, "تم إضافة الموظف بنجاح", employee.EmployeeId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"حدث خطأ أثناء إضافة الموظف: {ex.Message}", null);
            }
        }

        public async Task<(bool Success, string Message)> UpdateEmployeeAsync(int employeeId, EmployeeFormDto dto, string userName)
        {
            var employee = await _db.Employees.FindAsync(employeeId);
            if (employee == null)
            {
                return (false, "لم يتم العثور على الموظف");
            }

            // التحقق من الرقم القومي المكرر (باستثناء الموظف الحالي)
            var nationalIdExists = await _db.Employees
                .AnyAsync(e => e.NationalId == dto.NationalId && e.EmployeeId != employeeId);
            if (nationalIdExists)
            {
                return (false, "الرقم القومي مسجل بالفعل لموظف آخر");
            }

            // التحقق من كود البصمة المكرر
            if (dto.BioEmployeeId.HasValue)
            {
                var bioExists = await _db.Employees
                    .AnyAsync(e => e.BioEmployeeId == dto.BioEmployeeId && e.EmployeeId != employeeId);
                if (bioExists)
                {
                    return (false, "كود البصمة مسجل بالفعل لموظف آخر");
                }
            }

            using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                // حفظ البيانات القديمة للـ AuditLog
                var oldData = new
                {
                    employee.FullName,
                    employee.JobTitle,
                    employee.Department,
                    employee.NationalId,
                    employee.Gender,
                    employee.BirthDate,
                    employee.Qualification,
                    employee.Yearqualification,
                    employee.Address,
                    employee.MobilePhone,
                    employee.MobilePhone2,
                    employee.EmailAddress,
                    employee.HireDate,
                    employee.EndDate,
                    employee.BioEmployeeId,
                    employee.IsPermanentlyExempt,
                    employee.CurrentSalaryBase,
                    employee.Status,
                    employee.Notes
                };

                // تحديث البيانات
                employee.FullName = dto.FullName.Trim();
                employee.JobTitle = dto.JobTitle?.Trim();
                employee.Department = dto.Department?.Trim();
                employee.NationalId = dto.NationalId.Trim();
                employee.Gender = dto.Gender;
                employee.BirthDate = dto.BirthDate;
                employee.Qualification = dto.Qualification?.Trim();
                employee.Yearqualification = dto.YearQualification?.Trim();
                employee.Address = dto.Address?.Trim();
                employee.MobilePhone = dto.MobilePhone?.Trim();
                employee.MobilePhone2 = dto.MobilePhone2?.Trim();
                employee.EmailAddress = dto.EmailAddress?.Trim();
                employee.HireDate = dto.HireDate;
                employee.EndDate = dto.EndDate;
                employee.BioEmployeeId = dto.BioEmployeeId;
                employee.IsPermanentlyExempt = dto.IsPermanentlyExempt;
                employee.Status = dto.Status;
                employee.Notes = dto.Notes?.Trim();

                // تتبع تغيير المرتب
                var newSalary = dto.CurrentSalaryBase ?? 0;
                var oldSalary = oldData.CurrentSalaryBase ?? 0;

                if (newSalary != oldSalary)
                {
                    employee.CurrentSalaryBase = newSalary;

                    var salaryHistory = new SalaryHistory
                    {
                        EmployeeId = employeeId,
                        OldSalary = oldSalary,
                        NewSalary = newSalary,
                        ChangeDate = DateOnly.FromDateTime(DateTime.Now),
                        Reason = !string.IsNullOrWhiteSpace(dto.SalaryChangeReason)
                            ? dto.SalaryChangeReason
                            : "تعديل المرتب",
                        CreatedBy = userName,
                        CreatedAt = DateTime.Now
                    };

                    _db.SalaryHistories.Add(salaryHistory);
                }
                else
                {
                    employee.CurrentSalaryBase = newSalary;
                }

                await _db.SaveChangesAsync();

                // AuditLog
                var newData = new
                {
                    employee.FullName,
                    employee.JobTitle,
                    employee.Department,
                    employee.NationalId,
                    employee.Gender,
                    employee.BirthDate,
                    employee.Qualification,
                    employee.Yearqualification,
                    employee.Address,
                    employee.MobilePhone,
                    employee.MobilePhone2,
                    employee.EmailAddress,
                    employee.HireDate,
                    employee.EndDate,
                    employee.BioEmployeeId,
                    employee.IsPermanentlyExempt,
                    employee.CurrentSalaryBase,
                    employee.Status,
                    employee.Notes
                };

                await _audit.LogAsync(
                    "Employees",
                    "Update",
                    employeeId.ToString(),
                    oldData,
                    newData,
                    userName
                );

                // إشعار بالتعديل
                if (newSalary != oldSalary)
                {
                    var title = "تغيير مرتب موظف";
                    var message = $"تم تغيير مرتب {employee.FullName} من {oldSalary:N2} ج.م إلى {newSalary:N2} ج.م";

                    await _notify.NotifyRoleAsync(title, message, SystemRoles.Admin, userName,
                        "frm_Employeeslist", "Employees", employeeId);

                    await _notify.NotifyRoleAsync(title, message, SystemRoles.AccountManager, userName,
                        "frm_Employeeslist", "Employees", employeeId);
                }

                await transaction.CommitAsync();

                return (true, "تم تحديث بيانات الموظف بنجاح");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"حدث خطأ أثناء تحديث الموظف: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> ChangeStatusAsync(int employeeId, string newStatus, string reason, string userName)
        {
            var employee = await _db.Employees.FindAsync(employeeId);
            if (employee == null)
            {
                return (false, "لم يتم العثور على الموظف");
            }

            var oldStatus = employee.Status;
            if (oldStatus == newStatus)
            {
                return (false, "الموظف بالفعل في هذه الحالة");
            }

            using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                employee.Status = newStatus;

                // لو الاستقالة أو إنهاء العمل - سجل تاريخ الانتهاء
                if (newStatus == EmployeeStatuses.Resigned && !employee.EndDate.HasValue)
                {
                    employee.EndDate = DateTime.Now;
                }

                await _db.SaveChangesAsync();

                // AuditLog
                await _audit.LogAsync<object?>(
                    "Employees",
                    "Update",
                    employeeId.ToString(),
                    (object?)new { Status = oldStatus },
                    (object?)new { Status = newStatus, Reason = reason },
                    userName
                );

                // إشعار تغيير الحالة
                var statusLabel = newStatus switch
                {
                    "نشط" => "تفعيل",
                    "موقوف" => "توقيف",
                    "مستقيل" => "استقالة",
                    "بالإجازة" => "إجازة",
                    _ => newStatus
                };

                var title = $"تغيير حالة موظف - {statusLabel}";
                var message = $"تم تغيير حالة {employee.FullName} من '{oldStatus}' إلى '{newStatus}'"
                            + (!string.IsNullOrEmpty(reason) ? $" - السبب: {reason}" : "");

                await _notify.NotifyRoleAsync(title, message, SystemRoles.Admin, userName,
                    "frm_Employeeslist", "Employees", employeeId);

                await _notify.NotifyRoleAsync(title, message, SystemRoles.AccountManager, userName,
                    "frm_Employeeslist", "Employees", employeeId);

                await transaction.CommitAsync();

                return (true, $"تم تغيير حالة الموظف إلى '{newStatus}' بنجاح");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"حدث خطأ: {ex.Message}");
            }
        }

        public async Task<EmployeeStatsDto> GetStatsAsync()
        {
            var employees = await _db.Employees.ToListAsync();

            return new EmployeeStatsDto
            {
                TotalEmployees = employees.Count,
                ActiveCount = employees.Count(e => e.Status == EmployeeStatuses.Active),
                SuspendedCount = employees.Count(e => e.Status == EmployeeStatuses.Suspended),
                ResignedCount = employees.Count(e => e.Status == EmployeeStatuses.Resigned),
                OnLeaveCount = employees.Count(e => e.Status == EmployeeStatuses.OnLeave),
                TotalSalaries = employees.Where(e => e.Status == EmployeeStatuses.Active)
                    .Sum(e => e.CurrentSalaryBase ?? 0),
                AverageSalary = employees.Where(e => e.Status == EmployeeStatuses.Active && e.CurrentSalaryBase > 0)
                    .Select(e => e.CurrentSalaryBase ?? 0)
                    .DefaultIfEmpty(0)
                    .Average(),
                DepartmentCount = employees.Where(e => !string.IsNullOrEmpty(e.Department))
                    .Select(e => e.Department)
                    .Distinct()
                    .Count()
            };
        }

        public async Task<List<SalaryHistoryDto>> GetSalaryHistoryAsync(int employeeId)
        {
            return await _db.SalaryHistories
                .Where(s => s.EmployeeId == employeeId)
                .OrderByDescending(s => s.ChangeDate)
                .Select(s => new SalaryHistoryDto
                {
                    SalaryHistoryId = s.SalaryHistoryId,
                    EmployeeId = s.EmployeeId,
                    OldSalary = s.OldSalary,
                    NewSalary = s.NewSalary,
                    ChangeDate = s.ChangeDate,
                    Reason = s.Reason,
                    CreatedBy = s.CreatedBy,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<List<string>> GetDepartmentsAsync()
        {
            return await _db.Employees
                .Where(e => !string.IsNullOrEmpty(e.Department))
                .Select(e => e.Department!)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync();
        }

        public async Task<List<EmployeeListDto>> SearchEmployeesAsync(string searchTerm, int maxResults = 20)
        {
            var query = _db.Employees.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim();
                query = query.Where(e =>
                    e.FullName.Contains(term) ||
                    e.NationalId.Contains(term) ||
                    (e.MobilePhone != null && e.MobilePhone.Contains(term)));
            }

            return await query
                .Where(e => e.Status == EmployeeStatuses.Active)
                .OrderBy(e => e.FullName)
                .Take(maxResults)
                .Select(e => new EmployeeListDto
                {
                    EmployeeId = e.EmployeeId,
                    FullName = e.FullName,
                    JobTitle = e.JobTitle,
                    Department = e.Department,
                    BioEmployeeId = e.BioEmployeeId,
                    NationalId = e.NationalId,
                    MobilePhone = e.MobilePhone,
                    CurrentSalaryBase = e.CurrentSalaryBase,
                    Status = e.Status
                })
                .ToListAsync();
        }
    }
}