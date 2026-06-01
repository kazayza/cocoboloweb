using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IComplaintService
{
    // ─── List & Detail ──────────────────────────────────────
    Task<PagedComplaintsDto>   GetComplaintsAsync(ComplaintFilterDto filter, string? currentUserName = null);
    Task<ComplaintDetailDto?>  GetByIdAsync(int complaintId);

    // ─── Create / Update / Delete ──────────────────────────
    Task<(bool Success, string Message, int? ComplaintId)>
        CreateAsync(ComplaintFormDto dto, string currentUserName);

    Task<(bool Success, string Message)>
        UpdateAsync(ComplaintFormDto dto, string currentUserName);

    Task<(bool Success, string Message)>
        DeleteAsync(int complaintId);

    // ─── Workflow ──────────────────────────────────────────
    Task<(bool Success, string Message)>
        ChangeStatusAsync(ChangeStatusDto dto, string currentUserName);

    Task<(bool Success, string Message)>
        AssignAsync(AssignComplaintDto dto, string currentUserName);

    Task<(bool Success, string Message)>
        EscalateAsync(EscalateComplaintDto dto, int currentUserId, string currentUserName);

    Task<(bool Success, string Message)>
        RateAsync(RateComplaintDto dto);

    // ─── FollowUps ─────────────────────────────────────────
    Task<(bool Success, string Message, int? FollowUpId)>
        AddFollowUpAsync(FollowUpFormDto dto, int currentEmployeeId);

    Task<(bool Success, string Message)>
        DeleteFollowUpAsync(int followUpId);

    // ─── Attachments ───────────────────────────────────────
    Task<(bool Success, string Message, int? AttachmentId)>
        UploadAttachmentAsync(int complaintId, string originalFileName,
            byte[] content, string mimeType, int uploadedByUserId);

    Task<(bool Success, string Message)>
        DeleteAttachmentAsync(int attachmentId);

    Task<(byte[] Content, string MimeType, string FileName)?>
        GetAttachmentAsync(int attachmentId);

    // ─── Dashboard ─────────────────────────────────────────
    Task<ComplaintsDashboardDto> GetDashboardAsync(DateTime? from = null, DateTime? to = null);

    // ─── Lookups ───────────────────────────────────────────
    Task<List<ComplaintTypeDto>>          GetTypesAsync(bool activeOnly = true);
    Task<List<PartyLookupItemDto>>        SearchPartiesAsync(string? search, int max = 20);
    Task<List<EmployeeLookupItemDto>>     GetEmployeesAsync();
    Task<List<TransactionLookupItemDto>>  GetCustomerTransactionsAsync(int partyId, int max = 30);
     Task<List<ProductLookupItemDto>> GetTransactionProductsAsync(int transactionId);
     Task<List<ProductLookupItemDto>> GetCustomerProductsAsync(int partyId, int max = 100);

    // ─── Types Management ──────────────────────────────────
    Task<(bool Success, string Message, int? TypeId)>
        SaveTypeAsync(ComplaintTypeDto dto);

    Task<(bool Success, string Message)>
        DeleteTypeAsync(int typeId);

    // ─── Excel Export ──────────────────────────────────────
    Task<byte[]> ExportToExcelAsync(ComplaintFilterDto filter);
}
