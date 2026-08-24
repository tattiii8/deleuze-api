using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Data;
using DeleuzeAuth.Models;
using Deleuze.Shared.Constants;

namespace DeleuzeAuth.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route(ApiRoutes.Auth.InternalBase)] // -> "api/auth/internal"
    public class InternalAuthController : ControllerBase
    {
        private readonly AuthDbContext _dbContext;

        public InternalAuthController(AuthDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// API Key の有効性とテナントの認証モードを検証します
        /// </summary>
        [HttpPost("apikey")] // -> /api/auth/internal/apikey
        public async Task<IActionResult> ValidateApiKey([FromBody] ValidateApiKeyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return BadRequest(new { error = "API Key が指定されていません。" });
            }

            var tenant = await _dbContext.Tenants
                .FirstOrDefaultAsync(t => t.ApiKey == request.ApiKey);

            if (tenant == null)
            {
                return Unauthorized(new { error = "無効な API Key です。" });
            }

            if (tenant.AuthMode == AuthMode.JwtOnly)
            {
                return Unauthorized(new { error = "このテナントでは API Key 認証が許可されていません。" });
            }

            return Ok(new ValidateApiKeyResponse
            {
                TenantId = tenant.Id,
                AuthMode = tenant.AuthMode.ToString()
            });
        }
    }

    public class ValidateApiKeyRequest
    {
        public string ApiKey { get; set; } = string.Empty;
    }

    public class ValidateApiKeyResponse
    {
        public string TenantId { get; set; } = string.Empty;
        public string AuthMode { get; set; } = string.Empty;
    }
}