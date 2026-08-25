using System;
using System.IO;
using System.Threading.Tasks;
using Deleuze.Shared.Infrastructure;

namespace DeleuzeAuth.Services.Tenant;

public interface ITenantMigrationService
{
    Task MigrateAsync(string tenantId);
}

public class TenantMigrationService
    : ITenantMigrationService
{
    private readonly ITenantSchemaMigrator _migrator;

    public TenantMigrationService(
        ITenantSchemaMigrator migrator)
    {
        _migrator = migrator;
    }

    public async Task MigrateAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "Tenant ID is required.",
                nameof(tenantId));
        }

        await _migrator.MigrateAsync(
            tenantId,
            GetMigrationDirectory());
    }

    private static string GetMigrationDirectory()
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "DbMigration",
            "Tenant");
    }
}