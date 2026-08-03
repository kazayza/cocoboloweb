using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IB2BAuthService
{
    Task<B2BLoginResultDto?> ValidateLoginAsync(string username, string password);
}
