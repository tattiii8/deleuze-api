using System.Threading.Tasks;

namespace Deleuze.Shared.Infrastructure;

public interface ITenantSchemaProvisioner
{
    Task ProvisionAsync(
        string tenantId,
        string migrationDirectory);
}