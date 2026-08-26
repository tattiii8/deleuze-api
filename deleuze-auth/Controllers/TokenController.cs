using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using DeleuzeAuth.Data;
using DeleuzeAuth.Models;

namespace DeleuzeAuth.Controllers
{
    /// <summary>
    /// API利用者向けアクセストークン発行API
    ///
    /// POST /api/auth/token
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/auth")]
    public class TokenController : ControllerBase
    {
        private readonly AuthDbContext _dbContext;
        private readonly IConfiguration _configuration;

        public TokenController(
            AuthDbContext dbContext,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _configuration = configuration;
        }

        /// <summary>
        /// アクセストークンを発行します。
        /// </summary>
        [HttpPost("/connect/token")]
        public async Task<IActionResult> IssueToken(
            [FromBody] IssueTokenRequest request)
        {
            // ==========================================
            // 1. リクエストチェック
            // ==========================================
            if (request == null ||
                string.IsNullOrWhiteSpace(request.TenantId) ||
                string.IsNullOrWhiteSpace(request.LoginId) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new
                {
                    error = "InvalidRequest",
                    message = "TenantId, LoginId, Password は必須です。"
                });
            }

            // ==========================================
            // 2. テナント存在確認
            // ==========================================
            var tenantExists = await _dbContext.Tenants
                .AsNoTracking()
                .AnyAsync(t => t.TenantId == request.TenantId);

            if (!tenantExists)
            {
                // テナントの存在有無を外部に詳細に知らせない
                return Unauthorized(new
                {
                    error = "InvalidCredentials",
                    message = "テナントID、ログインID、またはパスワードが正しくありません。"
                });
            }

            // ==========================================
            // 3. ユーザー取得
            // ==========================================
            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.TenantId == request.TenantId &&
                    u.LoginId == request.LoginId);

            if (user == null)
            {
                return Unauthorized(new
                {
                    error = "InvalidCredentials",
                    message = "テナントID、ログインID、またはパスワードが正しくありません。"
                });
            }

            // ==========================================
            // 4. パスワード検証
            // ==========================================
            bool passwordValid;

            try
            {
                passwordValid = BCrypt.Net.BCrypt.Verify(
                    request.Password,
                    user.PasswordHash);
            }
            catch
            {
                // 不正なハッシュ等がDBに存在する場合も
                // 認証失敗として扱う
                passwordValid = false;
            }

            if (!passwordValid)
            {
                return Unauthorized(new
                {
                    error = "InvalidCredentials",
                    message = "テナントID、ログインID、またはパスワードが正しくありません。"
                });
            }

            // ==========================================
            // 5. Access Token発行
            // ==========================================
            var accessToken = GenerateAccessToken(user);

            // ==========================================
            // 6. レスポンス
            // ==========================================
            return Ok(new
            {
                accessToken,
                tokenType = "Bearer",
                expiresIn = GetTokenLifetimeSeconds()
            });
        }

        /// <summary>
        /// JWT Access Tokenを生成します。
        /// </summary>
        private string GenerateAccessToken(AuthUser user)
        {
            var key = _configuration["Jwt:Key"];

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    "Jwt:Key が設定されていません。");
            }

            var issuer = _configuration["Jwt:Issuer"];

            var audience = _configuration["Jwt:Audience"];

            var lifetimeMinutes =
                GetTokenLifetimeMinutes();

            var claims = new[]
            {
                // ユーザー識別子
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    user.SubjectId),

                // テナント識別子
                new Claim(
                    "tenant_id",
                    user.TenantId),

                // ログインID
                new Claim(
                    JwtRegisteredClaimNames.UniqueName,
                    user.LoginId)
            };

            var securityKey =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(key));

            var credentials =
                new SigningCredentials(
                    securityKey,
                    SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(
                    lifetimeMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler()
                .WriteToken(token);
        }

        private int GetTokenLifetimeMinutes()
        {
            if (int.TryParse(
                _configuration["Jwt:ExpiresMinutes"],
                out var minutes) &&
                minutes > 0)
            {
                return minutes;
            }

            // 設定がない場合のデフォルト
            return 60;
        }

        private int GetTokenLifetimeSeconds()
        {
            return GetTokenLifetimeMinutes() * 60;
        }
    }
}