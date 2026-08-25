namespace DeleuzeAuth.Services.Authentication;

public interface IUserService
{
    Task<bool> AuthenticateAsync(
        string tenantId,
        string loginId,
        string password);
}