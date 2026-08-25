namespace Deleuze.Shared.Infrastructure;

public interface ITenantSchemaDeprovisioner
{
    Task DeprovisionAsync(string tenantId);
}