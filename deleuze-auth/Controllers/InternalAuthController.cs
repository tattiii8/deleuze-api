using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Data;
using DeleuzeAuth.Models;
using DeleuzeAuth.Services;
using Deleuze.Shared.Constants;

namespace DeleuzeAuth.Controllers
{
    /// <summary>
    /// Mng等の内部サービスから利用するAuth管理API
    /// </summary>
    [ApiController]
    [Authorize]
    [Route(ApiRoutes.Auth.InternalBase)]
    public class InternalAuthController : ControllerBase
    {
        private readonly AuthDbContext _dbContext;
        private readonly IDbInitializerService _dbInitializer;

        public InternalAuthController(
            AuthDbContext dbContext,
            IDbInitializerService dbInitializer)
        {
            _dbContext = dbContext;
            _dbInitializer = dbInitializer;
        }

        // ==========================================
        // DB初期化
        // ==========================================

        /// <summary>
        /// DBの初期スキーマおよびテーブルを
        /// .sqlファイルから作成します。
        ///
        /// POST /api/auth/internal/init
        /// </summary>
        [AllowAnonymous]
        [HttpPost("init")]
        public async Task<IActionResult> InitializeDatabase()
        {
            try
            {
                await _dbInitializer.ExecuteInitSqlAsync();

                return Ok(new
                {
                    message =
                        "auth スキーマおよび初期テーブルの作成が完了しました。",
                    timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error =
                        "データベース初期化処理に失敗しました。",
                    detail = ex.Message
                });
            }
        }

        // ==========================================
        // ユーザー作成
        // ==========================================

        /// <summary>
        /// 内部認証ユーザー作成
        ///
        /// POST /api/auth/internal/users
        /// </summary>
        [HttpPost("users")]
        public async Task<IActionResult> RegisterUser(
            [FromBody] RegisterAuthUserRequest request)
        {
            // ==========================================
            // 1. リクエストチェック
            // ==========================================

            if (request == null ||
                string.IsNullOrWhiteSpace(request.SubjectId) ||
                string.IsNullOrWhiteSpace(request.TenantId) ||
                string.IsNullOrWhiteSpace(request.LoginId) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new
                {
                    error = "InvalidRequest",
                    message =
                        "SubjectId, TenantId, LoginId, Password は必須です。"
                });
            }

            // ==========================================
            // 2. テナント存在確認
            // ==========================================

            var tenantExists = await _dbContext.Tenants
                .AnyAsync(t =>
                    t.TenantId == request.TenantId);

            if (!tenantExists)
            {
                return NotFound(new
                {
                    error = "TenantNotFound",
                    message =
                        "指定されたテナントが存在しません。",
                    tenantId = request.TenantId
                });
            }

            // ==========================================
            // 3. SubjectId重複確認
            // ==========================================

            var subjectExists = await _dbContext.Users
                .AnyAsync(u =>
                    u.SubjectId == request.SubjectId);

            if (subjectExists)
            {
                return Conflict(new
                {
                    error = "UserAlreadyExists",
                    message =
                        "指定された subject_id は既に使用されています。",
                    subjectId = request.SubjectId
                });
            }

            // ==========================================
            // 4. tenant_id + login_id 重複確認
            // ==========================================

            var loginExists = await _dbContext.Users
                .AnyAsync(u =>
                    u.TenantId == request.TenantId &&
                    u.LoginId == request.LoginId);

            if (loginExists)
            {
                return Conflict(new
                {
                    error = "UserAlreadyExists",
                    message =
                        "指定されたテナントでは、この login_id は既に使用されています。",
                    tenantId = request.TenantId,
                    loginId = request.LoginId
                });
            }

            // ==========================================
            // 5. AuthUser作成
            // ==========================================

            var authUser = new AuthUser
            {
                SubjectId = request.SubjectId,
                TenantId = request.TenantId,
                LoginId = request.LoginId,
                PasswordHash =
                    BCrypt.Net.BCrypt.HashPassword(
                        request.Password),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.Users.Add(authUser);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                return Conflict(new
                {
                    error = "UserAlreadyExists",
                    message =
                        "指定されたユーザー情報は既に使用されています。",
                    detail = ex.InnerException?.Message
                });
            }

