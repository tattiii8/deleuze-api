using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Deleuze.Shared.Constants;
using DeleuzeAuth.Data;
using DeleuzeAuth.Models;
using DeleuzeAuth.Services;

namespace DeleuzeAuth.Controllers
{
    /// <summary>
    /// OAuth 風アクセストークン発行
    ///
    /// POST /api/auth/connect/token
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route(ApiRoutes.Auth.Base)]
    public class TokenController : ControllerBase
    {
        private const string GrantPassword = "password";
        private const string GrantClientCredentials = "client_credentials";

        private readonly AuthDbContext _dbContext;
        private readonly TokenGenerator _tokenGenerator;

        public TokenController(
            AuthDbContext dbContext,
            TokenGenerator tokenGenerator)
        {
            _dbContext = dbContext;
            _tokenGenerator = tokenGenerator;
        }

        /// <summary>
        /// アクセストークンを発行します。
        /// grant_type=password または client_credentials。
        /// application/json と application/x-www-form-urlencoded の両方を受け付けます。
        /// </summary>
        [HttpPost("connect/token")]
        [Consumes("application/json", "application/x-www-form-urlencoded")]
        public async Task<IActionResult> IssueToken()
        {
            var request = await ReadRequestAsync();

            if (request == null)
            {
                return OAuthError(
                    StatusCodes.Status400BadRequest,
                    "invalid_request",
                    "リクエストを解釈できませんでした。");
            }

            var grantType = ResolveGrantType(request);

            if (string.IsNullOrWhiteSpace(grantType))
            {
                return OAuthError(
                    StatusCodes.Status400BadRequest,
                    "invalid_request",
                    "grant_type は必須です。");
            }

            if (string.Equals(grantType, GrantPassword, StringComparison.OrdinalIgnoreCase))
            {
                return await IssuePasswordTokenAsync(request);
            }

            if (string.Equals(grantType, GrantClientCredentials, StringComparison.OrdinalIgnoreCase))
            {
                return await IssueClientCredentialsTokenAsync(request);
            }

            return OAuthError(
                StatusCodes.Status400BadRequest,
                "unsupported_grant_type",
                "サポートされていない grant_type です。");
        }

        private async Task<IActionResult> IssuePasswordTokenAsync(
            IssueTokenRequest request)
        {
            var tenantId = request.TenantId?.Trim();
            var loginId = request.ResolvedUsername?.Trim();
            var password = request.Password;

            if (string.IsNullOrWhiteSpace(tenantId) ||
                string.IsNullOrWhiteSpace(loginId) ||
                string.IsNullOrWhiteSpace(password))
            {
                return OAuthError(
                    StatusCodes.Status400BadRequest,
                    "invalid_request",
                    "tenant_id, username, password は必須です。");
            }

            var tenantExists = await _dbContext.Tenants
                .AsNoTracking()
                .AnyAsync(t => t.TenantId == tenantId);

            if (!tenantExists)
            {
                return InvalidCredentials();
            }

            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.TenantId == tenantId &&
                    u.LoginId == loginId);

            if (user == null)
            {
                return InvalidCredentials();
            }

            bool passwordValid;

            try
            {
                passwordValid = BCrypt.Net.BCrypt.Verify(
                    password,
                    user.PasswordHash);
            }
            catch
            {
                passwordValid = false;
            }

            if (!passwordValid)
            {
                return InvalidCredentials();
            }

            var token = _tokenGenerator.GenerateUserToken(
                user.SubjectId,
                user.TenantId,
                user.LoginId);

            return Ok(ToResponse(token));
        }

        private async Task<IActionResult> IssueClientCredentialsTokenAsync(
            IssueTokenRequest request)
        {
            var clientSecret = request.ClientSecret?.Trim();

            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                return OAuthError(
                    StatusCodes.Status400BadRequest,
                    "invalid_request",
                    "client_secret は必須です。");
            }

            var keyHash = ApiKeyHasher.Hash(clientSecret);

            var apiKey = await _dbContext.ApiKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.KeyHash == keyHash);

            var now = DateTimeOffset.UtcNow;

            if (apiKey == null ||
                apiKey.RevokedAt != null ||
                (apiKey.ExpiresAt != null && apiKey.ExpiresAt <= now))
            {
                return OAuthError(
                    StatusCodes.Status401Unauthorized,
                    "invalid_client",
                    "client 認証に失敗しました。");
            }

            if (!string.IsNullOrWhiteSpace(request.ClientId))
            {
                var clientId = request.ClientId.Trim();
                var matchesTenant = string.Equals(
                    clientId,
                    apiKey.TenantId,
                    StringComparison.Ordinal);
                var matchesKeyId =
                    Guid.TryParse(clientId, out var keyId) &&
                    keyId == apiKey.Id;

                if (!matchesTenant && !matchesKeyId)
                {
                    return OAuthError(
                        StatusCodes.Status401Unauthorized,
                        "invalid_client",
                        "client 認証に失敗しました。");
                }
            }

            var token = _tokenGenerator.GenerateApiToken(
                apiKey.SubjectId,
                apiKey.TenantId,
                apiKey.Id);

            return Ok(ToResponse(token));
        }

        private async Task<IssueTokenRequest?> ReadRequestAsync()
        {
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                return IssueTokenRequest.FromForm(form);
            }

            try
            {
                using var document = await JsonDocument.ParseAsync(Request.Body);
                return IssueTokenRequest.FromJson(document.RootElement);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? ResolveGrantType(IssueTokenRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.GrantType))
            {
                return request.GrantType.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.ResolvedUsername) &&
                !string.IsNullOrWhiteSpace(request.Password))
            {
                return GrantPassword;
            }

            if (!string.IsNullOrWhiteSpace(request.ClientSecret))
            {
                return GrantClientCredentials;
            }

            return null;
        }

        private static TokenResponse ToResponse(AccessTokenResult token)
        {
            return new TokenResponse
            {
                AccessToken = token.AccessToken,
                TokenType = "Bearer",
                ExpiresIn = token.ExpiresIn
            };
        }

        private IActionResult InvalidCredentials()
        {
            return OAuthError(
                StatusCodes.Status401Unauthorized,
                "invalid_grant",
                "テナントID、ログインID、またはパスワードが正しくありません。");
        }

        private IActionResult OAuthError(
            int statusCode,
            string error,
            string description)
        {
            return StatusCode(statusCode, new
            {
                error,
                error_description = description
            });
        }
    }
}
