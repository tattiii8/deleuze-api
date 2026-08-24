namespace Deleuze.Shared.MultiTenancy;

public interface ITenantProvider
{
    string? GetTenantId();
}