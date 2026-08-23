using System.Net.Http;
using System.Threading.Tasks;

namespace Deleuze.Shared.Infrastructure
{
    public class GenericHttpProvisioningClient : IServiceProvisioningClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _serviceName;

        public GenericHttpProvisioningClient(HttpClient httpClient, string serviceName, string baseUrl)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(baseUrl);
            _serviceName = serviceName;
        }

        public async Task ProvisionTenantAsync(string tenantId)
        {
            await _httpClient.PostAsync($"/internal/tenants/{tenantId}", null);
        }

        public async Task DeprovisionTenantAsync(string tenantId)
        {
            await _httpClient.DeleteAsync($"/internal/tenants/{tenantId}");
        }

        public async Task MigrateTenantAsync(string tenantId)
        {
            // 各サービスのマイグレーション用内部エンドポイントを叩く
            await _httpClient.PostAsync($"/internal/tenants/{tenantId}/migrate", null);
        }
    }
}