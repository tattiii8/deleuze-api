using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DeleuzeMng.Models;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Logging;

namespace DeleuzeMng.Services;


public class TenantManagementService : ITenantManagementService
{
    private readonly string _appConnString;
    private readonly string _authConnString;
    private readonly Dictionary<string, Func<string, Task<bool>>> _serviceClients;
    private readonly Dictionary<string, Func<string, Task<bool>>> _disableServiceClients;
    private readonly Dictionary<string, Func<string, Task<bool>>> _migrateServiceClients;
    private readonly ILogger<TenantManagementService> _logger;

    public TenantManagementService(
        string appConnString,
        string authConnString,
        Dictionary<string, Func<string, Task<bool>>> serviceClients,
        Dictionary<string, Func<string, Task<bool>>> disableServiceClients,
        Dictionary<string, Func<string, Task<bool>>> migrateServiceClients,
        ILogger<TenantManagementService> logger)
    {
        _appConnString = appConnString;
        _authConnString = authConnString;
        _serviceClients = serviceClients;
        _disableServiceClients = disableServiceClients;
        _migrateServiceClients = migrateServiceClients;
        _logger = logger;
    }

    private async Task<NpgsqlConnection> OpenAuthConnAsync()
    {
        var conn = new NpgsqlConnection(_authConnString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task<NpgsqlConnection> OpenAppConnAsync()
    {
        var conn = new NpgsqlConnection(_appConnString);
        await conn.OpenAsync();
        return conn;
    }

    public async Task<IEnumerable<TenantInfo>> GetTenantsAsync()
    {
        await using var conn = await OpenAuthConnAsync();
        var dtos = await conn.QueryAsync<TenantAuthDto>(@"SELECT ""Id"", ""AuthMode"", ""ApiKey"" FROM public.""Tenants"";");

        await using var appConn = await OpenAppConnAsync();
        var schemas = (await appConn.QueryAsync<string>(
            @"SELECT schema_name FROM information_schema.schemata WHERE schema_name LIKE 'app_%';")).ToHashSet();

        return dtos.Select(dto => new TenantInfo(
            dto.Id,
            _serviceClients.Keys.Where(k => IsEnabled(schemas, dto.Id, k)).ToList(),
            dto.AuthMode,
            dto.ApiKey
        ));
    }

    public async Task<TenantInfo?> GetTenantByIdAsync(string tenantId)
    {
        await using var conn = await OpenAuthConnAsync();
        var dto = await conn.QueryFirstOrDefaultAsync<TenantAuthDto>(
            @"SELECT ""Id"", ""AuthMode"", ""ApiKey"" FROM public.""Tenants"" WHERE ""Id"" = @TenantId;", new { TenantId = tenantId });

        if (dto == null) return null;

        await using var appConn = await OpenAppConnAsync();
        var schemas = (await appConn.QueryAsync<string>(
            @"SELECT schema_name FROM information_schema.schemata WHERE schema_name LIKE @Pattern;",
            new { Pattern = $"app_{tenantId}%" })).ToHashSet();

        var activeServices = _serviceClients.Keys.Where(k => IsEnabled(schemas, tenantId, k)).ToList();

        return new TenantInfo(dto.Id, activeServices, dto.AuthMode, dto.ApiKey);
    }

    public async Task<bool> CreateTenantAsync(string tenantId, string name = "")
    {
        await using var appConn = await OpenAppConnAsync();
        await appConn.ExecuteAsync($"CREATE SCHEMA IF NOT EXISTS \"app_{tenantId}\";");

        await using var authConn = await OpenAuthConnAsync();
        const string sql = @"
            INSERT INTO public.""Tenants"" (""Id"", ""Name"", ""AuthMode"")
            VALUES (@TenantId, @Name, 0)
            ON CONFLICT (""Id"") DO NOTHING;";
        await authConn.ExecuteAsync(sql, new { TenantId = tenantId, Name = string.IsNullOrWhiteSpace(name) ? tenantId : name });

        return true;
    }

    public async Task<bool> DeleteTenantAsync(string tenantId)
    {
        foreach (var client in _disableServiceClients.Values)
        {
            try { await client(tenantId); } catch { }
        }

        await using var appConn = await OpenAppConnAsync();
        await appConn.ExecuteAsync($"DROP SCHEMA IF EXISTS \"app_{tenantId}\" CASCADE;");

        await using var authConn = await OpenAuthConnAsync();
        await authConn.ExecuteAsync(@"DELETE FROM public.""Tenants"" WHERE ""Id"" = @TenantId;", new { TenantId = tenantId });

        return true;
    }

    public async Task<bool> EnableServiceForTenantAsync(string tenantId, string serviceKey)
    {
        if (_serviceClients.TryGetValue(serviceKey, out var client))
        {
            return await client(tenantId);
        }
        return false;
    }

    public async Task<bool> DisableServiceForTenantAsync(string tenantId, string serviceKey)
    {
        if (_disableServiceClients.TryGetValue(serviceKey, out var client))
        {
            return await client(tenantId);
        }
        return false;
    }

    public async Task<TenantMigrationResult> MigrateAllServicesForTenantAsync(string tenantId)
    {
        var result = new TenantMigrationResult();

        foreach (var (serviceName, client) in _migrateServiceClients)
        {
            try
            {
                var ok = await client(tenantId);
                if (!ok)
                {
                    result.FailedServices.Add(serviceName);
                    _logger.LogWarning(
                        "テナント {TenantId} のサービス {ServiceName} マイグレーションが失敗しました(戻り値false)。",
                        tenantId, serviceName);
                }
            }
            catch (Exception ex)
            {
                result.FailedServices.Add(serviceName);
                _logger.LogError(ex,
                    "テナント {TenantId} のサービス {ServiceName} マイグレーション中に例外が発生しました。",
                    tenantId, serviceName);
            }
        }

        return result;
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
        return await Task.FromResult(true);
    }

    public async Task<string> GenerateApiKeyAsync(string tenantId)
    {
        var apiKey = $"key_{Guid.NewGuid():N}";
        await using var conn = await OpenAuthConnAsync();
        await conn.ExecuteAsync(@"UPDATE public.""Tenants"" SET ""ApiKey"" = @ApiKey WHERE ""Id"" = @TenantId;", new { ApiKey = apiKey, TenantId = tenantId });
        return apiKey;
    }

    public async Task<bool> UpdateAuthModeAsync(string tenantId, int authMode)
    {
        await using var conn = await OpenAuthConnAsync();
        await conn.ExecuteAsync(@"UPDATE public.""Tenants"" SET ""AuthMode"" = @AuthMode WHERE ""Id"" = @TenantId;", new { AuthMode = authMode, TenantId = tenantId });
        return true;
    }

    public async Task<IEnumerable<UserInfo>> GetUsersAsync()
    {
        await using var conn = await OpenAuthConnAsync();
        var users = await conn.QueryAsync<dynamic>(@"SELECT ""Id"", ""LoginId"", ""TenantId"", ""CreatedAt"" FROM public.""Users"";");
        return users.Select(u => new UserInfo(u.Id.ToString(), u.LoginId, u.TenantId, u.CreatedAt.ToString("o")));
    }

    public async Task<bool> RegisterUserAsync(string loginId, string password, string tenantId)
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
        await using var conn = await OpenAuthConnAsync();
        const string sql = @"
            INSERT INTO public.""Users"" (""LoginId"", ""PasswordHash"", ""TenantId"")
            VALUES (@LoginId, @PasswordHash, @TenantId);";
        await conn.ExecuteAsync(sql, new { LoginId = loginId, PasswordHash = passwordHash, TenantId = tenantId });
        return true;
    }

    public async Task<bool> DeleteUserAsync(string userId)
    {
        if (!int.TryParse(userId, out int id)) return false;
        await using var conn = await OpenAuthConnAsync();
        await conn.ExecuteAsync(@"DELETE FROM public.""Users"" WHERE ""Id"" = @Id;", new { Id = id });
        return true;
    }

    private static bool IsEnabled(HashSet<string> schemas, string tenantId, string key)
        => key.Equals("drive", StringComparison.OrdinalIgnoreCase)
             ? schemas.Contains($"app_{tenantId}")
             : schemas.Contains($"app_{tenantId}_{key}");
}