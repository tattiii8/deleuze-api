// deleuze-drive/Authentication/ApiKeyAuthenticationHandler.cs
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeleuzeDrive.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";
    public string HeaderName { get; set; } = "X-Api-Key";
}

public class ValidateApiKeyResponse
{
    public string TenantId { get; set; } = string.Empty;
    public string AuthMode { get; set; } = string.Empty;
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IHttpClientFactory httpClientFactory)
        : base(options, logger, encoder, clock)
    {
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var apiKeyHeaderValues))
        {
            return AuthenticateResult.Fail("API Key Header Missing");
        }

        var apiKey = apiKeyHeaderValues.FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
        {
            return AuthenticateResult.Fail("API Key Missing");
        }

        var client = _httpClientFactory.CreateClient("AuthService");
        var requestBody = new { apiKey = apiKey };

        // 相対パス "internal/apikey" で指定（BaseAddress の末尾に結合される）
        var response = await client.PostAsJsonAsync("internal/apikey", requestBody);

        if (!response.IsSuccessStatusCode)
        {
            return AuthenticateResult.Fail("Invalid API Key or Unauthorized AuthMode");
        }

        var authResult = await response.Content.ReadFromJsonAsync<ValidateApiKeyResponse>();
        if (authResult == null || string.IsNullOrEmpty(authResult.TenantId))
        {
            return AuthenticateResult.Fail("Invalid tenant response from auth service");
        }

        var claims = new[]
        {
            new Claim("TenantId", authResult.TenantId),
            new Claim("AuthMode", authResult.AuthMode),
            new Claim(ClaimTypes.Name, $"ApiKeyUser:{authResult.TenantId}")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}