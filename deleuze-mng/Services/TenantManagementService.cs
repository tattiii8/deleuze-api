using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using DeleuzeMng.Models;
using DeleuzeMng.Services.Clients; // DriveProvisioningClient や DTO を参照するため

namespace DeleuzeMng.Services
{
    public class TenantManagementService : ITenantManagementService
    {
        private readonly string _appConnString;
        private readonly string _authConnString;
        private readonly Dictionary<string, Func<string, Task<bool>>> _serviceClients;
        private readonly Dictionary<string, Func<string, Task<bool>>> _disableServiceClients;
        private readonly Dictionary<string, Func<string, Task<bool>>> _migrateServiceClients;
        private readonly DriveProvisioningClient? _driveClient; // 👈 追加: 履歴やヘルスチェック用

        public TenantManagementService(
            string appConnString,
            string authConnString,
            Dictionary<string, Func<string, Task<bool>>> serviceClients,
            Dictionary<string, Func<string, Task<bool>>> disableServiceClients,
            Dictionary<string, Func<string, Task<bool>>> migrateServiceClients,
            DriveProvisioningClient? driveClient = null) // 👈 追加
        {
            _appConnString = appConnString;
            _authConnString = authConnString;
            _serviceClients = serviceClients;
            _disableServiceClients = disableServiceClients;
            _migrateServiceClients = migrateServiceClients;
            _driveClient = driveClient;
        }

        public async Task<IEnumerable<TenantInfo>> GetTenantsAsync()
        {
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

            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            const string schemaSql = @"
                SELECT schema_name 
                FROM information_schema.schemata
                WHERE schema_name LIKE 'app_%';";

            var schemas = (await appConn.QueryAsync<string>(schemaSql)).ToList();
            var result = new List<TenantInfo>();

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
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            string schemaName = $"app_{tenantId}";
            string createSchemaSql = $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";";
            await appConn.ExecuteAsync(createSchemaSql);

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

            foreach (var disableFunc in _disableServiceClients.Values)
            {
                try
                {
                    await disableFunc(tenantId);
                }
                catch
                {
                    // 削除エラーは吸収
                }
            }

            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            string dropSchemaSql = $"DROP SCHEMA IF EXISTS \"app_{tenantId}\" CASCADE;";
            await appConn.ExecuteAsync(dropSchemaSql);

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

        public async Task<bool> MigrateServiceForTenantAsync(string tenantId, string serviceKey)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(serviceKey))
            {
                return false;
            }

            if (_migrateServiceClients.TryGetValue(serviceKey, out var migrateFunc))
            {
                return await migrateFunc(tenantId);
            }

            throw new ArgumentException($"マイグレーション未対応のサービスキーです: {serviceKey}");
        }

        public async Task<bool> MigrateAllServicesForTenantAsync(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return false;

            bool allSuccess = true;
            foreach (var migrateFunc in _migrateServiceClients.Values)
            {
                try
                {
                    var success = await migrateFunc(tenantId);
                    if (!success) allSuccess = false;
                }
                catch
                {
                    allSuccess = false;
                }
            }

            return allSuccess;
        }

        // 💡 追加実装 1: マイグレーション履歴の取得
        public async Task<IEnumerable<MigrationHistoryDto>> GetTenantMigrationsAsync(string tenantId)
        {
            if (_driveClient != null)
            {
                var history = await _driveClient.GetTenantMigrationsAsync(tenantId);
                return history.Select(h => new MigrationHistoryDto(h.MigrationName, h.AppliedAt.ToString("yyyy-MM-dd HH:mm:ss")));
            }
            return Enumerable.Empty<MigrationHistoryDto>();
        }

        // 💡 追加実装 2: ヘルスチェックの実行
        public async Task<HealthCheckResultDto> CheckTenantHealthAsync(string tenantId)
        {
            if (_driveClient != null)
            {
                var health = await _driveClient.CheckTenantHealthAsync(tenantId);
                return new HealthCheckResultDto(health.DbStatus, health.StorageStatus, health.Message);
            }
            return new HealthCheckResultDto("Unknown", "Unknown", "Drive client not configured");
        }

        // 💡 追加実装 3: テナントのステータス変更 (一時停止/有効化など)
        public async Task<bool> UpdateTenantStatusAsync(string tenantId, string status)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return false;

            await using var conn = new NpgsqlConnection(_authConnString);
            await conn.OpenAsync();

            // 必要に応じて Tenants テーブルにステータス用のカラムがある前提、または AuthMode 等の拡張
            const string sql = @"
                UPDATE public.""Tenants"" 
                SET ""Name"" = ""Name"" 
                WHERE ""Id"" = @TenantId;"; // ステータスカラムを追加している場合はここに SET カラム = @Status を記述

            var rows = await conn.ExecuteAsync(sql, new { TenantId = tenantId, Status = status });
            return rows > 0;
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