            return Created(
                $"/api/auth/internal/users/{authUser.SubjectId}",
                new
                {
                    subjectId = authUser.SubjectId,
                    tenantId = authUser.TenantId,
                    loginId = authUser.LoginId
                });
        }

        // ==========================================
        // ユーザー削除
        // ==========================================

        /// <summary>
        /// 内部認証ユーザー削除
        ///
        /// DELETE /api/auth/internal/users/{subjectId}
        /// </summary>
        [HttpDelete("users/{subjectId}")]
        public async Task<IActionResult> DeleteUser(
            string subjectId)
        {
            // ==========================================
            // 1. リクエストチェック
            // ==========================================

            if (string.IsNullOrWhiteSpace(subjectId))
            {
                return BadRequest(new
                {
                    error = "InvalidRequest",
                    message = "subjectId は必須です。"
                });
            }

            // ==========================================
            // 2. ユーザー取得
            // ==========================================

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u =>
                    u.SubjectId == subjectId);

            if (user == null)
            {
                return NotFound(new
                {
                    error = "UserNotFound",
                    message =
                        "該当する認証ユーザーが存在しません。",
                    subjectId
                });
            }

            // ==========================================
            // 3. 削除
            // ==========================================

            _dbContext.Users.Remove(user);

            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        // ==========================================
        // テナント作成
        // ==========================================

        /// <summary>
        /// 内部認証テナント作成
        ///
        /// POST /api/auth/internal/tenants
        /// </summary>
        [HttpPost("tenants")]
        public async Task<IActionResult> RegisterTenant(
            [FromBody] RegisterAuthTenantRequest request)
        {
            // ==========================================
            // 1. リクエストチェック
            // ==========================================

            if (request == null ||
                string.IsNullOrWhiteSpace(request.TenantId))
            {
                return BadRequest(new
                {
                    error = "InvalidRequest",
                    message = "tenantId は必須です。"
                });
            }

            // ==========================================
            // 2. 重複確認
            // ==========================================

            var exists = await _dbContext.Tenants
                .AnyAsync(t =>
                    t.TenantId == request.TenantId);

            if (exists)
            {
                return Conflict(new
                {
                    error = "TenantAlreadyExists",
                    message =
                        "指定された tenant_id は既に存在します。",
                    tenantId = request.TenantId
                });
            }

            // ==========================================
            // 3. テナント作成
            // ==========================================

            var tenant = new AuthTenant
            {
                TenantId = request.TenantId
            };

            _dbContext.Tenants.Add(tenant);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Conflict(new
                {
                    error = "TenantAlreadyExists",
                    message =
                        "指定された tenant_id は既に存在します。",
                    tenantId = request.TenantId
                });
            }

            return Created(
                $"/api/auth/internal/tenants/{tenant.TenantId}",
                new
                {
                    tenantId = tenant.TenantId
                });
        }

        // ==========================================
        // テナント削除
        // ==========================================

        /// <summary>
        /// 内部認証テナント削除
        ///
        /// DELETE /api/auth/internal/tenants/{tenantId}
        /// </summary>
        [HttpDelete("tenants/{tenantId}")]
        public async Task<IActionResult> DeleteTenant(
            [FromRoute] string tenantId)
        {
            // ==========================================
            // 1. リクエストチェック
            // ==========================================

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return BadRequest(new
                {
                    error = "InvalidRequest",
                    message = "tenantId は必須です。"
                });
            }

            // ==========================================
            // 2. テナント取得
            // ==========================================

            var tenant = await _dbContext.Tenants
                .FirstOrDefaultAsync(t =>
                    t.TenantId == tenantId);

            if (tenant == null)
            {
                return NotFound(new
                {
                    error = "TenantNotFound",
                    message =
                        "該当する認証テナントが存在しません。",
                    tenantId
                });
            }

            // ==========================================
            // 3. 所属ユーザー削除
            // ==========================================

            var users = await _dbContext.Users
                .Where(u =>
                    u.TenantId == tenantId)
                .ToListAsync();

            _dbContext.Users.RemoveRange(users);

            // ==========================================
            // 4. テナント削除
            // ==========================================

            _dbContext.Tenants.Remove(tenant);

            // ==========================================
            // 5. 保存
            // ==========================================

            await _dbContext.SaveChangesAsync();

            return NoContent();
        }
    }
}