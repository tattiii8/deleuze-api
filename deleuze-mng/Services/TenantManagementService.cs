using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using DeleuzeMng.Models;
using Deleuze.Shared.Infrastructure;

namespace DeleuzeMng.Services
{
    public class TenantManagementService : ITenantManagementService
    {
        private readonly string _appConnString;
        private readonly string _authConnString;
        private readonly IEnumerable<IServiceProvisioningClient> _provisioningClients;

        public TenantManagementService(
            string appConnString,
            string authConnString,
            IEnumerable<IServiceProvisioningClient> provisioningClients)
        {
            _appConnString = appConnString;
            _authConnString = authConnString;
            _provisioningClients = provisioningClients;
        }

        public async Task<IEnumerable<TenantInfo>> GetTenantsAsync()
        {
            // 1. Auth DB から登録済みテナントを取得
            await using var authConn = new NpgsqlConnection(_authConnString);
            await authConn.OpenAsync();

            const string tenantAuthSql = @"
                SELECT ""Id"", ""AuthMode"", ""ApiKey"" 
                FROM public.""Tenants"";";

            var authTenants = (await authConn.QueryAsync<TenantAuthDto>(tenantAuthSql)).ToList();

            if (!authTenants.Any())
            {
                return Enumerable.Empty<TenantInfo>();
            }

            // 2. App DB から全スキーマを取得
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            const string schemaSql = @"
                SELECT schema_name 
                FROM information_schema.schemata;";

            var schemas = (await appConn.QueryAsync<string>(schemaSql)).ToList();

            var result = new List<TenantInfo>();

            // 3. DIで登録されている各サービスの {serviceKey}_{tenantId} スキーマ存在チェック
            foreach (var authDto in authTenants)
            {
                var tenantId = authDto.Id;
                var activeServices = new List<string>();

                foreach (var client in _provisioningClients)
                {
                    // スキーマ命名規則: {serviceKey}_{tenantId}
                    string expectedSchema = $"{client.ServiceName}_{tenantId}";
                    if (schemas.Contains(expectedSchema))
                    {
                        activeServices.Add(client.ServiceName);
                    }
                }

                result.Add(new TenantInfo(tenantId, activeServices, authDto.AuthMode, authDto.ApiKey));
            }

            return result;
        }

        public async Task<TenantInfo?> GetTenantByIdAsync(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return null;

            // 1. Auth DB から対象テナントを取得
            await using var authConn = new NpgsqlConnection(_authConnString);
            await authConn.OpenAsync();

            const string tenantAuthSql = @"
                SELECT ""Id"", ""AuthMode"", ""ApiKey"" 
                FROM public.""Tenants""
                WHERE ""Id"" = @TenantId;";

            var authDto = await authConn.QueryFirstOrDefaultAsync<TenantAuthDto>(tenantAuthSql, new { TenantId = tenantId });

            if (authDto == null)
            {
                return null;
            }

            // 2. App DB から全スキーマを取得して {serviceKey}_{tenantId} の合致判定
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            const string schemaSql = @"
                SELECT schema_name 
                FROM information_schema.schemata
                WHERE schema_name LIKE '%_' || @TenantId;";

            var schemas = (await appConn.QueryAsync<string>(schemaSql, new { TenantId = tenantId })).ToList();

            var activeServices = new List<string>();
            foreach (var client in _provisioningClients)
            {
                string expectedSchema = $"{client.ServiceName}_{tenantId}";
                if (schemas.Contains(expectedSchema))
                {
                    activeServices.Add(client.ServiceName);
                }
            }

            return new TenantInfo(tenantId, activeServices, authDto.AuthMode, authDto.ApiKey);
        }

        public async Task<bool> CreateTenantAsync(string tenantId, string name = "")
        {
            // 1. App DB に mng 用の基本スキーマ (mng_{tenantId}) や共通環境を作成（必要に応じて）
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            string baseSchemaName = $"mng_{tenantId}";
            string createSchemaSql = $"CREATE SCHEMA IF NOT EXISTS \"{baseSchemaName}\";";
            await appConn.ExecuteAsync(createSchemaSql);

            // 2. Auth DB にテナント情報を登録
            await using var authConn = new NpgsqlConnection(_authConnString);
            await authConn.OpenAsync();

            string tenantName = string.IsNullOrWhiteSpace(name) ? tenantId : name;

            const string initAuthSql = @"
                INSERT INTO public.""Tenants"" (""Id"", ""Name"", ""AuthMode"")
                VALUES (@TenantId, @Name, 0)
                ON CONFLICT (""Id"") DO NOTHING;";

            await authConn.ExecuteAsync(initAuthSql, new { TenantId = tenantId, Name = tenantName });

            return true;
        }

