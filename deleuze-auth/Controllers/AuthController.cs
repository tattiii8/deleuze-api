// deleuze-auth/Controllers/AuthController.cs
using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DeleuzeAuth.Services;

namespace DeleuzeAuth.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("")]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly TokenGenerator _tokenGenerator;

        public AuthController(IUserService userService, TokenGenerator tokenGenerator)
        {
            _userService = userService;
            _tokenGenerator = tokenGenerator;
        }

        [HttpGet(".well-known/openid-configuration")]
        public IActionResult GetOpenIdConfiguration()
        {
            var externalUrl = (Environment.GetEnvironmentVariable("AUTH_EXTERNAL_URL") ?? "https://deleuze.lesure.net/api/auth").TrimEnd('/');
            return Ok(new
            {
                issuer = externalUrl,
                token_endpoint = $"{externalUrl}/connect/token",
                jwks_uri = $"{externalUrl}/.well-known/jwks",
                id_token_signing_alg_values_supported = new[] { "RS256" }
            });
        }

        [HttpGet(".well-known/jwks")]
        public IActionResult GetJwks()
        {
            return Ok(_tokenGenerator.GetJwks());
        }

        [HttpPost("connect/token")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> ConnectToken([FromForm] TokenRequest request)
        {
            if (string.IsNullOrEmpty(request.user_id) || string.IsNullOrEmpty(request.password))
            {
                return BadRequest(new { error = "invalid_request", message = "IDとパスワードを指定してください。" });
            }

            var tenantId = await _userService.AuthenticateAndGetTenantAsync(request.user_id, request.password);
            if (tenantId == null)
            {
                return BadRequest(new { error = "invalid_grant", message = "認証に失敗しました。" });
            }

            var token = _tokenGenerator.GenerateJwt(request.user_id, tenantId);

            return Ok(new
            {
                access_token = token,
                token_type = "Bearer",
                expires_in = 7200
            });
        }
    }

    public class TokenRequest
    {
        public string user_id { get; set; } = string.Empty;
        public string password { get; set; } = string.Empty;
    }
}