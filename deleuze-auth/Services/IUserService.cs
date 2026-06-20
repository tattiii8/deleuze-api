namespace DeleuzeAuth.Services;

public interface IUserService
{
    Task<string?> AuthenticateAndGetTenantAsync(string loginId, string password);
}