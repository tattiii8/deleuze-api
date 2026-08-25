using System;
using System.Threading.Tasks;
using Deleuze.Shared.Infrastructure;

namespace DeleuzeAuth.Services.Tenant;

public interface ITenantDeprovisioningService
{
    Task DeprovisionAsync(string tenantId);
}

public class TenantDeprovisioningService
    : ITenantDeprovisioningService
{
    private readonly ITenantSchemaDeprovisioner _deprovisioner;

    public TenantDeprovisioningService(
        ITenantSchemaDeprovisioner deprovisioner)
    {
        _deprovisioner = deprovisioner;
    }

    public async Task DeprovisionAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "Tenant ID is required.",
                nameof(tenantId));
        }

        await _deprovisioner.DeprovisionAsync(
            tenantId);
    }
}