using System.IO;
using System.Threading.Tasks;
using Deleuze.Shared.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeleuzeDrive.Services;

public interface ITenantMigrationService
{
    Task MigrateTenantSchemaAsync(string schemaName);
}

public class TenantMigrationService :
    ITenantMigrationService
{
    private readonly ITenantSchemaMigrator _migrator;
    private readonly IHostEnvironment _env;
    private readonly ILogger<TenantMigrationService> _logger;

    public TenantMigrationService(
        ITenantSchemaMigrator migrator,
        IHostEnvironment env,
        ILogger<TenantMigrationService> logger)
    {
        _migrator = migrator;
        _env = env;
        _logger = logger;
    }

    public async Task MigrateTenantSchemaAsync(
        string schemaName)
    {
        var migrationDirectory =
            Path.Combine(
                _env.ContentRootPath,
                "DbMigration");

        _logger.LogInformation(
            "Starting migration process for tenant schema: {SchemaName}",
            schemaName);

        await _migrator.MigrateAsync(
            schemaName,
            migrationDirectory);

        _logger.LogInformation(
            "Completed migration process for tenant schema: {SchemaName}",
            schemaName);
    }
}