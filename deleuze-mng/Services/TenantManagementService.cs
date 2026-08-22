using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using DeleuzeMng.Services.Infrastructure;

namespace DeleuzeMng.Services
{
    public class TenantManagementService
    {
        private readonly string _appConnString;
        private readonly string _authConnString;
        private readonly Dictionary<string, IServiceProvisioningClient> _serviceClients;

        private static readonly Regex ValidTenantIdPattern = new(@"^[a-z][a-z0-9_]{2,58}$", RegexOptions.Compiled);

        public TenantManagementService(
            IConfiguration configuration,
            IEnumerable<IServiceProvisioningClient> serviceClients)
        {
            _appConnString = configuration.GetConnectionString("AppConnection")
                ?? throw new InvalidOperationException("接続文字列 'AppConnection' が設定されていません。");
            _authConnString = configuration.GetConnectionString("AuthConnection")
                ?? throw new InvalidOperationException("接続文字列 'AuthConnection' が設定されていません。");

            _serviceClients = serviceClients.ToDictionary(c => c.ServiceKey.ToLower(), c => c);
        }

        public bool IsSupportedService(string serviceKey)
        {
            return !string.IsNullOrWhiteSpace(serviceKey) && _serviceClients.ContainsKey(serviceKey.ToLower());
        }

        /// <summary>
        /// テナントを作成し、指定されたサービス群の初期化 API を呼び出す。途中で失敗した場合は全補償削除する。
        /// </summary>
        public async Task CreateTenantAsync(string tenantId, IEnumerable<string>? servicesToEnable = null)
        {
            EnsureValidTenantId(tenantId);
            string schemaName = $"app_{tenantId}";

            var enabledServices = servicesToEnable?.Select(s => s.ToLower()).Distinct().ToList() ?? new List<string>();

            // 1. 未対応サービスの事前検証
            foreach (var serviceKey in enabledServices)
            {
                if (!IsSupportedService(serviceKey))
                    throw new ArgumentException($"未対応のサービスが含まれています: '{serviceKey}'", nameof(servicesToEnable));
            }

            var completedServices = new List<string>();
            bool coreCreated = false;

            try
            {
                // 2. Core (App) DB の基本スキーマ作成
                await CreateCoreAppSchemaAsync(tenantId, schemaName);
                coreCreated = true;

                // 3. 各サービスの内部 API を呼び出してプロビジョニング実行
                foreach (var serviceKey in enabledServices)
                {
                    await _serviceClients[serviceKey].InitializeTenantAsync(tenantId);
                    completedServices.Add(serviceKey);
                }
            }
            catch
            {
                // 補償処理：途中で失敗した場合は作成済みのものをロールバックする
                await RollbackTenantCreationAsync(tenantId, coreCreated, completedServices);
                throw;
            }
        }

        /// <summary>
        /// 既存テナントに対して単体でサービスを後から追加有効化する
        /// </summary>
        public async Task EnableServiceForTenantAsync(string tenantId, string serviceKey)
        {
            EnsureValidTenantId(tenantId);

            if (string.IsNullOrWhiteSpace(serviceKey) || !IsSupportedService(serviceKey))
                throw new ArgumentException($"未対応のサービスキーです: '{serviceKey}'", nameof(serviceKey));

            await _serviceClients[serviceKey.ToLower()].InitializeTenantAsync(tenantId);
        }

        private async Task CreateCoreAppSchemaAsync(string tenantId, string schemaName)
        {
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            var alreadyExists = await appConn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = @schemaName);",
                new { schemaName });

            if (alreadyExists)
                throw new InvalidOperationException($"テナント '{tenantId}' はすでに存在します。");

