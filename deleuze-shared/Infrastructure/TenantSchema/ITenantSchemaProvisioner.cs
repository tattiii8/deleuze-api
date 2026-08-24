namespace Deleuze.Shared.Infrastructure;

public interface ITenantSchemaProvisioner
{
    Task ProvisionAsync(
        string schemaName,
        string migrationDirectory);
}