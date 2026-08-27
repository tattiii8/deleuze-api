using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using DeleuzeAuth.Data;
using DeleuzeAuth.Models;
using Deleuze.Shared.Constants;

namespace DeleuzeAuth.Controllers
{
    /// <summary>
    /// API Key管理API
    ///
    /// POST   /api/auth/apikeys
    /// GET    /api/auth/apikeys
    /// DELETE /api/auth/apikeys/{id}
    /// </summary>
    [ApiController]
    [Authorize]
    [Route(ApiRoutes.Auth.InternalBase)]
    public class ApiKeyController : ControllerBase
    {
        private readonly AuthDbContext _dbContext;

        public ApiKeyController(AuthDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// API Keyを発行します。
        /// </summary>
        [HttpPost("apikey")]
        public async Task<IActionResult> CreateApiKey(
            [FromBody] CreateApiKeyRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new
                {
                    error = "InvalidRequest",
                    message = "name は必須です。"
                });
            }

            // ==========================================
            // JWTからユーザー情報を取得
            // ==========================================
            var subjectId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            // JwtRegisteredClaimNames.Sub が
            // ClaimTypes.NameIdentifier にマッピングされない
            // 場合への対応
            if (string.IsNullOrWhiteSpace(subjectId))
            {
                subjectId = User.FindFirstValue("sub");
            }

            var tenantId = User.FindFirstValue("tenant_id");

            if (string.IsNullOrWhiteSpace(subjectId) ||
                string.IsNullOrWhiteSpace(tenantId))
            {
                return Unauthorized(new
                {
                    error = "InvalidToken",
                    message = "JWTに必要なユーザー情報がありません。"
                });
            }

            // ==========================================
            // ユーザー存在確認
            // ==========================================
            var userExists = await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(u =>
                    u.SubjectId == subjectId &&
                    u.TenantId == tenantId);

            if (!userExists)
            {
                return Unauthorized(new
                {
                    error = "InvalidToken",
                    message = "ユーザーが存在しません。"
                });
            }

            // ==========================================
            // API Key生成
            // ==========================================
            var apiKey = GenerateApiKey();

            // DBにはハッシュのみ保存
            var keyHash = HashApiKey(apiKey);

            var entity = new ApiKey
            {
                Id = Guid.NewGuid(),
                SubjectId = subjectId,
                TenantId = tenantId,
                KeyHash = keyHash,
                Name = request.Name,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = request.ExpiresAt,
                RevokedAt = null
            };

            _dbContext.ApiKeys.Add(entity);

            await _dbContext.SaveChangesAsync();

            // ==========================================
            // API Keyは発行時のみ平文を返す
            // ==========================================
            return Ok(new
            {
                id = entity.Id,
                name = entity.Name,
                apiKey,
                createdAt = entity.CreatedAt,
                expiresAt = entity.ExpiresAt
            });
        }



        /// <summary>
        /// 管理コンソールからAPI Keyを発行します。
        /// JWTは不要です。
        ///
        /// POST /api/auth/admin/apikey
        /// </summary>
        [AllowAnonymous]
        [HttpPost("admin/apikey")]
        public async Task<IActionResult> CreateAdminApiKey(
            [FromBody] AdminCreateApiKeyRequest request)
        {
            // ==========================================
            // 1. リクエストチェック
            // ==========================================
            if (request == null ||
                string.IsNullOrWhiteSpace(request.TenantId) ||
                string.IsNullOrWhiteSpace(request.LoginId) ||
                string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new
                {
                    error = "InvalidRequest",
                    message = "tenantId, loginId, name は必須です。"
                });
            }

            // ==========================================
            // 2. TenantId + LoginId からユーザー取得
            // ==========================================
            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.TenantId == request.TenantId &&
                    u.LoginId == request.LoginId);

            if (user == null)
            {
                return NotFound(new
                {
                    error = "UserNotFound",
                    message = "指定されたユーザーが存在しません。"
                });
            }

            // ==========================================
            // 3. API Key生成
            // ==========================================
            var apiKey = GenerateApiKey();

            // DBにはハッシュのみ保存
            var keyHash = HashApiKey(apiKey);

            // ==========================================
            // 4. API Key保存
            // ==========================================
            var entity = new ApiKey
            {
                Id = Guid.NewGuid(),

                // DBから取得したSubjectIdを使用
                SubjectId = user.SubjectId,

                // DBから取得したTenantIdを使用
                TenantId = user.TenantId,

                KeyHash = keyHash,
                Name = request.Name,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = request.ExpiresAt,
                RevokedAt = null
            };

            _dbContext.ApiKeys.Add(entity);

            await _dbContext.SaveChangesAsync();

            // ==========================================
            // 5. API Keyは発行時のみ平文を返す
            // ==========================================
            return Ok(new
            {
                id = entity.Id,
                name = entity.Name,
                apiKey,
                createdAt = entity.CreatedAt,
                expiresAt = entity.ExpiresAt
            });
        }

