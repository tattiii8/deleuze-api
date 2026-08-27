using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DeleuzeAuth.Services;
using DeleuzeAuth.Models;
using Deleuze.Shared.Constants;

namespace DeleuzeAuth.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route(ApiRoutes.Auth.Base)] // <- ここを修正
    public class OidcController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly TokenGenerator _tokenGenerator;

        public OidcController(IUserService userService, TokenGenerator tokenGenerator)
        {
            _userService = userService;
            _tokenGenerator = tokenGenerator;
        }

        [HttpGet(ApiRoutes.Auth.OpenIdConfig)]
        public IActionResult GetOpenIdConfiguration()
        {
            var externalUrl = (Environment.GetEnvironmentVariable("AUTH_EXTERNAL_URL") ?? "https://deleuze.lesure.net/api/auth").TrimEnd('/');
            return Ok(new
            {
                issuer = externalUrl,
                token_endpoint = $"{externalUrl}/connect/token",
                jwks_uri = $"{externalUrl}/.well-known/jwks",
                grant_types_supported = new[] { "password", "client_credentials" },
                token_endpoint_auth_methods_supported = new[] { "client_secret_post" },
                id_token_signing_alg_values_supported = new[] { "RS256" }
            });
        }

        [HttpGet(ApiRoutes.Auth.Jwks)]
        public IActionResult GetJwks()
        {
            return Ok(_tokenGenerator.GetJwks());
        }
    }

}