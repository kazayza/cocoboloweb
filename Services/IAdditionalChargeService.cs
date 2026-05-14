using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IAdditionalChargeService
{
    Task<PagedResult<AdditionalChargeListDto>> GetChargesAsync(AdditionalChargeFilterDto filter);
    Task<AdditionalChargeStatsDto> GetStatsAsync();
    Task<AdditionalChargeFormDto?> GetChargeForEditAsync(int chargeId);
    Task<(bool Success, string Message)> CreateChargeAsync(AdditionalChargeFormDto dto, string currentUserName);
    Task<(bool Success, string Message)> UpdateChargeAsync(int chargeId, AdditionalChargeFormDto dto, string currentUserName);
    Task<(bool Success, string Message)> DeleteChargeAsync(int chargeId, string currentUserName);
    Task<(bool Success, string Message)> ApplyToInvoiceAsync(int chargeId, int transactionId, string currentUserName);
    Task<(bool Success, string Message)> MarkAsNonRefundableAsync(int chargeId, string reason, string currentUserName);
}