　　　　　/// <summary>
        /// 管理コンソールから指定されたユーザー（loginId）の API Key 一覧を取得します。
        /// JWTは不要です。
        ///
        /// GET /api/auth/admin/apikey/{loginId}?tenantId={tenantId}
        /// </summary>
        [AllowAnonymous]
        [HttpGet("admin/apikey/{loginId}")]
        public async Task<IActionResult> GetAdminApiKeysByLoginId(
            [FromRoute] string loginId,
            [FromQuery] string tenantId)
        {
            // ==========================================
            // 1. リクエストチェック
            // ==========================================
            if (string.IsNullOrWhiteSpace(loginId) ||
                string.IsNullOrWhiteSpace(tenantId))
            {
                return BadRequest(new
                {
                    error = "InvalidRequest",
                    message = "loginId と tenantId は必須です。"
                });
            }

            // ==========================================
            // 2. TenantId + LoginId からユーザー取得
            // ==========================================
            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.TenantId == tenantId &&
                    u.LoginId == loginId);

            if (user == null)
            {
                return NotFound(new
                {
                    error = "UserNotFound",
                    message = "指定されたユーザーが存在しません。"
                });
            }

            // ==========================================
            // 3. API Key 一覧取得
            // ==========================================
            var apiKeys = await _dbContext.ApiKeys
                .AsNoTracking()
                .Where(k =>
                    k.SubjectId == user.SubjectId &&
                    k.TenantId == user.TenantId)
                .OrderByDescending(k => k.CreatedAt)
                .Select(k => new
                {
                    id = k.Id,
                    name = k.Name,
                    createdAt = k.CreatedAt,
                    expiresAt = k.ExpiresAt,
                    revokedAt = k.RevokedAt,
                    active =
                        k.RevokedAt == null &&
                        (k.ExpiresAt == null ||
                         k.ExpiresAt > DateTimeOffset.UtcNow)
                })
                .ToListAsync();

            return Ok(apiKeys);
        }

        /// <summary>
        /// 管理コンソールから指定した UUID の API Key を削除（失効）します。
        /// JWTは不要です。
        ///
        /// DELETE /api/auth/admin/apikey/{id}
        /// </summary>
        [AllowAnonymous]
        [HttpDelete("admin/apikey/{id:guid}")]
        public async Task<IActionResult> RevokeAdminApiKey(Guid id)
        {
            // ==========================================
            // UUID を指定して API Key を取得
            // ==========================================
            var apiKey = await _dbContext.ApiKeys
                .FirstOrDefaultAsync(k => k.Id == id);

            if (apiKey == null)
            {
                return NotFound(new
                {
                    error = "ApiKeyNotFound",
                    message = "指定されたAPI Keyが存在しません。"
                });
            }

            if (apiKey.RevokedAt != null)
            {
                return Ok(new
                {
                    message = "API Keyは既に失効しています。"
                });
            }

            // 論理削除（失効日時の記録）
            apiKey.RevokedAt = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                message = "API Keyを失効させました。",
                id = apiKey.Id
            });
        }



        /// <summary>
        /// 現在のユーザーが発行したAPI Key一覧を取得します。
        /// </summary>
        /// 
        [HttpGet("apikey")]
        public async Task<IActionResult> GetApiKeys()
        {
            var subjectId = GetSubjectId();
            var tenantId = GetTenantId();

            if (string.IsNullOrWhiteSpace(subjectId) ||
                string.IsNullOrWhiteSpace(tenantId))
            {
                return Unauthorized(new
                {
                    error = "InvalidToken",
                    message = "JWTに必要なユーザー情報がありません。"
                });
            }

            var apiKeys = await _dbContext.ApiKeys
                .AsNoTracking()
                .Where(k =>
                    k.SubjectId == subjectId &&
                    k.TenantId == tenantId)
                .OrderByDescending(k => k.CreatedAt)
                .Select(k => new
                {
                    id = k.Id,
                    name = k.Name,
                    createdAt = k.CreatedAt,
                    expiresAt = k.ExpiresAt,
                    revokedAt = k.RevokedAt,
                    active =
                        k.RevokedAt == null &&
                        (k.ExpiresAt == null ||
                         k.ExpiresAt > DateTimeOffset.UtcNow)
                })
                .ToListAsync();

            return Ok(apiKeys);
        }

        /// <summary>
        /// API Keyを失効させます。
        /// </summary>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> RevokeApiKey(Guid id)
        {
            var subjectId = GetSubjectId();
            var tenantId = GetTenantId();

            if (string.IsNullOrWhiteSpace(subjectId) ||
                string.IsNullOrWhiteSpace(tenantId))
            {
                return Unauthorized(new
                {
                    error = "InvalidToken",
                    message = "JWTに必要なユーザー情報がありません。"
                });
            }

            // 自分自身のAPI Keyだけ操作可能
            var apiKey = await _dbContext.ApiKeys
                .FirstOrDefaultAsync(k =>
                    k.Id == id &&
                    k.SubjectId == subjectId &&
                    k.TenantId == tenantId);

            if (apiKey == null)
            {
                return NotFound(new
                {
                    error = "ApiKeyNotFound",
                    message = "指定されたAPI Keyが存在しません。"
                });
            }

            if (apiKey.RevokedAt != null)
            {
                return Ok(new
                {
                    message = "API Keyは既に失効しています。"
                });
            }

            apiKey.RevokedAt = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                message = "API Keyを失効させました。",
                id = apiKey.Id
            });
        }

        // ==========================================
        // JWTからSubjectIdを取得
        // ==========================================
        private string? GetSubjectId()
        {
            return User.FindFirstValue(
                       ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub");
        }

        // ==========================================
        // JWTからTenantIdを取得
        // ==========================================
        private string? GetTenantId()
        {
            return User.FindFirstValue("tenant_id");
        }

        // ==========================================
        // API Key生成
        // ==========================================
        private static string GenerateApiKey()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);

            return "dk_live_" +
                   Convert.ToBase64String(bytes)
                       .Replace("+", "-")
                       .Replace("/", "_")
                       .Replace("=", "");
        }

        // ==========================================
        // API Keyハッシュ化
        // ==========================================
        private static string HashApiKey(string apiKey)
        {
            var bytes = SHA256.HashData(
                Encoding.UTF8.GetBytes(apiKey));

            return Convert.ToHexString(bytes)
                .ToLowerInvariant();
        }
    }
}