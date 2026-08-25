using System.IO;
using System.Threading.Tasks;
using Deleuze.Shared.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeleuzeDrive.Services;

public interface ITenantProvisioningService
{
    Task ProvisionTenantSchemaAsync(string tenantId);
}

public class TenantProvisioningService :
    ITenantProvisioningService
{
    private readonly ITenantSchemaProvisioner _provisioner;
    private readonly IHostEnvironment _env;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        ITenantSchemaProvisioner provisioner,
        IHostEnvironment env,
        ILogger<TenantProvisioningService> logger)
    {
        _provisioner = provisioner;
        _env = env;
        _logger = logger;
    }

    public async Task ProvisionTenantSchemaAsync(
        string tenantId)
    {
        var migrationDirectory =
            Path.Combine(
                _env.ContentRootPath,
                "DbMigration");

        _logger.LogInformation(
            "Starting provisioning process for tenant: {TenantId}",
            tenantId);

        await _provisioner.ProvisionAsync(
            tenantId,
            migrationDirectory);

        _logger.LogInformation(
            "Completed provisioning process for tenant: {TenantId}",
            tenantId);
    }
}