using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Deleuze.Shared.Authentication
{
    public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly HttpClient _httpClient;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            IHttpClientFactory httpClientFactory)
            : base(options, logger, encoder, clock)
        {
            _httpClient = httpClientFactory.CreateClient("AuthService");
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeaderValues))
            {
                Logger.LogDebug("[ApiKeyHandler] X-Api-Key header not found in request.");
                return AuthenticateResult.NoResult();
            }

            var apiKey = apiKeyHeaderValues.ToString();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Logger.LogWarning("[ApiKeyHandler] X-Api-Key header is present but empty.");
                return AuthenticateResult.NoResult();
            }

            var requestUrl = $"{_httpClient.BaseAddress}internal/apikey";
            Logger.LogInformation("[ApiKeyHandler] Attempting ApiKey validation against AuthService URL: {RequestUrl}", requestUrl);

            try
            {
                var requestPayload = new { ApiKey = apiKey };
                var response = await _httpClient.PostAsJsonAsync("internal/apikey", requestPayload);

                var responseBody = await response.Content.ReadAsStringAsync();
                Logger.LogInformation("[ApiKeyHandler] AuthService Response Status: {StatusCode}, Body: {ResponseBody}", 
                    (int)response.StatusCode, responseBody);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogWarning("[ApiKeyHandler] AuthService returned failure HTTP status: {StatusCode}", response.StatusCode);
                    return AuthenticateResult.Fail($"AuthService validation failed with HTTP {response.StatusCode}.");
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<ApiKeyValidationResponse>(responseBody, options);

                if (result == null)
                {
                    Logger.LogError("[ApiKeyHandler] Failed to deserialize response body to ApiKeyValidationResponse. Body: {ResponseBody}", responseBody);
                    return AuthenticateResult.Fail("Invalid response payload from AuthService.");
                }

                if (string.IsNullOrEmpty(result.TenantId))
                {
                    Logger.LogWarning("[ApiKeyHandler] ApiKey is invalid or TenantId is null/empty. TenantId: '{TenantId}'", result.TenantId);
                    return AuthenticateResult.Fail("Invalid API Key or missing TenantId.");
                }

                Logger.LogInformation("[ApiKeyHandler] Successfully authenticated ApiKey for TenantId: {TenantId}", result.TenantId);

                // クレーム記法の違いを吸収するため同値の複数パターンを生成
                var claims = new[]
                {
                    new Claim("tenantId", result.TenantId),
                    new Claim("TenantId", result.TenantId),
                    new Claim("tenant_id", result.TenantId),
                    new Claim(ClaimTypes.NameIdentifier, result.TenantId)
                };

                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                return AuthenticateResult.Success(ticket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[ApiKeyHandler] Exception occurred while communicating with AuthService at {RequestUrl}", requestUrl);
                return AuthenticateResult.Fail($"Authentication exception: {ex.Message}");
            }
        }

        private class ApiKeyValidationResponse
        {
            [JsonPropertyName("tenantId")]
            public string? TenantId { get; set; }
        }
    }
}