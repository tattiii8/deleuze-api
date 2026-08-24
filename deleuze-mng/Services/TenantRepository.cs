using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DeleuzeMng.Models;
using Dapper;
using Npgsql;

namespace DeleuzeMng.Services;

public interface ITenantRepository
{
    Task<IEnumerable<TenantAuthDto>> GetAllAuthTenantsAsync();
    Task<TenantAuthDto?> GetAuthTenantByIdAsync(string tenantId);
    Task<HashSet<string>> GetSchemasAsync(string pattern);
    Task SaveTenantApiKeyAsync(string tenantId, string apiKey);
    Task UpdateAuthModeAsync(string tenantId, int authMode);
    Task UpdateStatusAsync(string tenantId, string status);
    Task CreateTenantSchemaAndRecordAsync(string tenantId, string name);
    Task DropTenantSchemaAndRecordAsync(string tenantId);
}

public class TenantRepository : ITenantRepository
{
    private readonly string _appConnString;
    private readonly string _authConnString;

    public TenantRepository(string appConnString, string authConnString)
    {
        _appConnString = appConnString;
        _authConnString = authConnString;
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

    public async Task<IEnumerable<TenantAuthDto>> GetAllAuthTenantsAsync()
    {
        await using var conn = await OpenAuthConnAsync();
        return await conn.QueryAsync<TenantAuthDto>(@"SELECT ""Id"", ""AuthMode"", ""ApiKey"" FROM public.""Tenants"";");
    }

    public async Task<TenantAuthDto?> GetAuthTenantByIdAsync(string tenantId)
    {
        await using var conn = await OpenAuthConnAsync();
        return await conn.QueryFirstOrDefaultAsync<TenantAuthDto>(@"SELECT ""Id"", ""AuthMode"", ""ApiKey"" FROM public.""Tenants"" WHERE ""Id"" = @TenantId;", new { TenantId = tenantId });
    }

    public async Task<HashSet<string>> GetSchemasAsync(string pattern)
    {
        await using var conn = await OpenAppConnAsync();
        var result = await conn.QueryAsync<string>(
            @"SELECT schema_name FROM information_schema.schemata WHERE schema_name LIKE @Pattern;", 
            new { Pattern = pattern });
        return result.ToHashSet();
    }

    public async Task CreateTenantSchemaAndRecordAsync(string tenantId, string name)
    {
        await using var appConn = await OpenAppConnAsync();
        await appConn.ExecuteAsync($"CREATE SCHEMA IF NOT EXISTS \"app_{tenantId}\";");

        await using var authConn = await OpenAuthConnAsync();
        const string sql = @"
            INSERT INTO public.""Tenants"" (""Id"", ""Name"", ""AuthMode"")
            VALUES (@TenantId, @Name, 0)
            ON CONFLICT (""Id"") DO NOTHING;";
        await authConn.ExecuteAsync(sql, new { TenantId = tenantId, Name = string.IsNullOrWhiteSpace(name) ? tenantId : name });
    }

    public async Task DropTenantSchemaAndRecordAsync(string tenantId)
    {
        await using var appConn = await OpenAppConnAsync();
        await appConn.ExecuteAsync($"DROP SCHEMA IF EXISTS \"app_{tenantId}\" CASCADE;");

        await using var authConn = await OpenAuthConnAsync();
        await authConn.ExecuteAsync(@"DELETE FROM public.""Tenants"" WHERE ""Id"" = @TenantId;", new { TenantId = tenantId });
    }

    public async Task SaveTenantApiKeyAsync(string tenantId, string apiKey)
    {
        await using var conn = await OpenAuthConnAsync();
        await conn.ExecuteAsync(@"UPDATE public.""Tenants"" SET ""ApiKey"" = @ApiKey WHERE ""Id"" = @TenantId;", new { ApiKey = apiKey, TenantId = tenantId });
    }

    public async Task UpdateAuthModeAsync(string tenantId, int authMode)
    {
        await using var conn = await OpenAuthConnAsync();
        await conn.ExecuteAsync(@"UPDATE public.""Tenants"" SET ""AuthMode"" = @AuthMode WHERE ""Id"" = @TenantId;", new { AuthMode = authMode, TenantId = tenantId });
    }

    public async Task UpdateStatusAsync(string tenantId, string status)
    {
        await Task.CompletedTask;
    }
}