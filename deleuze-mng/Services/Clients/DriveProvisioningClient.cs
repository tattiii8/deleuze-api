using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
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

        public async Task MigrateTenantAsync(string tenantId)
        {
            var response = await _httpClient.PostAsync($"internal/tenants/{tenantId}/migrate", null);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Drive サービスのマイグレーションに失敗しました ({response.StatusCode}): {error}");
            }
        }

        // 💡 追加: マイグレーション履歴を取得
        public async Task<List<MigrationHistoryDto>> GetTenantMigrationsAsync(string tenantId)
        {
            var response = await _httpClient.GetAsync($"internal/tenants/{tenantId}/migrations");

            if (!response.IsSuccessStatusCode)
            {
                return new List<MigrationHistoryDto>();
            }

            return await response.Content.ReadFromJsonAsync<List<MigrationHistoryDto>>() ?? new List<MigrationHistoryDto>();
        }

        // 💡 追加: 接続ヘルスチェックを実行
        public async Task<HealthCheckResultDto> CheckTenantHealthAsync(string tenantId)
        {
            var response = await _httpClient.GetAsync($"internal/tenants/{tenantId}/health");

            if (!response.IsSuccessStatusCode)
            {
                return new HealthCheckResultDto 
                { 
                    DbStatus = "Unreachable", 
                    StorageStatus = "Unreachable", 
                    Message = $"HTTP Error: {response.StatusCode}" 
                };
            }

            return await response.Content.ReadFromJsonAsync<HealthCheckResultDto>() 
                   ?? new HealthCheckResultDto();
        }
    }

    // 📌 クライアント内で使用する DTO 定義
    public class MigrationHistoryDto
    {
        public string MigrationName { get; set; } = string.Empty;
        public DateTimeOffset AppliedAt { get; set; }
    }

    public class HealthCheckResultDto
    {
        public string DbStatus { get; set; } = "Unknown";
        public string StorageStatus { get; set; } = "Unknown";
        public string Message { get; set; } = string.Empty;
    }
}