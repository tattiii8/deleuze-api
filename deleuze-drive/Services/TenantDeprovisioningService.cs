using System.Threading.Tasks;
using Deleuze.Shared.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DeleuzeDrive.Services;

public interface ITenantDeprovisioningService
{
    Task DeprovisionTenantAsync(string tenantId);
}

public class TenantDeprovisioningService :
    ITenantDeprovisioningService
{
    private readonly ITenantSchemaDeprovisioner _schemaDeprovisioner;
    private readonly IStorageService _storageService;
    private readonly ILogger<TenantDeprovisioningService> _logger;

    public TenantDeprovisioningService(
        ITenantSchemaDeprovisioner schemaDeprovisioner,
        IStorageService storageService,
        ILogger<TenantDeprovisioningService> logger)
    {
        _schemaDeprovisioner = schemaDeprovisioner;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task DeprovisionTenantAsync(
        string tenantId)
    {
        _logger.LogInformation(
            "Starting deprovisioning for tenant: {TenantId}",
            tenantId);

        // S3のテナントデータを削除
        await _storageService.DeletePrefixAsync(
            $"{tenantId}/");

        // DB Schemaを削除
        await _schemaDeprovisioner.DeprovisionAsync(
            tenantId);

        _logger.LogInformation(
            "Completed deprovisioning for tenant: {TenantId}",
            tenantId);
    }
}