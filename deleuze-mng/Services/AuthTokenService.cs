using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DeleuzeMng.Services
{
    public interface IAuthTokenService
    {
        Task<string> GetAccessTokenAsync();
    }

    public class AuthTokenService : IAuthTokenService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthTokenService> _logger;

        private string? _cachedToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public AuthTokenService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AuthTokenService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _cachedToken;
            }

            var clientId = _configuration["AUTH_CLIENT_ID"];
            var clientSecret = _configuration["AUTH_CLIENT_SECRET"];

            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                _logger.LogWarning("AUTH_CLIENT_SECRET が設定されていません。");
            }

            var client = _httpClientFactory.CreateClient("AuthApiClient");

            var payload = new
            {
                grant_type = "client_credentials",
                client_id = clientId,
                client_secret = clientSecret
            };

            var response = await client.PostAsJsonAsync("/api/auth/connect/token", payload);

            if (!response.IsSuccessStatusCode)
            {
                // バックアップエンドポイント試行
                response = await client.PostAsJsonAsync("connect/token", payload);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("deleuze-auth からのアクセストークン取得に失敗しました: {Error}", errorContent);
                throw new InvalidOperationException($"deleuze-auth 認証に失敗しました: {errorContent}");
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponseModel>();

            if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                throw new InvalidOperationException("レスポンスからアクセストークンを解析できませんでした。");
            }

            _cachedToken = tokenResponse.AccessToken;
            // 有効期限の30秒前に期限切れとみなす
            var bufferSeconds = 30;
            var expirySeconds = Math.Max(10, tokenResponse.ExpiresIn - bufferSeconds);
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expirySeconds);

            _logger.LogInformation("deleuze-auth からアクセストークンを取得しました (有効期限: {Expiry})", _tokenExpiry);
            return _cachedToken;
        }
    }

    internal class TokenResponseModel
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
