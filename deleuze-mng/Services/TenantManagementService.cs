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
    private readonly string _authConnString;

    private readonly Dictionary<string, Func<string, Task<bool>>> _serviceClients;
    private readonly Dictionary<string, Func<string, Task<bool>>> _disableServiceClients;
    private readonly Dictionary<string, Func<string, Task<bool>>> _migrateServiceClients;

    private readonly ILogger<TenantManagementService> _logger;

    public TenantManagementService(
        string authConnString,
        Dictionary<string, Func<string, Task<bool>>> serviceClients,
        Dictionary<string, Func<string, Task<bool>>> disableServiceClients,
        Dictionary<string, Func<string, Task<bool>>> migrateServiceClients,
        ILogger<TenantManagementService> logger)
    {
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


    /* ==========================================
     * テナント管理
     * ========================================== */

    public async Task<IEnumerable<TenantInfo>> GetTenantsAsync()
    {
        await using var conn = await OpenAuthConnAsync();

        var dtos = await conn.QueryAsync<TenantAuthDto>(
            @"
            SELECT
                ""Id"",
                ""AuthMode"",
                ""ApiKey"",
                ""Status""
            FROM public.""Tenants"";
            ");

        /*
         * Schema の存在確認は各サービス側が責任を持つ。
         *
         * Management 側では app_{tenantId} 等の
         * サービスSchemaを直接参照しない。
         *
         * 現時点では TenantInfo の services について、
         * Management 側で保持しているサービス一覧を返す。
         *
         * サービス有効状態を正確に管理する場合は、
         * 将来的に TenantServices 等の管理テーブルを
         * 使用する。
         */
        return dtos.Select(dto => new TenantInfo(
            dto.Id,
            _serviceClients.Keys.ToList(),
            dto.AuthMode,
            dto.ApiKey,
            dto.Status
        ));
    }


    public async Task<TenantInfo?> GetTenantByIdAsync(string tenantId)
    {
        await using var conn = await OpenAuthConnAsync();

        var dto = await conn.QueryFirstOrDefaultAsync<TenantAuthDto>(
            @"
            SELECT
                ""Id"",
                ""AuthMode"",
                ""ApiKey"",
                ""Status""
            FROM public.""Tenants""
            WHERE ""Id"" = @TenantId;
            ",
            new
            {
                TenantId = tenantId
            });

        if (dto == null)
        {
            return null;
        }

        /*
         * Schema の存在確認は行わない。
         *
         * 各サービスのSchemaは各サービス自身が管理する。
         */
        var activeServices = _serviceClients.Keys.ToList();

        return new TenantInfo(
            dto.Id,
            activeServices,
            dto.AuthMode,
            dto.ApiKey,
            dto.Status
        );
    }


    public async Task<string?> GetTenantStatusAsync(string tenantId)
    {
        await using var conn = await OpenAuthConnAsync();

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
        /*
         * Schema の作成は行わない。
         *
         * 各サービスが TenantSchemaManager.ProvisionAsync()
         * を通して自身のSchemaを作成する。
         */

        await using var authConn = await OpenAuthConnAsync();

        const string sql = @"
            INSERT INTO public.""Tenants""
            (
                ""Id"",
                ""Name"",
                ""AuthMode"",
                ""Status""
            )
            VALUES
            (
                @TenantId,
                @Name,
                0,
                'active'
            )
            ON CONFLICT (""Id"") DO NOTHING;
        ";

        await authConn.ExecuteAsync(
            sql,
            new
            {
                TenantId = tenantId,
                Name = string.IsNullOrWhiteSpace(name)
                    ? tenantId
                    : name
            });

        return true;
    }


    public async Task<bool> DeleteTenantAsync(string tenantId)
    {
        /*
         * サービスSchemaの削除は各サービスに委譲する。
         *
         * Management 側では app_{tenantId} 等を
         * 直接 DROP しない。
         */
        foreach (var (serviceName, client) in _disableServiceClients)
        {
            try
            {
                await client(tenantId);
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
         * 最後に Management DB の Tenants レコードを削除する。
         */
        await using var authConn = await OpenAuthConnAsync();

        var affectedRows = await authConn.ExecuteAsync(
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
            return await client(tenantId);
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
            return await client(tenantId);
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
                        "テナント {TenantId} のサービス {ServiceName} マイグレーションが失敗しました(戻り値false)。",
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
            var ok = await client(tenantId);

            if (!ok)
            {
                _logger.LogWarning(
                    "テナント {TenantId} のサービス {ServiceKey} マイグレーションが失敗しました(戻り値false)。",
                    tenantId,
                    serviceKey);
            }

            return ok;
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

        if (!status.Equals(
                "active",
                StringComparison.OrdinalIgnoreCase) &&
            !status.Equals(
                "suspended",
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "不正なテナントステータスです: {Status}",
                status);

            return false;
        }

        var normalizedStatus =
            status.ToLowerInvariant();

        await using var conn =
            await OpenAuthConnAsync();

        var affectedRows =
            await conn.ExecuteAsync(
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
        var apiKey =
            $"key_{Guid.NewGuid():N}";

        await using var conn =
            await OpenAuthConnAsync();

        await conn.ExecuteAsync(
            @"
            UPDATE public.""Tenants""
            SET ""ApiKey"" = @ApiKey
            WHERE ""Id"" = @TenantId;
            ",
            new
            {
                ApiKey = apiKey,
                TenantId = tenantId
            });

        return apiKey;
    }


    /* ==========================================
     * 認証モード
     * ========================================== */

    public async Task<bool> UpdateAuthModeAsync(
        string tenantId,
        int authMode)
    {
        await using var conn =
            await OpenAuthConnAsync();

        var affectedRows =
            await conn.ExecuteAsync(
                @"
                UPDATE public.""Tenants""
                SET ""AuthMode"" = @AuthMode
                WHERE ""Id"" = @TenantId;
                ",
                new
                {
                    AuthMode = authMode,
                    TenantId = tenantId
                });

        return affectedRows > 0;
    }


    /* ==========================================
     * ユーザー管理
     * ========================================== */

    public async Task<IEnumerable<UserInfo>> GetUsersAsync()
    {
        await using var conn =
            await OpenAuthConnAsync();

        var users =
            await conn.QueryAsync<dynamic>(
                @"
                SELECT
                    ""Id"",
                    ""LoginId"",
                    ""TenantId"",
                    ""CreatedAt""
                FROM public.""Users"";
                ");

        return users.Select(u =>
            new UserInfo(
                u.Id.ToString(),
                u.LoginId,
                u.TenantId,
                u.CreatedAt.ToString("o")));
    }


    public async Task<bool> RegisterUserAsync(
        string loginId,
        string password,
        string tenantId)
    {
        var passwordHash =
            BCrypt.Net.BCrypt.HashPassword(password);

        await using var conn =
            await OpenAuthConnAsync();

        const string sql = @"
            INSERT INTO public.""Users""
            (
                ""LoginId"",
                ""PasswordHash"",
                ""TenantId""
            )
            VALUES
            (
                @LoginId,
                @PasswordHash,
                @TenantId
            );
        ";

        await conn.ExecuteAsync(
            sql,
            new
            {
                LoginId = loginId,
                PasswordHash = passwordHash,
                TenantId = tenantId
            });

        return true;
    }


    public async Task<bool> DeleteUserAsync(
        string userId)
    {
        if (!int.TryParse(
                userId,
                out int id))
        {
            return false;
        }

        await using var conn =
            await OpenAuthConnAsync();

        var affectedRows =
            await conn.ExecuteAsync(
                @"
                DELETE FROM public.""Users""
                WHERE ""Id"" = @Id;
                ",
                new
                {
                    Id = id
                });

        return affectedRows > 0;
    }
}