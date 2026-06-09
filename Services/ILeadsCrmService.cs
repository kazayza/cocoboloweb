using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services;

public interface ILeadsCrmService
{
    // عرض كل الـ Leads مع فلترة وصفحات
    Task<PagedResult<LeadsCrmListDto>> GetLeadsAsync(LeadsCrmFilterDto filter);

    // تفاصيل Lead واحد
    Task<LeadsCrmDetailDto?> GetLeadByIdAsync(int leadId);

    // تحديث حالة Lead أو فيدباك
    Task<(bool Success, string Message)> UpdateLeadAsync(LeadsCrmUpdateDto dto, string userName);

    // تحويل Lead لعميل (Party + Opportunity + Task)
    Task<(bool Success, string Message, int PartyId, int OpportunityId)> ConvertLeadToClientAsync(
        LeadConvertDto dto, string userName);

    // إحصائيات
    Task<LeadsCrmStatsDto> GetStatsAsync();
    Task<List<Employee>> GetEmployeesAsync();

    // حذف Lead
    Task<(bool Success, string Message)> DeleteLeadAsync(int leadId, string userName);
}