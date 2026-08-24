using System.Threading.Tasks;

namespace Deleuze.Shared.Infrastructure;

public interface ITenantSchemaMigrator
{
    Task MigrateAsync(
        string tenantId,
        string migrationDirectory);
}