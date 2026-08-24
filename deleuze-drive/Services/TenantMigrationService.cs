using System;
using System.IO;
using System.Threading.Tasks;
using Deleuze.Shared.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace DeleuzeDrive.Services;

public interface ITenantMigrationService
{
    Task MigrateTenantSchemaAsync(string schemaName);
}

public class TenantMigrationService : ITenantMigrationService
{
    private readonly ITenantSchemaMigrator _migrator;
    private readonly IHostEnvironment _env;

    public TenantMigrationService(
        ITenantSchemaMigrator migrator,
        IHostEnvironment env)
    {
        _migrator = migrator;
        _env = env;
    }

    public async Task MigrateTenantSchemaAsync(string schemaName)
    {
        var migrationDirectory =
            Path.Combine(
                _env.ContentRootPath,
                "DbMigration");

        await _migrator.MigrateAsync(
            schemaName,
            migrationDirectory);
    }
}