using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DeleuzeAuth.Services;
using Deleuze.Shared.Constants;

namespace DeleuzeAuth.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route(ApiRoutes.Auth.Base)]
    public class OidcController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly TokenGenerator _tokenGenerator;

        public OidcController(
            IUserService userService,
            TokenGenerator tokenGenerator)
        {
            _userService = userService;
            _tokenGenerator = tokenGenerator;
        }

        // ==========================================================
        // OpenID Connect Configuration
        // ==========================================================

        [HttpGet(ApiRoutes.Auth.OpenIdConfig)]
        public IActionResult GetOpenIdConfiguration()
        {
            var externalUrl =
                (Environment.GetEnvironmentVariable("AUTH_EXTERNAL_URL")
                 ?? "https://deleuze.lesure.net/api/auth")
                .TrimEnd('/');

            return Ok(new
            {
                issuer = externalUrl,
                token_endpoint = $"{externalUrl}/connect/token",
                jwks_uri = $"{externalUrl}/.well-known/jwks",
                id_token_signing_alg_values_supported =
                    new[] { "RS256" }
            });
        }

        // ==========================================================
        // JWKS
        // ==========================================================

        [HttpGet(ApiRoutes.Auth.Jwks)]
        public IActionResult GetJwks()
        {
            return Ok(_tokenGenerator.GetJwks());
        }

        // ==========================================================
        // Token Endpoint
        // ==========================================================

        [HttpPost(ApiRoutes.Auth.Token)]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> ConnectToken(
            [FromForm] TokenRequest request)
        {
            // ------------------------------------------------------
            // 1. 必須パラメータチェック
            // ------------------------------------------------------

            if (string.IsNullOrWhiteSpace(request.tenant_id) ||
                string.IsNullOrWhiteSpace(request.user_id) ||
                string.IsNullOrWhiteSpace(request.password))
            {
                return BadRequest(new
                {
                    error = "invalid_request",
                    message =
                        "tenant_id、user_id、passwordを指定してください。"
                });
            }

            // ------------------------------------------------------
            // 2. tenant_id + user_id + password で認証
            //
            // UserService側で
            // auth_{tenantId} Schemaを使用する。
            // ------------------------------------------------------

            var authenticated =
                await _userService.AuthenticateAsync(
                    request.tenant_id,
                    request.user_id,
                    request.password);

            if (!authenticated)
            {
                return BadRequest(new
                {
                    error = "invalid_grant",
                    message = "認証に失敗しました。"
                });
            }

            // ------------------------------------------------------
            // 3. tenant_id をJWTへ格納
            // ------------------------------------------------------

            var token =
                _tokenGenerator.GenerateJwt(
                    request.user_id,
                    request.tenant_id);

            // ------------------------------------------------------
            // 4. Access Token返却
            // ------------------------------------------------------

            return Ok(new
            {
                access_token = token,
                token_type = "Bearer",
                expires_in = 7200
            });
        }
    }

    // ==============================================================
    // Token Request
    // ==============================================================

    public class TokenRequest
    {
        public string tenant_id { get; set; } = string.Empty;

        public string user_id { get; set; } = string.Empty;

        public string password { get; set; } = string.Empty;
    }
}