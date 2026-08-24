namespace Deleuze.Shared.Infrastructure;

public interface ITenantSchemaMigrator
{
    Task MigrateAsync(
        string schemaName,
        string migrationDirectory);
}