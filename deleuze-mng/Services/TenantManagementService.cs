using System.Collections.Generic;
using System.Threading.Tasks;
using DeleuzeMng.Models; // AuthMode enum の定義場所に応じて調整してください

namespace DeleuzeMng.Services
{
    public record TenantInfo(string TenantId, List<string> Services, int AuthMode = 0, string? ApiKey = null);
    public record UserInfo(string Id, string LoginId, string TenantId, string CreatedAt);

    public interface ITenantManagementService
    {
        Task<IEnumerable<TenantInfo>> GetTenantsAsync();
        Task<bool> CreateTenantAsync(string tenantId, string name = "");
        Task<bool> DeleteTenantAsync(string tenantId);
        Task<bool> EnableServiceForTenantAsync(string tenantId, string serviceKey);
        Task<string> GenerateApiKeyAsync(string tenantId);
        Task<bool> UpdateAuthModeAsync(string tenantId, int authMode);

        // ユーザー管理用
        Task<IEnumerable<UserInfo>> GetUsersAsync();
        Task<bool> RegisterUserAsync(string loginId, string password, string tenantId);
        Task<bool> DeleteUserAsync(string userId);
    }
}