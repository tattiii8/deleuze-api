using System.Threading.Tasks;
using Deleuze.Shared.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DeleuzeDrive.Services;

public interface ITenantDeprovisioningService
{
    Task DeprovisionTenantSchemaAsync(string tenantId);
}

public class TenantDeprovisioningService :
    ITenantDeprovisioningService
{
    private readonly ITenantSchemaDeprovisioner _deprovisioner;
    private readonly ILogger<TenantDeprovisioningService> _logger;

    public TenantDeprovisioningService(
        ITenantSchemaDeprovisioner deprovisioner,
        ILogger<TenantDeprovisioningService> logger)
    {
        _deprovisioner = deprovisioner;
        _logger = logger;
    }

    public async Task DeprovisionTenantSchemaAsync(
        string tenantId)
    {
        _logger.LogInformation(
            "Starting deprovisioning for tenant: {TenantId}",
            tenantId);

        await _deprovisioner.DeprovisionAsync(
            tenantId);

        _logger.LogInformation(
            "Completed deprovisioning for tenant: {TenantId}",
            tenantId);
    }
}