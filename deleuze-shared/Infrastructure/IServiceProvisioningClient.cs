namespace Deleuze.Shared.Infrastructure;

public interface IServiceProvisioningClient
{
    string ServiceName { get; }
    Task<bool> ProvisionTenantAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<bool> DeprovisionTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}