        public async Task<bool> DeleteTenantAsync(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return false;

            // 1. 登録されている全マイクロサービスの Deprovision API を呼び出し
            foreach (var client in _provisioningClients)
            {
                try
                {
                    await client.DeprovisionTenantAsync(tenantId);
                }
                catch
                {
                    // 既に削除済み・未接続時の例外は吸収
                }
            }

            // 2. App DB から *_{tenantId} のスキーマをすべて破棄（mng_{tenantId}含む）
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            const string findSchemasSql = @"
                SELECT schema_name 
                FROM information_schema.schemata
                WHERE schema_name LIKE '%_' || @TenantId;";

            var targetSchemas = await appConn.QueryAsync<string>(findSchemasSql, new { TenantId = tenantId });

            foreach (var schema in targetSchemas)
            {
                string dropSchemaSql = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;";
                await appConn.ExecuteAsync(dropSchemaSql);
            }

            // 3. Auth DB からテナントレコードを削除
            await using var authConn = new NpgsqlConnection(_authConnString);
            await authConn.OpenAsync();

            const string sql = @"DELETE FROM public.""Tenants"" WHERE ""Id"" = @TenantId;";
            var rows = await authConn.ExecuteAsync(sql, new { TenantId = tenantId });

            return rows > 0;
        }

        public async Task<bool> EnableServiceForTenantAsync(string tenantId, string serviceKey)
        {
            var client = _provisioningClients.FirstOrDefault(c => string.Equals(c.ServiceName, serviceKey, StringComparison.OrdinalIgnoreCase));
            if (client == null)
            {
                throw new ArgumentException($"未対応または未登録のサービスキーです: {serviceKey}");
            }

            return await client.ProvisionTenantAsync(tenantId);
        }

        public async Task<bool> DisableServiceForTenantAsync(string tenantId, string serviceKey)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(serviceKey))
            {
                return false;
            }

            var client = _provisioningClients.FirstOrDefault(c => string.Equals(c.ServiceName, serviceKey, StringComparison.OrdinalIgnoreCase));
            if (client == null)
            {
                return false;
            }

            return await client.DeprovisionTenantAsync(tenantId);
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

        public async Task<IEnumerable<UserInfo>> GetUsersAsync()
        {
            await using var conn = new NpgsqlConnection(_authConnString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT ""Id"", ""LoginId"", ""TenantId"", ""CreatedAt""
                FROM public.""Users"";";

            var users = await conn.QueryAsync<UserDto>(sql);
            return users.Select(u => new UserInfo(
                u.Id.ToString(),
                u.LoginId ?? "",
                u.TenantId ?? "",
                u.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            ));
        }

        public async Task<bool> RegisterUserAsync(string loginId, string password, string tenantId)
        {
            await using var conn = new NpgsqlConnection(_authConnString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO public.""Users"" (""LoginId"", ""PasswordHash"", ""TenantId"", ""CreatedAt"")
                VALUES (@LoginId, @Password, @TenantId, NOW());";

            var rows = await conn.ExecuteAsync(sql, new {
                LoginId = loginId,
                Password = password,
                TenantId = tenantId
            });

            return rows > 0;
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            await using var conn = new NpgsqlConnection(_authConnString);
            await conn.OpenAsync();

            if (!int.TryParse(userId, out var userIntId)) return false;

            const string sql = @"DELETE FROM public.""Users"" WHERE ""Id"" = @Id;";
            var rows = await conn.ExecuteAsync(sql, new { Id = userIntId });

            return rows > 0;
        }

        private class TenantAuthDto
        {
            public string Id { get; set; } = string.Empty;
            public int AuthMode { get; set; }
            public string? ApiKey { get; set; }
        }

        private class UserDto
        {
            public int Id { get; set; }
            public string? LoginId { get; set; }
            public string? TenantId { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}