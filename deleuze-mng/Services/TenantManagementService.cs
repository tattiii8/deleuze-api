using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using DeleuzeMng.Models;

namespace DeleuzeMng.Services
{
    public class TenantManagementService : ITenantManagementService
    {
        private readonly string _appConnString;
        private readonly string _authConnString;
        private readonly Dictionary<string, Func<string, Task<bool>>> _serviceClients;
        private readonly Dictionary<string, Func<string, Task<bool>>> _disableServiceClients;

        public TenantManagementService(
            string appConnString,
            string authConnString,
            Dictionary<string, Func<string, Task<bool>>> serviceClients,
            Dictionary<string, Func<string, Task<bool>>> disableServiceClients)
        {
            _appConnString = appConnString;
            _authConnString = authConnString;
            _serviceClients = serviceClients;
            _disableServiceClients = disableServiceClients;
        }

        public async Task<IEnumerable<TenantInfo>> GetTenantsAsync()
        {
            // 1. Auth DB から登録済みテナントを唯一の正として取得
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

            // 2. App DB から全スキーマを取得（各サービス有効化の判定用）
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            const string schemaSql = @"
                SELECT schema_name 
                FROM information_schema.schemata
                WHERE schema_name LIKE 'app_%';";

            var schemas = (await appConn.QueryAsync<string>(schemaSql)).ToList();

            var result = new List<TenantInfo>();

            // 3. Auth DB に実在するテナントのみを基点に情報を構築
            foreach (var authDto in authTenants)
            {
                var tenantId = authDto.Id;
                var services = new List<string>();

                foreach (var serviceKey in _serviceClients.Keys)
                {
                    bool isEnabled = serviceKey switch
                    {
                        "drive" => schemas.Contains($"app_{tenantId}"),
                        _ => schemas.Contains($"app_{tenantId}_{serviceKey}")
                    };

                    if (isEnabled)
                    {
                        services.Add(serviceKey);
                    }
                }

                result.Add(new TenantInfo(tenantId, services, authDto.AuthMode, authDto.ApiKey));
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

            // 2. App DB から関連スキーマを取得
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            const string schemaSql = @"
                SELECT schema_name 
                FROM information_schema.schemata
                WHERE schema_name LIKE 'app_' || @TenantId || '%'
                   OR schema_name = 'app_' || @TenantId;";

            var schemas = (await appConn.QueryAsync<string>(schemaSql, new { TenantId = tenantId })).ToList();

            var services = new List<string>();
            foreach (var serviceKey in _serviceClients.Keys)
            {
                bool isEnabled = serviceKey switch
                {
                    "drive" => schemas.Contains($"app_{tenantId}"),
                    _ => schemas.Contains($"app_{tenantId}_{serviceKey}")
                };

                if (isEnabled)
                {
                    services.Add(serviceKey);
                }
            }

            return new TenantInfo(tenantId, services, authDto.AuthMode, authDto.ApiKey);
        }

        public async Task<bool> CreateTenantAsync(string tenantId, string name = "")
        {
            // 1. App DB に基本スキーマを作成
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            string schemaName = $"app_{tenantId}";
            string createSchemaSql = $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";";
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

            // 1. 各マイクロサービス（deleuze-drive等）の削除 API を呼んで S3 / サービススキーマをクリーンアップ
            foreach (var disableFunc in _disableServiceClients.Values)
            {
                try
                {
                    await disableFunc(tenantId);
                }
                catch
                {
                    // 既に削除済み・未接続時の例外は吸収
                }
            }

            // 2. deleuze-mng が作成した親スキーマ (app_{tenantId}) を App DB から完全破棄
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            string dropSchemaSql = $"DROP SCHEMA IF EXISTS \"app_{tenantId}\" CASCADE;";
            await appConn.ExecuteAsync(dropSchemaSql);

            // 3. Auth DB からテナントレコードを削除
            await using var authConn = new NpgsqlConnection(_authConnString);
            await authConn.OpenAsync();

            const string sql = @"DELETE FROM public.""Tenants"" WHERE ""Id"" = @TenantId;";
            var rows = await authConn.ExecuteAsync(sql, new { TenantId = tenantId });

            return rows > 0;
        }

        public async Task<bool> EnableServiceForTenantAsync(string tenantId, string serviceKey)
        {
            if (!_serviceClients.TryGetValue(serviceKey, out var clientFunc))
            {
                throw new ArgumentException($"未対応のサービスキーです: {serviceKey}");
            }

            return await clientFunc(tenantId);
        }

        public async Task<bool> DisableServiceForTenantAsync(string tenantId, string serviceKey)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(serviceKey))
            {
                return false;
            }

            if (_disableServiceClients.TryGetValue(serviceKey, out var disableFunc))
            {
                return await disableFunc(tenantId);
            }

            return false;
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
                SELECT ""Id"", ""UserName"" AS ""LoginId"", ""TenantId"", ""CreatedAt""
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
                INSERT INTO public.""Users"" (""Id"", ""UserName"", ""PasswordHash"", ""TenantId"", ""CreatedAt"")
                VALUES (@Id, @LoginId, @Password, @TenantId, NOW());";

            var rows = await conn.ExecuteAsync(sql, new {
                Id = Guid.NewGuid(),
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

            if (!Guid.TryParse(userId, out var userGuid)) return false;

            const string sql = @"DELETE FROM public.""Users"" WHERE ""Id"" = @Id;";
            var rows = await conn.ExecuteAsync(sql, new { Id = userGuid });

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
            public Guid Id { get; set; }
            public string? LoginId { get; set; }
            public string? TenantId { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}