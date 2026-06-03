using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IInteractionService
{
    Task<PagedResult<InteractionListDto>> GetInteractionsAsync(InteractionFilterDto filter);
    Task<List<InteractionListDto>> GetByOpportunityAsync(int opportunityId);
    Task<(bool Success, string Message)> AddQuickAsync(QuickInteractionDto dto, string userName);
}