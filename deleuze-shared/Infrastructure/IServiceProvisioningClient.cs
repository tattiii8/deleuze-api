namespace Deleuze.Shared.Infrastructure
{
    public interface IServiceProvisioningClient
    {
        Task ProvisionTenantAsync(string tenantId);
        Task DeprovisionTenantAsync(string tenantId);
        Task MigrateTenantAsync(string tenantId); // 追加: マイグレーション用
    }
}