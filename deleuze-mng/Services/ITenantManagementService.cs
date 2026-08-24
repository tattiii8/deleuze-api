using System.Collections.Generic;
using System.Threading.Tasks;

namespace DeleuzeMng.Services
{
    public record TenantInfo(string TenantId, List<string> Services, int AuthMode = 0, string? ApiKey = null);
    public record UserInfo(string Id, string LoginId, string TenantId, string CreatedAt);

    // 💡 コントローラーで利用する DTO の定義（必要に応じて別ファイルでも可）
    public record MigrationHistoryDto(string MigrationName, string AppliedAt);
    public record HealthCheckResultDto(string DbStatus, string StorageStatus, string Message);

    // 💡 追加: マイグレーション結果（失敗サービス一覧を含む）
    public class TenantMigrationResult
    {
        public bool Success => FailedServices.Count == 0;
        public List<string> FailedServices { get; } = new();
    }

    public interface ITenantManagementService
    {
        Task<IEnumerable<TenantInfo>> GetTenantsAsync();
        
        Task<TenantInfo?> GetTenantByIdAsync(string tenantId);
        
        Task<bool> CreateTenantAsync(string tenantId, string name = "");
        Task<bool> DeleteTenantAsync(string tenantId);
        Task<bool> EnableServiceForTenantAsync(string tenantId, string serviceKey);
        Task<bool> DisableServiceForTenantAsync(string tenantId, string serviceKey);

        // 全サービスに対するマイグレーション実行（失敗したサービス名を含む結果を返す）
        Task<TenantMigrationResult> MigrateAllServicesForTenantAsync(string tenantId);

        Task<bool> MigrateServiceForTenantAsync(string tenantId, string serviceKey);

        // 💡 追加: マイグレーション履歴の取得
        Task<IEnumerable<MigrationHistoryDto>> GetTenantMigrationsAsync(string tenantId);

        // 💡 追加: 接続ヘルスチェックの実行
        Task<HealthCheckResultDto> CheckTenantHealthAsync(string tenantId);

        // 💡 追加: テナントのステータス変更 (一時停止/有効化)
        Task<bool> UpdateTenantStatusAsync(string tenantId, string status);

        Task<string> GenerateApiKeyAsync(string tenantId);
        Task<bool> UpdateAuthModeAsync(string tenantId, int authMode);

        // ユーザー管理機能
        Task<IEnumerable<UserInfo>> GetUsersAsync();
        Task<bool> RegisterUserAsync(string loginId, string password, string tenantId);
        Task<bool> DeleteUserAsync(string userId);
    }
}