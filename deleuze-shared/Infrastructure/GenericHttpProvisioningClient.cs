using System;
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
            // BaseAddress の末尾スラッシュを補正
            var formattedBaseUrl = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
            _httpClient.BaseAddress = new Uri(formattedBaseUrl);
            _serviceName = serviceName;
        }

        public async Task ProvisionTenantAsync(string tenantId)
        {
            // 💡 先頭スラッシュを削除
            await _httpClient.PostAsync($"internal/tenants/{tenantId}", null);
        }

        public async Task DeprovisionTenantAsync(string tenantId)
        {
            // 💡 先頭スラッシュを削除
            await _httpClient.DeleteAsync($"internal/tenants/{tenantId}");
        }

        public async Task MigrateTenantAsync(string tenantId)
        {
            // 💡 先頭スラッシュを削除
            await _httpClient.PostAsync($"internal/tenants/{tenantId}/migrate", null);
        }
    }
}