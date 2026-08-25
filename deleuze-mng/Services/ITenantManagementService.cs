using System.Collections.Generic;
using System.Threading.Tasks;

namespace DeleuzeMng.Services
{
    public record TenantInfo(
        string TenantId,
        List<string> Services,
        int AuthMode = 0,
        string? ApiKey = null,
        string Status = "active"
    );

    public record UserInfo(
        string Id,
        string LoginId,
        string TenantId,
        string CreatedAt
    );

    // コントローラーで利用する DTO の定義
    public record MigrationHistoryDto(
        string MigrationName,
        string AppliedAt
    );

    public record HealthCheckResultDto(
        string DbStatus,
        string StorageStatus,
        string Message
    );

    // マイグレーション結果（失敗サービス一覧を含む）
    public class TenantMigrationResult
    {
        public bool Success => FailedServices.Count == 0;

        public List<string> FailedServices { get; } = new();
    }

    public interface ITenantManagementService
    {
        // テナント一覧取得
        Task<IEnumerable<TenantInfo>> GetTenantsAsync();

        // テナント詳細取得
        Task<TenantInfo?> GetTenantByIdAsync(string tenantId);

        // テナント作成・削除
        Task<bool> CreateTenantAsync(
            string tenantId,
            string name = ""
        );

        Task<bool> DeleteTenantAsync(
            string tenantId
        );

        // サービス管理
        Task<bool> EnableServiceForTenantAsync(
            string tenantId,
            string serviceKey
        );

        Task<bool> DisableServiceForTenantAsync(
            string tenantId,
            string serviceKey
        );

        // 全サービスに対するマイグレーション実行
        // 失敗したサービス名を含む結果を返す
        Task<TenantMigrationResult> MigrateAllServicesForTenantAsync(
            string tenantId
        );

        // 指定サービスのマイグレーション実行
        Task<bool> MigrateServiceForTenantAsync(
            string tenantId,
            string serviceKey
        );

        // マイグレーション履歴の取得
        Task<IEnumerable<MigrationHistoryDto>> GetTenantMigrationsAsync(
            string tenantId
        );

        // 接続ヘルスチェックの実行
        Task<HealthCheckResultDto> CheckTenantHealthAsync(
            string tenantId
        );

        // テナントの現在のステータス取得
        // active / suspended
        Task<string?> GetTenantStatusAsync(
            string tenantId
        );

        // テナントのステータス変更
        // active / suspended
        Task<bool> UpdateTenantStatusAsync(
            string tenantId,
            string status
        );

        // API Key 管理
        Task<string> GenerateApiKeyAsync(
            string tenantId
        );

        // 認証モード管理
        Task<bool> UpdateAuthModeAsync(
            string tenantId,
            int authMode
        );

        // ユーザー管理
        Task<IEnumerable<UserInfo>> GetUsersAsync();

        Task<bool> RegisterUserAsync(
            string loginId,
            string password,
            string tenantId
        );

        Task<bool> DeleteUserAsync(
            string userId
        );
    }
}