            await using var tx = await appConn.BeginTransactionAsync();
            try
            {
                await using (var createSchemaCmd = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";", appConn, tx))
                {
                    await createSchemaCmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private async Task RollbackTenantCreationAsync(string tenantId, bool coreCreated, List<string> completedServices)
        {
            string schemaName = $"app_{tenantId}";

            // 1. 各サービス側 API にロールバック要求
            foreach (var serviceKey in completedServices)
            {
                if (_serviceClients.TryGetValue(serviceKey, out var client))
                {
                    await client.RollbackTenantAsync(tenantId);
                }
            }

            // 2. Core (App) スキーマの補償削除
            if (coreCreated)
            {
                try
                {
                    await using var appConn = new NpgsqlConnection(_appConnString);
                    await appConn.OpenAsync();
                    await using var cmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;", appConn);
                    await cmd.ExecuteNonQueryAsync();
                }
                catch { }
            }
        }

        public async Task RegisterUserAsync(string loginId, string password, string tenantId)
        {
            EnsureValidTenantId(tenantId);

            if (string.IsNullOrWhiteSpace(loginId))
                throw new ArgumentException("LoginId は必須です。", nameof(loginId));

            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                throw new ArgumentException("Password は8文字以上で指定してください。", nameof(password));

            await using var authConn = new NpgsqlConnection(_authConnString);
            await authConn.OpenAsync();

            var loginIdExists = await authConn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM public.\"Users\" WHERE \"LoginId\" = @loginId);",
                new { loginId });

            if (loginIdExists)
                throw new InvalidOperationException($"LoginId '{loginId}' は既に使用されています。");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

            const string insertSql = @"
                INSERT INTO public.""Users"" (""LoginId"", ""PasswordHash"", ""TenantId"")
                VALUES (@loginId, @passwordHash, @tenantId);";

            await authConn.ExecuteAsync(insertSql, new { loginId, passwordHash, tenantId });
        }

        public async Task<IEnumerable<UserInfo>> GetUsersAsync()
        {
            await using var authConn = new NpgsqlConnection(_authConnString);
            const string sql = @"
                SELECT ""Id"", ""LoginId"", ""TenantId"", ""CreatedAt"" 
                FROM public.""Users"" 
                ORDER BY ""Id"" DESC;";
            
            return await authConn.QueryAsync<UserInfo>(sql);
        }

        public async Task<IEnumerable<TenantInfo>> GetTenantsAsync()
        {
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            const string sql = @"
                SELECT schema_name 
                FROM information_schema.schemata
                WHERE schema_name LIKE 'app_%'
                ORDER BY schema_name;";

            var schemas = (await appConn.QueryAsync<string>(sql)).ToList();

            // ベースとなるテナントID（app_flaubert のようなコア用スキーマ）を抽出
            var baseTenants = schemas
                .Select(s => s.Replace("app_", ""))
                .Where(t => !t.Contains('_')) // app_flaubert_drive 等のサービス拡張スキーマを除外
                .Distinct()
                .ToList();

            var result = new List<TenantInfo>();

            foreach (var tenantId in baseTenants)
            {
                var services = new List<string>();

                // サポートされている全クライアントに対して、判定用のスキーマが存在するか確認
                foreach (var serviceKey in _serviceClients.Keys)
                {
                    if (schemas.Contains($"app_{tenantId}_{serviceKey}"))
                    {
                        services.Add(serviceKey);
                    }
                }

                result.Add(new TenantInfo(tenantId, services));
            }

            return result;
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            await using var authConn = new NpgsqlConnection(_authConnString);
            const string sql = @"DELETE FROM public.""Users"" WHERE ""Id"" = @id;";
            int affected = await authConn.ExecuteAsync(sql, new { id });
            return affected > 0;
        }

        public async Task DeleteTenantAsync(string tenantId)
        {
            EnsureValidTenantId(tenantId);
            string schemaName = $"app_{tenantId}";

            // 1. App DB から Core スキーマ削除
            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();
            await using (var dropCmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;", appConn))
            {
                await dropCmd.ExecuteNonQueryAsync();
            }

            // 2. 登録されている全マイクロサービス API に削除依頼
            foreach (var client in _serviceClients.Values)
            {
                await client.RollbackTenantAsync(tenantId);
            }

            // 3. 認証 DB からの関連ユーザー削除
            await using var authConn = new NpgsqlConnection(_authConnString);
            const string deleteUsersSql = @"DELETE FROM public.""Users"" WHERE ""TenantId"" = @tenantId;";
            await authConn.ExecuteAsync(deleteUsersSql, new { tenantId });
        }

        private static void EnsureValidTenantId(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !ValidTenantIdPattern.IsMatch(tenantId))
            {
                throw new ArgumentException(
                    $"不正なテナントID形式です。小文字英数字とアンダースコアのみ、3〜59文字で指定してください: '{tenantId}'",
                    nameof(tenantId));
            }
        }
    }

    public record UserInfo(int Id, string LoginId, string TenantId, DateTime CreatedAt);
    public record TenantInfo(string TenantId, List<string> Services);
}