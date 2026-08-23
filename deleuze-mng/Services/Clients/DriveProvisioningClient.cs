using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using DeleuzeMng.Services.Infrastructure;

namespace DeleuzeMng.Services.Clients
{
    public class DriveProvisioningClient : IServiceProvisioningClient
    {
        private readonly HttpClient _httpClient;

        public string ServiceKey => "drive";

        public DriveProvisioningClient(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            
            var baseUrl = configuration["Services:Drive:InternalApiUrl"]
                ?? throw new InvalidOperationException("設定 'Services:Drive:InternalApiUrl' が未設定です。");

            _httpClient.BaseAddress = new Uri(baseUrl);
        }

        public async Task InitializeTenantAsync(string tenantId)
        {
            var response = await _httpClient.PostAsync($"internal/tenants/{tenantId}/initialize", null);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Drive サービスのプロビジョニングに失敗しました ({response.StatusCode}): {error}");
            }
        }

        public async Task RollbackTenantAsync(string tenantId)
        {
            var response = await _httpClient.DeleteAsync($"internal/tenants/{tenantId}");

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Drive サービスのデプロビジョニング（削除）に失敗しました ({response.StatusCode}): {error}");
            }
        }
    }
}