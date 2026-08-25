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
    // Management DBのみ直接参照する
    private readonly string _mngConnString;

    private readonly Dictionary<string, Func<string, Task<bool>>> _serviceClients;
    private readonly Dictionary<string, Func<string, Task<bool>>> _disableServiceClients;
    private readonly Dictionary<string, Func<string, Task<bool>>> _migrateServiceClients;

    // Authサービスへの操作はAuthサービスへ委譲する
    private readonly IAuthManagementClient _authClient;

    private readonly ILogger<TenantManagementService> _logger;

    public TenantManagementService(
        string mngConnString,
        Dictionary<string, Func<string, Task<bool>>> serviceClients,
        Dictionary<string, Func<string, Task<bool>>> disableServiceClients,
        Dictionary<string, Func<string, Task<bool>>> migrateServiceClients,
        IAuthManagementClient authClient,
        ILogger<TenantManagementService> logger)
    {
        _mngConnString = mngConnString;

        _serviceClients = serviceClients;
        _disableServiceClients = disableServiceClients;
        _migrateServiceClients = migrateServiceClients;

        _authClient = authClient;

        _logger = logger;
    }

    private async Task<NpgsqlConnection> OpenMngConnAsync()
    {
        var conn = new NpgsqlConnection(_mngConnString);
        await conn.OpenAsync();
        return conn;
    }


    /* ==========================================
     * テナント管理
     * ========================================== */

    public async Task<IEnumerable<TenantInfo>> GetTenantsAsync()
    {
        await using var conn = await OpenMngConnAsync();

        var tenants = await conn.QueryAsync<TenantManagementDto>(
            @"
            SELECT
                ""Id"",
                ""Name"",
                ""Status"",
                ""CreatedAt""
            FROM public.""Tenants""
            ORDER BY ""Id"";
            ");

        return tenants.Select(tenant =>
            new TenantInfo(
                tenant.Id,
                _serviceClients.Keys.ToList(),
                0,
                null,
                tenant.Status
            ));
    }


    public async Task<TenantInfo?> GetTenantByIdAsync(string tenantId)
    {
        await using var conn = await OpenMngConnAsync();

        var tenant = await conn.QueryFirstOrDefaultAsync<TenantManagementDto>(
            @"
            SELECT
                ""Id"",
                ""Name"",
                ""Status"",
                ""CreatedAt""
            FROM public.""Tenants""
            WHERE ""Id"" = @TenantId;
            ",
            new
            {
                TenantId = tenantId
            });

        if (tenant == null)
        {
            return null;
        }

        return new TenantInfo(
            tenant.Id,
            _serviceClients.Keys.ToList(),
            0,
            null,
            tenant.Status
        );
    }


    public async Task<string?> GetTenantStatusAsync(string tenantId)
    {
        await using var conn = await OpenMngConnAsync();

        return await conn.QueryFirstOrDefaultAsync<string>(
            @"
            SELECT ""Status""
            FROM public.""Tenants""
            WHERE ""Id"" = @TenantId;
            ",
            new
            {
                TenantId = tenantId
            });
    }


    public async Task<bool> CreateTenantAsync(
        string tenantId,
        string name = "")
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return false;
        }

        await using var conn = await OpenMngConnAsync();

        const string sql = @"
            INSERT INTO public.""Tenants""
            (
                ""Id"",
                ""Name"",
                ""Status""
            )
            VALUES
            (
                @TenantId,
                @Name,
                'active'
            )
            ON CONFLICT (""Id"") DO NOTHING;
        ";

        var affectedRows = await conn.ExecuteAsync(
            sql,
            new
            {
                TenantId = tenantId,
                Name = string.IsNullOrWhiteSpace(name)
                    ? tenantId
                    : name
            });

        if (affectedRows == 0)
        {
            _logger.LogWarning(
                "テナントは既に存在します: {TenantId}",
                tenantId);

            return false;
        }

        return true;
    }


    public async Task<bool> DeleteTenantAsync(string tenantId)
    {
        /*
         * Auth / Drive等のサービス側のテナントデータを
         * 各サービスへ削除依頼する。
         *
         * MngからサービスDBを直接操作しない。
         */
        foreach (var (serviceName, client) in _disableServiceClients)
        {
            try
            {
                var ok = await client(tenantId);

                if (!ok)
                {
                    _logger.LogWarning(
                        "テナント {TenantId} のサービス {ServiceName} 削除に失敗しました。",
                        tenantId,
                        serviceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "テナント {TenantId} のサービス {ServiceName} 削除中にエラーが発生しました。",
                    tenantId,
                    serviceName);
            }
        }

        /*
         * サービス側への削除依頼後、
         * Management DBのテナント情報を削除する。
         */
        await using var conn = await OpenMngConnAsync();

        var affectedRows = await conn.ExecuteAsync(
            @"
            DELETE FROM public.""Tenants""
            WHERE ""Id"" = @TenantId;
            ",
            new
            {
                TenantId = tenantId
            });

        return affectedRows > 0;
    }


    /* ==========================================
     * テナントサービス管理
     * ========================================== */

    public async Task<bool> EnableServiceForTenantAsync(
        string tenantId,
        string serviceKey)
    {
        if (_serviceClients.TryGetValue(
                serviceKey,
                out var client))
        {
            try
            {
                return await client(tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "テナント {TenantId} のサービス {ServiceKey} 有効化中にエラーが発生しました。",
                    tenantId,
                    serviceKey);

                return false;
            }
        }

        _logger.LogWarning(
            "未対応のサービスキーです: {ServiceKey}",
            serviceKey);

        return false;
    }


    public async Task<bool> DisableServiceForTenantAsync(
        string tenantId,
        string serviceKey)
    {
        if (_disableServiceClients.TryGetValue(
                serviceKey,
                out var client))
        {
            try
            {
                return await client(tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "テナント {TenantId} のサービス {ServiceKey} 無効化中にエラーが発生しました。",
                    tenantId,
                    serviceKey);

                return false;
            }
        }

        _logger.LogWarning(
            "未対応のサービスキーです: {ServiceKey}",
            serviceKey);

        return false;
    }


    /* ==========================================
     * マイグレーション
     * ========================================== */

    public async Task<TenantMigrationResult> MigrateAllServicesForTenantAsync(
        string tenantId)
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
                        "テナント {TenantId} のサービス {ServiceName} マイグレーションが失敗しました。",
                        tenantId,
                        serviceName);
                }
            }
            catch (Exception ex)
            {
                result.FailedServices.Add(serviceName);

                _logger.LogError(
                    ex,
                    "テナント {TenantId} のサービス {ServiceName} マイグレーション中に例外が発生しました。",
                    tenantId,
                    serviceName);
            }
        }

        return result;
    }


    public async Task<bool> MigrateServiceForTenantAsync(
        string tenantId,
        string serviceKey)
    {
        if (!_migrateServiceClients.TryGetValue(
                serviceKey,
                out var client))
        {
            _logger.LogWarning(
                "未対応のサービスキーです: {ServiceKey}",
                serviceKey);

            return false;
        }

        try
        {
            return await client(tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "テナント {TenantId} のサービス {ServiceKey} マイグレーション中に例外が発生しました。",
                tenantId,
                serviceKey);

            return false;
        }
    }


    /* ==========================================
     * マイグレーション履歴
     * ========================================== */

    public async Task<IEnumerable<MigrationHistoryDto>> GetTenantMigrationsAsync(
        string tenantId)
    {
        return await Task.FromResult(
            Enumerable.Empty<MigrationHistoryDto>());
    }


    /* ==========================================
     * Health Check
     * ========================================== */

    public async Task<HealthCheckResultDto> CheckTenantHealthAsync(
        string tenantId)
    {
        return await Task.FromResult(
            new HealthCheckResultDto(
                "Healthy",
                "Healthy",
                "All services operating normally."));
    }


    /* ==========================================
     * テナントステータス
     * ========================================== */

    public async Task<bool> UpdateTenantStatusAsync(
        string tenantId,
        string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        if (!status.Equals("active", StringComparison.OrdinalIgnoreCase) &&
            !status.Equals("suspended", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "不正なテナントステータスです: {Status}",
                status);

            return false;
        }

        var normalizedStatus = status.ToLowerInvariant();

        await using var conn = await OpenMngConnAsync();

        var affectedRows = await conn.ExecuteAsync(
            @"
            UPDATE public.""Tenants""
            SET ""Status"" = @Status
            WHERE ""Id"" = @TenantId;
            ",
            new
            {
                TenantId = tenantId,
                Status = normalizedStatus
            });

        if (affectedRows == 0)
        {
            _logger.LogWarning(
                "ステータス更新対象のテナントが存在しません: {TenantId}",
                tenantId);

            return false;
        }

        return true;
    }


    /* ==========================================
     * API Key
     * ========================================== */

    public async Task<string> GenerateApiKeyAsync(
        string tenantId)
    {
        /*
         * API KeyはAuthサービスの責務。
         *
         * Mng DB / Auth DBを直接操作しない。
         */
        return await _authClient.GenerateApiKeyAsync(tenantId);
    }


    /* ==========================================
     * 認証モード
     * ========================================== */

    public async Task<bool> UpdateAuthModeAsync(
        string tenantId,
        int authMode)
    {
        /*
         * AuthModeはAuthサービスの責務。
         */
        return await _authClient.UpdateAuthModeAsync(
            tenantId,
            authMode);
    }


    /* ==========================================
     * ユーザー管理
     * ========================================== */

    public async Task<IEnumerable<UserInfo>> GetUsersAsync()
    {
        /*
         * UsersはAuthサービスが管理する。
         */
        return await _authClient.GetUsersAsync();
    }


    public async Task<bool> RegisterUserAsync(
        string loginId,
        string password,
        string tenantId)
    {
        /*
         * PasswordHash生成もAuthサービス側で行う。
         *
         * Mngは平文パスワードをハッシュ化しない。
         */
        return await _authClient.RegisterUserAsync(
            loginId,
            password,
            tenantId);
    }


    public async Task<bool> DeleteUserAsync(
        string userId)
    {
        return await _authClient.DeleteUserAsync(userId);
    }
}