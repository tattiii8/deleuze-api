// deleuze-auth/Controllers/InternalAuthController.cs
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Data;
using DeleuzeAuth.Models;

namespace DeleuzeAuth.Controllers
{
    [ApiController]
    [AllowAnonymous] // サービス間内部通信のため AllowAnonymous（ネットワーク層や認証で保護）
    [Route("internal/auth")]
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
        [HttpPost("validate-key")]
        public async Task<IActionResult> ValidateApiKey([FromBody] ValidateApiKeyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return BadRequest(new { error = "API Key が指定されていません。" });
            }

            // DB から API Key に該当するテナントを検索
            var tenant = await _dbContext.Tenants
                .FirstOrDefaultAsync(t => t.ApiKey == request.ApiKey);

            if (tenant == null)
            {
                return Unauthorized(new { error = "無効な API Key です。" });
            }

            // AuthMode が JwtOnly の場合は API Key でのアクセスを拒否
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