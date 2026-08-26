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
    [ApiController]
    [AllowAnonymous]
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

        /// <summary>
        /// DBの初期スキーマおよびテーブルを
        /// .sqlファイルから作成します。
        /// </summary>
        [HttpPost("init")]
        public async Task<IActionResult> InitializeDatabase()
        {
            try
            {
                await _dbInitializer.ExecuteInitSqlAsync();

                return Ok(new
                {
                    message = "auth スキーマおよび初期テーブルの作成が完了しました。",
                    timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "データベース初期化処理に失敗しました。",
                    detail = ex.Message
                });
            }
        }

        /// <summary>
        /// 内部認証ユーザー作成
        /// POST api/auth/internal/users
        /// </summary>
        [HttpPost("users")]
        public async Task<IActionResult> RegisterUser(
            [FromBody] RegisterAuthUserRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.SubjectId) ||
                string.IsNullOrWhiteSpace(request.TenantId) ||
                string.IsNullOrWhiteSpace(request.LoginId) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("必須項目が不足しています。");
            }

            // テナント内で login_id が重複していないか確認
            var exists = await _dbContext.Users
                .AnyAsync(u =>
                    u.TenantId == request.TenantId &&
                    u.LoginId == request.LoginId);

            if (exists)
            {
                return Conflict(new
                {
                    error = "UserAlreadyExists",
                    message = "指定されたテナントでは、この login_id は既に使用されています。",
                    tenantId = request.TenantId,
                    loginId = request.LoginId
                });
            }

            var authUser = new AuthUser
            {
                SubjectId = request.SubjectId,
                TenantId = request.TenantId,
                LoginId = request.LoginId,
                PasswordHash =
                    BCrypt.Net.BCrypt.HashPassword(request.Password)
            };

            _dbContext.Users.Add(authUser);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                message = "認証情報の登録に成功しました。",
                subjectId = authUser.SubjectId,
                tenantId = authUser.TenantId
            });
        }

        /// <summary>
        /// 内部認証ユーザー削除
        /// DELETE api/auth/internal/users/{subjectId}
        /// </summary>
        [HttpDelete("users/{subjectId}")]
        public async Task<IActionResult> DeleteUser(string subjectId)
        {
            if (string.IsNullOrWhiteSpace(subjectId))
            {
                return BadRequest("subjectId は必須です。");
            }

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u =>
                    u.SubjectId == subjectId);

            if (user == null)
            {
                return NotFound(
                    "該当する認証ユーザーが存在しません。");
            }

            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                message = "認証情報の削除に成功しました。"
            });
        }
    }
}