using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DeleuzeMng.Models;
using DeleuzeMng.Services.Infrastructure;

namespace DeleuzeMng.Services;

public class TenantManagementService : ITenantManagementService
{
    private readonly ITenantRepository _repository;
    private readonly IServiceProvisioningCoordinator _coordinator;

    public TenantManagementService(
        ITenantRepository repository,
        IServiceProvisioningCoordinator coordinator)
    {
        _repository = repository;
        _coordinator = coordinator;
    }

    public async Task<IEnumerable<TenantInfo>> GetTenantsAsync()
    {
        var authTenants = await _repository.GetAllAuthTenantsAsync();
        var schemas = await _repository.GetSchemasAsync("app_%");
        return authTenants.Select(dto => new TenantInfo(
            dto.Id,
            _coordinator.RegisteredServiceKeys.Where(k => IsEnabled(schemas, dto.Id, k)).ToList(),
            dto.AuthMode,
            dto.ApiKey
        ));
    }

    public async Task<TenantInfo?> GetTenantByIdAsync(string tenantId)
    {
        var dto = await _repository.GetAuthTenantByIdAsync(tenantId);
        if (dto == null) return null;

        var schemas = await _repository.GetSchemasAsync($"app_{tenantId}%");
        var activeServices = _coordinator.RegisteredServiceKeys
            .Where(k => IsEnabled(schemas, tenantId, k))
            .ToList();

        return new TenantInfo(dto.Id, activeServices, dto.AuthMode, dto.ApiKey);
    }

    public async Task<bool> CreateTenantAsync(string tenantId, string name = "")
    {
        await _repository.CreateTenantSchemaAndRecordAsync(tenantId, name);
        return true;
    }

    public async Task<bool> DeleteTenantAsync(string tenantId)
    {
        await _coordinator.DeprovisionAllAsync(tenantId);
        await _repository.DropTenantSchemaAndRecordAsync(tenantId);
        return true;
    }

    public async Task<bool> EnableServiceForTenantAsync(string tenantId, string serviceKey)
    {
        await _coordinator.ProvisionAsync(tenantId, serviceKey);
        return true;
    }

    public async Task<bool> DisableServiceForTenantAsync(string tenantId, string serviceKey)
    {
        await _coordinator.DeprovisionAsync(tenantId, serviceKey);
        return true;
    }

    public async Task<bool> MigrateAllServicesForTenantAsync(string tenantId)
    {
        return await _coordinator.MigrateAllAsync(tenantId);
    }

    public async Task<IEnumerable<MigrationHistoryDto>> GetTenantMigrationsAsync(string tenantId)
    {
        return await Task.FromResult(Enumerable.Empty<MigrationHistoryDto>());
    }

    public async Task<HealthCheckResultDto> CheckTenantHealthAsync(string tenantId)
    {
        return await Task.FromResult(new HealthCheckResultDto("Healthy", "Healthy", "All services operating normally."));
    }

    public async Task<bool> UpdateTenantStatusAsync(string tenantId, string status)
    {
        await _repository.UpdateStatusAsync(tenantId, status);
        return true;
    }

    public async Task<string> GenerateApiKeyAsync(string tenantId)
    {
        var apiKey = $"key_{Guid.NewGuid():N}";
        await _repository.SaveTenantApiKeyAsync(tenantId, apiKey);
        return apiKey;
    }

    public async Task<bool> UpdateAuthModeAsync(string tenantId, int authMode)
    {
        await _repository.UpdateAuthModeAsync(tenantId, authMode);
        return true;
    }

    public async Task<IEnumerable<UserInfo>> GetUsersAsync()
    {
        return await Task.FromResult(Enumerable.Empty<UserInfo>());
    }

    public async Task<bool> RegisterUserAsync(string loginId, string password, string tenantId)
    {
        return await Task.FromResult(true);
    }

    public async Task<bool> DeleteUserAsync(string userId)
    {
        return await Task.FromResult(true);
    }

    private static bool IsEnabled(HashSet<string> schemas, string tenantId, string key)
        => key.Equals("drive", StringComparison.OrdinalIgnoreCase)
             ? schemas.Contains($"app_{tenantId}")
             : schemas.Contains($"app_{tenantId}_{key}");
}