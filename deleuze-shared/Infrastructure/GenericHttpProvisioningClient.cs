using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Deleuze.Shared.Infrastructure
{
    public class GenericHttpProvisioningClient : IServiceProvisioningClient
    {
        private readonly HttpClient _httpClient;

        // インターフェースの要件を満たすために ServiceKey を追加
        public string ServiceKey { get; }

        // 既存のコードとの互換性を保つための Alias
        public string ServiceName => ServiceKey;

        public GenericHttpProvisioningClient(
            HttpClient httpClient,
            string serviceName,
            string baseUrl,
            string internalBasePath)
        {
            _httpClient = httpClient;

            var formattedBaseUrl = baseUrl.EndsWith("/")
                ? baseUrl
                : baseUrl + "/";

            var formattedInternalBasePath = internalBasePath.Trim('/');

            _httpClient.BaseAddress = new Uri(
                $"{formattedBaseUrl}{formattedInternalBasePath}/");

            ServiceKey = serviceName;
        }

        public async Task ProvisionTenantAsync(string tenantId)
        {
            var response = await _httpClient.PostAsync(
                $"tenants/{tenantId}",
                null);

            response.EnsureSuccessStatusCode();
        }

        public async Task DeprovisionTenantAsync(string tenantId)
        {
            var response = await _httpClient.DeleteAsync(
                $"tenants/{tenantId}");

            response.EnsureSuccessStatusCode();
        }

        public async Task MigrateTenantAsync(string tenantId)
        {
            var response = await _httpClient.PostAsync(
                $"tenants/{tenantId}/migrate",
                null);

            response.EnsureSuccessStatusCode();
        }
    }
}