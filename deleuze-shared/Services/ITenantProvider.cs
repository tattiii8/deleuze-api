namespace Deleuze.Shared.Services;

public interface ITenantProvider
{
    string? GetTenantId();
}