namespace Deleuze.Shared.Infrastructure
{
    public interface IServiceProvisioningClient
    {
        string ServiceKey { get; }

        Task ProvisionTenantAsync(string tenantId);
        Task DeprovisionTenantAsync(string tenantId);
        Task MigrateTenantAsync(string tenantId);
    }
}