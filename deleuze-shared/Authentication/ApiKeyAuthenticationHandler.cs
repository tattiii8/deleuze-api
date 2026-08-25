// deleuze-shared/Authentication/ApiKeyHandler.cs

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

            const string relativePath = "internal/apikey";

            var requestUrl = new Uri(_httpClient.BaseAddress!, relativePath);
            Logger.LogInformation("[ApiKeyHandler] Attempting ApiKey validation against AuthService URL: {RequestUrl}", requestUrl);

            try
            {
                var requestPayload = new { ApiKey = apiKey };
                var response = await _httpClient.PostAsJsonAsync(relativePath, requestPayload);

                var responseBody = await response.Content.ReadAsStringAsync();
                Logger.LogDebug("[ApiKeyHandler] AuthService Response Status: {StatusCode}, Body: {ResponseBody}",
                    (int)response.StatusCode, responseBody);

                if (!response.IsSuccessStatusCode)
                {
                    return AuthenticateResult.Fail($"AuthService validation failed with HTTP {response.StatusCode}.");
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<ApiKeyValidationResponse>(responseBody, options);

                if (result == null || string.IsNullOrEmpty(result.TenantId))
                {
                    return AuthenticateResult.Fail("Invalid API Key or missing TenantId.");
                }

                Logger.LogInformation("[ApiKeyHandler] Successfully authenticated ApiKey for TenantId: {TenantId}", result.TenantId);

                var claims = new[]
                {
                    new Claim(JwtTenantProvider.TenantClaimType, result.TenantId)
                };

                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                return AuthenticateResult.Success(ticket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[ApiKeyHandler] Exception occurred while communicating with AuthService");
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