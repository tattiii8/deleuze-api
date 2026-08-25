using System.IO;
using System.Threading.Tasks;
using Deleuze.Shared.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeleuzeDrive.Services.Tenant;

public interface ITenantMigrationService
{
    Task MigrateTenantSchemaAsync(string tenantId);
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
        string tenantId)
    {
        var migrationDirectory =
            Path.Combine(
                _env.ContentRootPath,
                "DbMigration");

        _logger.LogInformation(
            "Starting migration process for tenant: {TenantId}",
            tenantId);

        await _migrator.MigrateAsync(
            tenantId,
            migrationDirectory);

        _logger.LogInformation(
            "Completed migration process for tenant: {TenantId}",
            tenantId);
    }
}