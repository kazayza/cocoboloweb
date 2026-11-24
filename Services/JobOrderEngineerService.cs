using System.Data;
using System.Data.Common;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class JobOrderEngineerService
{
    public const string DesignRole = SystemRoles.DesignEngineer;
    public const string ExecutionRole = SystemRoles.ExecutionEngineer;
    public const string DesignSeat = "Design";
    public const string ExecutionSeat = "Execution";

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly NotificationService _notify;

    public JobOrderEngineerService(IDbContextFactory<db24804Context> dbFactory, NotificationService notify)
    {
        _dbFactory = dbFactory;
        _notify = notify;
    }

    public async Task<JobEngineerView> GetAsync(int partyId, string? username)
    {
        if (partyId <= 0)
            return new JobEngineerView();

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await TablesReadyAsync(db))
            return new JobEngineerView();

        var row = await ReadRowAsync(db, partyId);
        var names = await NamesAsync(db, row.DesignEmployeeId, row.ExecutionEmployeeId);
        var actor = await ActorAsync(db, username);
        return new JobEngineerView
        {
            Ready = true,
            DesignName = names.Design,
            ExecutionName = names.Execution,
            DesignEmployeeId = row.DesignEmployeeId,
            ExecutionEmployeeId = row.ExecutionEmployeeId,
            CanTransferDesign = CanTransfer(actor, DesignSeat, row.DesignEmployeeId),
            CanTransferExecution = CanTransfer(actor, ExecutionSeat, row.ExecutionEmployeeId)
        };
    }

    public async Task<bool> UserCanOpenJobAsync(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return false;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var actor = await ActorAsync(db, username);
        return actor.HasDesign || actor.HasExecution;
    }

    public async Task<JobEngineerClaim> ClaimOnOpenAsync(int partyId, string? username)
    {
        if (partyId <= 0 || string.IsNullOrWhiteSpace(username))
            return new JobEngineerClaim();

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await TablesReadyAsync(db))
            return new JobEngineerClaim();

        var actor = await ActorAsync(db, username);
        if (!actor.HasDesign && !actor.HasExecution)
            return new JobEngineerClaim { Ready = true };
        if (actor.EmployeeId is not int employeeId)
            return new JobEngineerClaim { Ready = true, Note = "اربط الحساب بموظف قبل استلام الجوب أوردر." };

        var dealBranch = await LatestDealBranchAsync(db, partyId);
        var factoryBranch = await FactoryBranchIdAsync(db);
        var row = await ReadRowAsync(db, partyId);
        var seat = ChooseSeat(actor, employeeId, row, dealBranch, factoryBranch);
        if (seat == null)
            return new JobEngineerClaim { Ready = true };

        var claimed = await ClaimSeatAsync(db, partyId, seat, employeeId, username.Trim());
        if (!claimed)
            return new JobEngineerClaim { Ready = true };

        var names = await NamesAsync(db, seat == DesignSeat ? employeeId : row.DesignEmployeeId, seat == ExecutionSeat ? employeeId : row.ExecutionEmployeeId);
        var takenName = seat == DesignSeat ? names.Design : names.Execution;
        try
        {
            await NotifyClaimAsync(db, partyId, seat, takenName, username.Trim());
        }
        catch
        {
            // الاستلام محفوظ حتى لو الإشعار فشل
        }
        return new JobEngineerClaim { Ready = true, ClaimedSeat = seat };
    }

    public async Task<List<JobEngineerCandidate>> CandidatesAsync(int partyId, string seat)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await TablesReadyAsync(db))
            return new();

        var row = await ReadRowAsync(db, partyId);
        var other = seat == DesignSeat ? row.ExecutionEmployeeId : row.DesignEmployeeId;
        var branch = seat == ExecutionSeat
            ? await FactoryBranchIdAsync(db)
            : await LatestDealBranchAsync(db, partyId);
        var holders = await RoleHoldersAsync(db, seat == ExecutionSeat ? ExecutionRole : DesignRole, seat == ExecutionSeat ? "مهندس تنفيذ" : "مهندس تصميم");
        var employees = await EmployeeNamesAsync(db, holders.Select(h => h.EmployeeId).Where(id => id.HasValue).Select(id => id!.Value));

        return holders
            .Where(h => h.EmployeeId is int id && id != other && employees.ContainsKey(id))
            .Where(h => !branch.HasValue || h.BranchId == branch.Value)
            .GroupBy(h => h.EmployeeId!.Value)
            .Select(g => new JobEngineerCandidate { EmployeeId = g.Key, Name = employees[g.Key] })
            .OrderBy(c => c.Name)
            .ToList();
    }

    public async Task<(bool Ok, string Message)> TransferAsync(int partyId, string seat, int toEmployeeId, string? reason, string? username)
    {
        var note = reason?.Trim();
        if (partyId <= 0 || toEmployeeId <= 0)
            return (false, "اختر المهندس.");
        if (string.IsNullOrWhiteSpace(note))
            return (false, "اكتب سبب التحويل.");
        if (note.Length > 300)
            note = note[..300];
        if (seat != DesignSeat && seat != ExecutionSeat)
            return (false, "الخانة غير معروفة.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await TablesReadyAsync(db))
            return (false, "شغّل سكربت مهندسي الجوب أوردر مرة واحدة.");

        var actor = await ActorAsync(db, username);
        var row = await ReadRowAsync(db, partyId);
        var current = seat == DesignSeat ? row.DesignEmployeeId : row.ExecutionEmployeeId;
        if (!CanTransfer(actor, seat, current))
            return (false, "ليس لديك صلاحية تحويل هذه الخانة.");
        if (current == toEmployeeId)
            return (false, "المهندس المختار هو المسؤول الحالي.");

        var other = seat == DesignSeat ? row.ExecutionEmployeeId : row.DesignEmployeeId;
        if (other == toEmployeeId)
            return (false, "مهندس التصميم ومهندس التنفيذ لا يكونان نفس الشخص.");

        var holders = await RoleHoldersAsync(db, seat == ExecutionSeat ? ExecutionRole : DesignRole, seat == ExecutionSeat ? "مهندس تنفيذ" : "مهندس تصميم");
        var target = holders.FirstOrDefault(h => h.EmployeeId == toEmployeeId);
        if (target == null)
            return (false, "المهندس المختار ليس له نفس الدور أو غير مربوط بموظف.");

        var branch = seat == ExecutionSeat
            ? await FactoryBranchIdAsync(db)
            : await LatestDealBranchAsync(db, partyId);
        if (branch.HasValue && target.BranchId != branch.Value)
            return (false, "المهندس المختار ليس من فرع هذه الخانة.");

        await EnsureRowAsync(db, partyId);
        var column = seat == DesignSeat ? "DesignEmployeeId" : "ExecutionEmployeeId";
        var at = seat == DesignSeat ? "DesignAssignedAt" : "ExecutionAssignedAt";
        var by = seat == DesignSeat ? "DesignAssignedBy" : "ExecutionAssignedBy";
        await ExecAsync(db, $@"
UPDATE dbo.JobOrderEngineers
SET {column} = @to, {at} = GETDATE(), {by} = @by
WHERE PartyId = @party",
            ("@to", toEmployeeId), ("@by", username?.Trim() ?? "System"), ("@party", partyId));
        await ExecAsync(db, @"
INSERT INTO dbo.JobOrderEngineerLogs (PartyId, Seat, FromEmployeeId, ToEmployeeId, Reason, ChangedBy)
VALUES (@party, @seat, @from, @to, @reason, @by)",
            ("@party", partyId), ("@seat", seat), ("@from", (object?)current ?? DBNull.Value),
            ("@to", toEmployeeId), ("@reason", note), ("@by", username?.Trim() ?? "System"));

        var names = await NamesAsync(db, seat == DesignSeat ? toEmployeeId : null, seat == ExecutionSeat ? toEmployeeId : null);
        var newName = seat == DesignSeat ? names.Design : names.Execution;
        var oldName = current.HasValue ? (await NamesAsync(db, current, current)).Design : null;
        await NotifyTransferAsync(db, partyId, seat, oldName, newName, target.Username, current, note, username);
        return (true, "تم التحويل.");
    }

    public async Task NotifyAcceptedAsync(int quotationId, int partyId, string? reference, int? empId, string? createdBy, int? warehouseId, string actor)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var partyName = await db.Parties.AsNoTracking()
            .Where(p => p.PartyId == partyId)
            .Select(p => p.PartyName)
            .FirstOrDefaultAsync() ?? "العميل";
        var dealBranch = await DealBranchAsync(db, empId, createdBy, warehouseId);
        var factoryBranch = await FactoryBranchIdAsync(db);
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (dealBranch.HasValue)
        {
            AddBranch(recipients, await RoleHoldersAsync(db, DesignRole, "مهندس تصميم"), dealBranch.Value);
            AddBranch(recipients, await RoleHoldersAsync(db, SystemRoles.SalesManager), dealBranch.Value);
            AddBranch(recipients, await RoleHoldersAsync(db, SystemRoles.GeneralManager), dealBranch.Value);
        }
        if (factoryBranch.HasValue)
        {
            AddBranch(recipients, await RoleHoldersAsync(db, ExecutionRole, "مهندس تنفيذ"), factoryBranch.Value);
            AddBranch(recipients, await RoleHoldersAsync(db, SystemRoles.FactoryManager), factoryBranch.Value);
            AddBranch(recipients, await RoleHoldersAsync(db, SystemRoles.GeneralManager), factoryBranch.Value);
        }

        recipients.RemoveWhere(u => string.Equals(u, actor, StringComparison.OrdinalIgnoreCase));
        if (recipients.Count == 0)
            return;

        var number = string.IsNullOrWhiteSpace(reference) ? $"#{quotationId}" : reference.Trim();
        var title = "مراجعة جوب أوردر بعد موافقة العرض";
        var message = $"تمت الموافقة على عرض السعر {number} للعميل {partyName}. راجع بيانات الجوب أوردر وابدأ ما يلزم.";
        var form = $"customers/{partyId}/job-spec";
        foreach (var username in recipients)
        {
            await _notify.AddAsync(title, message, username, string.IsNullOrWhiteSpace(actor) ? "System" : actor,
                form, "Parties", null);
        }
    }

    private static void AddBranch(HashSet<string> target, IEnumerable<SeatHolder> holders, int branchId)
    {
        foreach (var holder in holders)
        {
            if (holder.BranchId == branchId && !string.IsNullOrWhiteSpace(holder.Username))
                target.Add(holder.Username);
        }
    }

    private static string? ChooseSeat(SeatActor actor, int employeeId, EngineerRow row, int? dealBranch, int? factoryBranch)
    {
        var designOpen = !row.DesignEmployeeId.HasValue && row.ExecutionEmployeeId != employeeId;
        var executionOpen = !row.ExecutionEmployeeId.HasValue && row.DesignEmployeeId != employeeId;
        var canDesign = actor.HasDesign && designOpen && (!dealBranch.HasValue || actor.BranchId == dealBranch);
        var canExecution = actor.HasExecution && executionOpen && (!factoryBranch.HasValue || actor.BranchId == factoryBranch);
        if (canDesign && canExecution)
            return actor.BranchId == factoryBranch ? ExecutionSeat : DesignSeat;
        if (canDesign) return DesignSeat;
        if (canExecution) return ExecutionSeat;
        return null;
    }

    private static bool CanTransfer(SeatActor actor, string seat, int? currentEmployeeId)
    {
        if (actor.IsAdmin) return true;
        if (seat == ExecutionSeat && actor.IsFactoryManager) return true;
        return actor.EmployeeId.HasValue && actor.EmployeeId == currentEmployeeId;
    }

    private async Task<bool> ClaimSeatAsync(db24804Context db, int partyId, string seat, int employeeId, string username)
    {
        await EnsureRowAsync(db, partyId);
        var column = seat == DesignSeat ? "DesignEmployeeId" : "ExecutionEmployeeId";
        var other = seat == DesignSeat ? "ExecutionEmployeeId" : "DesignEmployeeId";
        var at = seat == DesignSeat ? "DesignAssignedAt" : "ExecutionAssignedAt";
        var by = seat == DesignSeat ? "DesignAssignedBy" : "ExecutionAssignedBy";
        var count = await ExecAsync(db, $@"
UPDATE dbo.JobOrderEngineers
SET {column} = @emp, {at} = GETDATE(), {by} = @by
WHERE PartyId = @party AND {column} IS NULL AND ({other} IS NULL OR {other} <> @emp)",
            ("@emp", employeeId), ("@by", username), ("@party", partyId));
        if (count != 1)
            return false;
        await ExecAsync(db, @"
INSERT INTO dbo.JobOrderEngineerLogs (PartyId, Seat, ToEmployeeId, Reason, ChangedBy)
VALUES (@party, @seat, @to, N'استلام عند فتح الإشعار', @by)",
            ("@party", partyId), ("@seat", seat), ("@to", employeeId), ("@by", username));
        return true;
    }

    private async Task NotifyClaimAsync(db24804Context db, int partyId, string seat, string? name, string actor)
    {
        var creator = await db.Quotations.AsNoTracking()
            .Where(q => q.PartyId == partyId && q.Status == QuotationStatuses.Accepted && q.CreatedBy != null)
            .OrderByDescending(q => q.AcceptedAt)
            .Select(q => q.CreatedBy)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(creator) || string.Equals(creator, actor, StringComparison.OrdinalIgnoreCase))
            return;
        var partyName = await db.Parties.AsNoTracking().Where(p => p.PartyId == partyId).Select(p => p.PartyName).FirstOrDefaultAsync() ?? "العميل";
        var label = seat == DesignSeat ? "مهندس التصميم" : "مهندس التنفيذ";
        await _notify.AddAsync("استلام جوب أوردر",
            $"استلم {name ?? "المهندس"} مهمة {label} في جوب أوردر العميل {partyName}.",
            creator, actor, $"customers/{partyId}/job-spec", "Parties", null);
    }

    private async Task NotifyTransferAsync(db24804Context db, int partyId, string seat, string? oldName, string? newName, string? newUsername, int? oldEmployeeId, string reason, string? actor)
    {
        var partyName = await db.Parties.AsNoTracking().Where(p => p.PartyId == partyId).Select(p => p.PartyName).FirstOrDefaultAsync() ?? "العميل";
        var label = seat == DesignSeat ? "مهندس التصميم" : "مهندس التنفيذ";
        var message = $"تم تحويل {label} في جوب أوردر العميل {partyName} من {oldName ?? "غير محدد"} إلى {newName ?? "المهندس"}. السبب: {reason}";
        var form = $"customers/{partyId}/job-spec";
        var by = string.IsNullOrWhiteSpace(actor) ? "System" : actor;
        if (!string.IsNullOrWhiteSpace(newUsername))
            await _notify.AddAsync("تحويل جوب أوردر", message, newUsername, by, form, "Parties", null);
        if (oldEmployeeId is int oldId)
        {
            var oldUser = await db.Users.AsNoTracking()
                .Where(u => u.EmployeeId == oldId && u.IsActive != false)
                .Select(u => u.Username)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(oldUser) && !string.Equals(oldUser, newUsername, StringComparison.OrdinalIgnoreCase))
                await _notify.AddAsync("تحويل جوب أوردر", message, oldUser, by, form, "Parties", null);
        }
    }

    private async Task<int?> LatestDealBranchAsync(db24804Context db, int partyId)
    {
        var quote = await db.Quotations.AsNoTracking()
            .Where(q => q.PartyId == partyId && q.Status == QuotationStatuses.Accepted)
            .OrderByDescending(q => q.QuotationId)
            .Select(q => new { q.EmpId, q.CreatedBy, q.WarehouseId })
            .FirstOrDefaultAsync()
            ?? await db.Quotations.AsNoTracking()
                .Where(q => q.PartyId == partyId)
                .OrderByDescending(q => q.QuotationId)
                .Select(q => new { q.EmpId, q.CreatedBy, q.WarehouseId })
                .FirstOrDefaultAsync();
        if (quote == null)
            return null;
        return await DealBranchAsync(db, quote.EmpId, quote.CreatedBy, quote.WarehouseId);
    }

    private async Task<int?> DealBranchAsync(db24804Context db, int? empId, string? createdBy, int? warehouseId)
    {
        if (empId is int employeeId)
        {
            var branch = await db.Employees.AsNoTracking().Where(e => e.EmployeeId == employeeId).Select(e => e.BranchId).FirstOrDefaultAsync();
            if (branch.HasValue) return branch;
        }
        if (!string.IsNullOrWhiteSpace(createdBy))
        {
            var user = await db.Users.AsNoTracking()
                .Where(u => u.Username == createdBy)
                .Select(u => new { u.EmployeeId, u.DefaultBranchId })
                .FirstOrDefaultAsync();
            if (user?.EmployeeId is int linked)
            {
                var branch = await db.Employees.AsNoTracking().Where(e => e.EmployeeId == linked).Select(e => e.BranchId).FirstOrDefaultAsync();
                if (branch.HasValue) return branch;
            }
            if (user?.DefaultBranchId is int preferred)
                return preferred;
        }
        if (warehouseId is int warehouse)
            return await db.Warehouses.AsNoTracking().Where(w => w.WarehouseId == warehouse).Select(w => w.BranchId).FirstOrDefaultAsync();
        return null;
    }

    private async Task<int?> FactoryBranchIdAsync(db24804Context db)
    {
        var branches = await db.Branches.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => new { b.BranchId, b.BranchCode, b.BranchNameAr })
            .ToListAsync();
        var byCode = branches.FirstOrDefault(b => string.Equals((b.BranchCode ?? "").Trim(), "3", StringComparison.OrdinalIgnoreCase));
        if (byCode != null) return byCode.BranchId;
        var byId = branches.FirstOrDefault(b => b.BranchId == 3);
        if (byId != null) return byId.BranchId;
        return branches.FirstOrDefault(b => (b.BranchNameAr ?? "").Contains("مصنع"))?.BranchId;
    }

    private async Task<List<SeatHolder>> RoleHoldersAsync(db24804Context db, string role, params string[] aliases)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { role.Replace(" ", "") };
        foreach (var alias in aliases)
            wanted.Add(alias.Replace(" ", ""));
        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive != false && u.Role != null && u.Username != null)
            .Select(u => new { u.Username, u.Role, u.EmployeeId, u.DefaultBranchId })
            .ToListAsync();
        var matched = users.Where(u => wanted.Contains((u.Role ?? "").Replace(" ", "")));
        return await WithBranchesAsync(db, matched.Select(u => (u.Username!, u.EmployeeId, u.DefaultBranchId)));
    }

    private async Task<List<SeatHolder>> WithBranchesAsync(db24804Context db, IEnumerable<(string Username, int? EmployeeId, int? DefaultBranchId)> users)
    {
        var list = users.ToList();
        var employeeIds = list.Where(u => u.EmployeeId.HasValue).Select(u => u.EmployeeId!.Value).Distinct().ToList();
        var branches = employeeIds.Count == 0
            ? new Dictionary<int, int?>()
            : await db.Employees.AsNoTracking()
                .Where(e => employeeIds.Contains(e.EmployeeId) && (e.EndDate == null || e.EndDate > DateTime.Today))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.BranchId);
        return list.Select(u =>
        {
            int? branch = u.DefaultBranchId;
            if (u.EmployeeId is int id && branches.TryGetValue(id, out var employeeBranch) && employeeBranch.HasValue)
                branch = employeeBranch;
            else if (u.EmployeeId is int ended && !branches.ContainsKey(ended))
                return new SeatHolder(u.Username, null, branch);
            return new SeatHolder(u.Username, u.EmployeeId, branch);
        }).ToList();
    }

    private async Task<SeatActor> ActorAsync(db24804Context db, string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return new SeatActor();
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Username == username && u.IsActive != false)
            .Select(u => new { u.UserId, u.EmployeeId, u.DefaultBranchId, u.Role })
            .FirstOrDefaultAsync();
        if (user == null)
            return new SeatActor();
        int? branch = user.DefaultBranchId;
        if (user.EmployeeId is int employeeId)
        {
            var employeeBranch = await db.Employees.AsNoTracking()
                .Where(e => e.EmployeeId == employeeId && (e.EndDate == null || e.EndDate > DateTime.Today))
                .Select(e => e.BranchId)
                .FirstOrDefaultAsync();
            if (employeeBranch.HasValue)
                branch = employeeBranch;
        }
        var role = (user.Role ?? "").Replace(" ", "");
        return new SeatActor
        {
            EmployeeId = user.EmployeeId,
            BranchId = branch,
            HasDesign = role.Equals(DesignRole, StringComparison.OrdinalIgnoreCase) || role.Equals("مهندستصميم", StringComparison.Ordinal),
            HasExecution = role.Equals(ExecutionRole, StringComparison.OrdinalIgnoreCase) || role.Equals("مهندستنفيد", StringComparison.Ordinal),
            IsAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase),
            IsFactoryManager = role.Equals(SystemRoles.FactoryManager, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static async Task<Dictionary<int, string>> EmployeeNamesAsync(db24804Context db, IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0)
            return new();
        return await db.Employees.AsNoTracking()
            .Where(e => list.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);
    }

    private static async Task<(string? Design, string? Execution)> NamesAsync(db24804Context db, int? designId, int? executionId)
    {
        var names = await EmployeeNamesAsync(db, new[] { designId, executionId }.Where(id => id.HasValue).Select(id => id!.Value));
        names.TryGetValue(designId ?? 0, out var design);
        names.TryGetValue(executionId ?? 0, out var execution);
        return (design, execution);
    }

    private async Task<EngineerRow> ReadRowAsync(db24804Context db, int partyId)
    {
        return await WithConnectionAsync(db, async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT DesignEmployeeId, ExecutionEmployeeId
FROM dbo.JobOrderEngineers
WHERE PartyId = @party";
            Add(cmd, "@party", partyId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return new EngineerRow();
            return new EngineerRow
            {
                DesignEmployeeId = reader.IsDBNull(0) ? null : reader.GetInt32(0),
                ExecutionEmployeeId = reader.IsDBNull(1) ? null : reader.GetInt32(1)
            };
        });
    }

    private async Task EnsureRowAsync(db24804Context db, int partyId)
    {
        await ExecAsync(db, @"
IF NOT EXISTS (SELECT 1 FROM dbo.JobOrderEngineers WHERE PartyId = @party)
    INSERT INTO dbo.JobOrderEngineers (PartyId) VALUES (@party)",
            ("@party", partyId));
    }

    private static async Task<bool> TablesReadyAsync(db24804Context db)
    {
        return await WithConnectionAsync(db, async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo.JobOrderEngineers', N'U') IS NULL THEN 0 ELSE 1 END";
            var value = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(value) == 1;
        });
    }

    private static async Task<int> ExecAsync(db24804Context db, string sql, params (string Name, object Value)[] parameters)
    {
        return await WithConnectionAsync(db, async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var parameter in parameters)
                Add(cmd, parameter.Name, parameter.Value);
            return await cmd.ExecuteNonQueryAsync();
        });
    }

    private static void Add(DbCommand cmd, string name, object value)
    {
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(parameter);
    }

    private static async Task<T> WithConnectionAsync<T>(db24804Context db, Func<DbConnection, Task<T>> action)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
            await connection.OpenAsync();
        try
        {
            return await action(connection);
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    private sealed class EngineerRow
    {
        public int? DesignEmployeeId { get; init; }
        public int? ExecutionEmployeeId { get; init; }
    }

    private sealed class SeatActor
    {
        public int? EmployeeId { get; init; }
        public int? BranchId { get; init; }
        public bool HasDesign { get; init; }
        public bool HasExecution { get; init; }
        public bool IsAdmin { get; init; }
        public bool IsFactoryManager { get; init; }
    }

    private sealed record SeatHolder(string Username, int? EmployeeId, int? BranchId);
}

public sealed class JobEngineerView
{
    public bool Ready { get; init; }
    public string? DesignName { get; init; }
    public string? ExecutionName { get; init; }
    public int? DesignEmployeeId { get; init; }
    public int? ExecutionEmployeeId { get; init; }
    public bool CanTransferDesign { get; init; }
    public bool CanTransferExecution { get; init; }
}

public sealed class JobEngineerClaim
{
    public bool Ready { get; init; }
    public string? ClaimedSeat { get; init; }
    public string? Note { get; init; }
}

public sealed class JobEngineerCandidate
{
    public int EmployeeId { get; init; }
    public string Name { get; set; } = "";
}
