using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace DeleuzeMng.Services
{
    public class TenantManagementService : ITenantManagementService
    {
        private readonly string _appConnString;
        private readonly string _authConnString;
        private readonly Dictionary<string, Func<string, Task<bool>>> _serviceClients;

        public TenantManagementService(
            string appConnString,
            string authConnString,
            Dictionary<string, Func<string, Task<bool>>> serviceClients)
        {
            _appConnString = appConnString;
            _authConnString = authConnString;
            _serviceClients = serviceClients;
        }

        public async Task<IEnumerable<TenantInfo>> GetTenantsAsync()
        {
            // 1. App DB からスキーマ一覧を取得
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            const string schemaSql = @"
                SELECT schema_name 
                FROM information_schema.schemata
                WHERE schema_name LIKE 'app_%'
                ORDER BY schema_name;";

            var schemas = (await appConn.QueryAsync<string>(schemaSql)).ToList();

            var baseTenants = schemas
                .Select(s => s.Replace("app_", ""))
                .Where(t => !t.Contains('_'))
                .Distinct()
                .ToList();

            // 2. Auth DB から各テナントの AuthMode と ApiKey を取得
            await using var authConn = new NpgsqlConnection(_authConnString);
            await authConn.OpenAsync();

            const string tenantAuthSql = @"
                SELECT ""Id"", ""AuthMode"", ""ApiKey"" 
                FROM public.""Tenants"";";

            var authTenants = (await authConn.QueryAsync<TenantAuthDto>(tenantAuthSql))
                .ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

            var result = new List<TenantInfo>();

            foreach (var tenantId in baseTenants)
            {
                var services = new List<string>();

                foreach (var serviceKey in _serviceClients.Keys)
                {
                    if (schemas.Contains($"app_{tenantId}_{serviceKey}"))
                    {
                        services.Add(serviceKey);
                    }
                }

                int authMode = 0;
                string? apiKey = null;

                if (authTenants.TryGetValue(tenantId, out var authDto))
                {
                    authMode = authDto.AuthMode;
                    apiKey = authDto.ApiKey;
                }

                result.Add(new TenantInfo(tenantId, services, authMode, apiKey));
            }

            return result;
        }

        public async Task<bool> CreateTenantAsync(string tenantId)
        {
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            string schemaName = $"app_{tenantId}";
            string createSchemaSql = $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";";
            await appConn.ExecuteAsync(createSchemaSql);

            // Auth DB 側にレコードを確保
            await using var authConn = new NpgsqlConnection(_authConnString);
            await authConn.OpenAsync();

            const string initAuthSql = @"
                INSERT INTO public.""Tenants"" (""Id"", ""Name"", ""AuthMode"")
                VALUES (@TenantId, @TenantId, 0)
                ON CONFLICT (""Id"") DO NOTHING;";

            await authConn.ExecuteAsync(initAuthSql, new { TenantId = tenantId });

            return true;
        }

        public async Task<bool> EnableServiceAsync(string tenantId, string serviceKey)
        {
            if (!_serviceClients.TryGetValue(serviceKey, out var clientFunc))
            {
                throw new ArgumentException($"未対応のサービスキーです: {serviceKey}");
            }

            return await clientFunc(tenantId);
        }

        public async Task<string> GenerateApiKeyAsync(string tenantId)
        {
            var apiKey = $"sk_live_{Guid.NewGuid():N}{Guid.NewGuid():N}";

            await using var conn = new NpgsqlConnection(_authConnString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO public.""Tenants"" (""Id"", ""Name"", ""ApiKey"", ""AuthMode"")
                VALUES (@TenantId, @TenantId, @ApiKey, 0)
                ON CONFLICT (""Id"") 
                DO UPDATE SET ""ApiKey"" = EXCLUDED.""ApiKey"";";

            await conn.ExecuteAsync(sql, new { TenantId = tenantId, ApiKey = apiKey });
            return apiKey;
        }

        public async Task<bool> UpdateAuthModeAsync(string tenantId, int authMode)
        {
            await using var conn = new NpgsqlConnection(_authConnString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO public.""Tenants"" (""Id"", ""Name"", ""AuthMode"")
                VALUES (@TenantId, @TenantId, @AuthMode)
                ON CONFLICT (""Id"") 
                DO UPDATE SET ""AuthMode"" = EXCLUDED.""AuthMode"";";

            var rows = await conn.ExecuteAsync(sql, new { TenantId = tenantId, AuthMode = authMode });
            return rows > 0;
        }

        private class TenantAuthDto
        {
            public string Id { get; set; } = string.Empty;
            public int AuthMode { get; set; }
            public string? ApiKey { get; set; }
        }
    }
}