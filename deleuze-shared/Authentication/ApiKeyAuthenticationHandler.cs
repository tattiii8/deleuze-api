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
                return AuthenticateResult.NoResult();
            }

            var apiKey = apiKeyHeaderValues.ToString();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return AuthenticateResult.NoResult();
            }

            try
            {
                // 1. ApiKey を PascalCase で送信 (deleuze-auth の ValidateApiKeyRequest.ApiKey に合わせる)
                var response = await _httpClient.PostAsJsonAsync("internal/apikey", new { ApiKey = apiKey });

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogWarning("[ApiKeyHandler] AuthService returned status code: {StatusCode}", response.StatusCode);
                    return AuthenticateResult.Fail("Invalid API Key.");
                }

                var responseBody = await response.Content.ReadAsStringAsync();

                // 2. 大文字小文字を無視してデシリアライズ
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<ApiKeyValidationResponse>(responseBody, options);

                if (result == null || string.IsNullOrEmpty(result.TenantId))
                {
                    Logger.LogWarning("[ApiKeyHandler] ApiKey validation failed or TenantId is null.");
                    return AuthenticateResult.Fail("Invalid API Key.");
                }

                var claims = new[]
                {
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
                Logger.LogError(ex, "[ApiKeyHandler] Exception occurred while validating ApiKey with AuthService.");
                return AuthenticateResult.Fail("Authentication error.");
            }
        }

        private class ApiKeyValidationResponse
        {
            [JsonPropertyName("tenantId")]
            public string? TenantId { get; set; }
        }
    }
}