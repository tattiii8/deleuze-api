using System;
using System.IO;
using System.Threading.Tasks;
using Deleuze.Shared.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace DeleuzeDrive.Services;

public interface ITenantProvisioningService
{
    Task ProvisionTenantSchemaAsync(string schemaName);
}

public class TenantProvisioningService : ITenantProvisioningService
{
    private readonly ITenantSchemaProvisioner _provisioner;
    private readonly IHostEnvironment _env;

    public TenantProvisioningService(
        ITenantSchemaProvisioner provisioner,
        IHostEnvironment env)
    {
        _provisioner = provisioner;
        _env = env;
    }

    public async Task ProvisionTenantSchemaAsync(string schemaName)
    {
        var migrationDirectory =
            Path.Combine(
                _env.ContentRootPath,
                "DbMigration");

        await _provisioner.ProvisionAsync(
            schemaName,
            migrationDirectory);
    }
}