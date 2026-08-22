using System.Collections.Generic;
using System.Threading.Tasks;

namespace DeleuzeMng.Services
{
    public record TenantInfo(string TenantId, List<string> Services, int AuthMode = 0, string? ApiKey = null);

    public interface ITenantManagementService
    {
        Task<IEnumerable<TenantInfo>> GetTenantsAsync();
        Task<bool> CreateTenantAsync(string tenantId);
        Task<bool> EnableServiceAsync(string tenantId, string serviceKey);
        Task<string> GenerateApiKeyAsync(string tenantId);
        Task<bool> UpdateAuthModeAsync(string tenantId, int authMode);
    }
}   