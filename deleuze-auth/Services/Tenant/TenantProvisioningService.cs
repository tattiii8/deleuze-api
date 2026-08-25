using System;
using System.IO;
using System.Threading.Tasks;
using Deleuze.Shared.Infrastructure;

namespace DeleuzeAuth.Services.Tenant;

public interface ITenantProvisioningService
{
    Task ProvisionAsync(string tenantId);
}

public class TenantProvisioningService
    : ITenantProvisioningService
{
    private readonly ITenantSchemaProvisioner _provisioner;

    public TenantProvisioningService(
        ITenantSchemaProvisioner provisioner)
    {
        _provisioner = provisioner;
    }

    public async Task ProvisionAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "Tenant ID is required.",
                nameof(tenantId));
        }

        await _provisioner.ProvisionAsync(
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