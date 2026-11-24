using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface ISpecService
{
    Task<bool> IsReadyAsync();
    Task<List<SpecListDto>> GetListsAsync();
    Task<(bool Ok, string Message, int? ListId)> AddListAsync(string nameAr);
    Task<(bool Ok, string Message, int? OptionId)> AddOptionAsync(int listId, string nameAr);
    Task<(bool Ok, string Message)> RenameOptionAsync(int optionId, string nameAr);
    Task<(bool Ok, string Message)> RemoveOptionAsync(int optionId);
    Task<(bool Ok, string Message)> RenameListAsync(int listId, string nameAr);
    Task<(bool Ok, string Message)> RemoveListAsync(int listId);
    Task<List<string>> GetOptionNamesAsync(int listId);
    Task<List<SpecOptionDto>> GetOptionsAsync(int listId);
    Task<List<string>> GetTextSuggestionsAsync(int questionId);
    Task<int> GetProductGroupIdAsync(int productId);
    Task<List<SpecQuestionDto>> GetStandardQuestionsAsync();
    Task<List<SpecQuestionDto>> GetQuestionsAsync(int productGroupId);
    Task<(bool Ok, string Message)> AddQuestionAsync(SpecQuestionDto question);
    Task<List<SpecAnswerDto>> GetAnswersAsync(string ownerType, int ownerId);
    Task<(bool Ok, string Message)> SaveAnswersAsync(string ownerType, int ownerId, IReadOnlyList<SpecAnswerDto> answers);
    Task ClearAnswersAsync(string ownerType, int ownerId);
    Task RemoveAnswersAsync(string ownerType, int ownerId, IReadOnlyList<int> questionIds);
    Task<List<SpecVariableImageDto>> GetVariableImagesAsync(string ownerType, int ownerId);
    Task SaveVariableImagesAsync(string ownerType, int ownerId, IReadOnlyList<SpecVariableImageDto> images);
}
