using System;
using System.Threading.Tasks;

namespace DeleuzeAuth.Services.Authentication;

public class AuthenticationResult
{
    public bool IsSuccess { get; set; }
    public Guid LoginId { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public interface IUserService
{
    Task<AuthenticationResult> AuthenticateAsync(
        string tenantId,
        Guid loginId,
        string password);
}