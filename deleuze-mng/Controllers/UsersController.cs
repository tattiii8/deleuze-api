using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Deleuze.Shared.Constants;
using DeleuzeMng.Data;
using DeleuzeMng.Models;
using DeleuzeMng.Services;

namespace DeleuzeMng.Controllers
{
    // ==========================================
    // 1. ユーザー管理 API
    //
    // POST   /api/mng/tenants/{tenantId}/users
    // GET    /api/mng/tenants/{tenantId}/users
    // GET    /api/mng/tenants/{tenantId}/users/{subjectId}
    // DELETE /api/mng/tenants/{tenantId}/users/{subjectId}
    // ==========================================
    [ApiController]
    [Route(ApiRoutes.Management.Users)]
    public class UsersController : ControllerBase
    {
        private readonly MngDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;

        public UsersController(
            MngDbContext dbContext,
            IHttpClientFactory httpClientFactory)
        {
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// ユーザー作成
        /// POST /api/mng/tenants/{tenantId}/users
        /// </summary>
        [HttpPost("/api/mng/tenants/{tenantId}/users")]
        public async Task<IActionResult> CreateUser(
            string tenantId,
            [FromBody] CreateUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || request == null)
            {
                return BadRequest("リクエストが無効です。");
            }

            if (string.IsNullOrWhiteSpace(request.LoginId) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest("LoginId, Password, Email は必須です。");
            }

            var subjectId = Guid.NewGuid().ToString();

            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 1. 管理DBへの保存
                var mngUser = new MngUser
                {
                    SubjectId = subjectId,
                    TenantId = tenantId,
                    LoginId = request.LoginId,
                    UserName = request.UserName,
                    Email = request.Email
                };

                _dbContext.Users.Add(mngUser);

                try
                {
                    await _dbContext.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                    when (ex.InnerException is Npgsql.PostgresException pg &&
                          pg.SqlState == "23505")
                {
                    await transaction.RollbackAsync();

                    return Conflict(new
                    {
                        error = "UserAlreadyExists",
                        message = "指定されたテナントでは、この login_id は既に使用されています。",
                        tenantId,
                        loginId = request.LoginId
                    });
                }

                // 2. 認証API連携
                var client =
                    _httpClientFactory.CreateClient("AuthApiClient");

                var authPayload = new
                {
                    SubjectId = subjectId,
                    TenantId = tenantId,
                    LoginId = request.LoginId,
                    Password = request.Password
                };

                var authEndpoint =
                    $"{ApiRoutes.Auth.InternalBase}/users";

                var response = await client.PostAsJsonAsync(
                    authEndpoint,
                    authPayload);

                // 3. Auth側でエラー
                if (!response.IsSuccessStatusCode)
                {
                    await transaction.RollbackAsync();

                    var error =
                        await response.Content.ReadAsStringAsync();

                    return StatusCode(
                        (int)response.StatusCode,
                        $"認証DBへの登録に失敗しました: {error}");
                }

                // 4. 両方成功
                await transaction.CommitAsync();

                return CreatedAtAction(
                    nameof(GetUserBySubjectId),
                    new
                    {
                        tenantId,
                        subjectId
                    },
                    new
                    {
                        subjectId,
                        tenantId,
                        loginId = request.LoginId,
                        userName = request.UserName,
                        email = request.Email
                    });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(
                    500,
                    $"ユーザー作成中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// テナントのユーザー一覧取得
        /// GET /api/mng/tenants/{tenantId}/users
        /// </summary>
        [HttpGet("/api/mng/tenants/{tenantId}/users")]
        public async Task<IActionResult> GetUsers(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return BadRequest("tenantId は必須です。");
            }

            var users = await _dbContext.Users
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId)
                .OrderBy(u => u.CreatedAt)
                .Select(u => new
                {
                    subjectId = u.SubjectId,
                    tenantId = u.TenantId,
                    loginId = u.LoginId,
                    userName = u.UserName,
                    email = u.Email,
                    createdAt = u.CreatedAt,
                    updatedAt = u.UpdatedAt
                })
                .ToListAsync();

            return Ok(users);
        }

        /// <summary>
        /// テナントのユーザー取得
        /// GET /api/mng/tenants/{tenantId}/users/{subjectId}
        /// </summary>
        [HttpGet("/api/mng/tenants/{tenantId}/users/{subjectId}")]
        public async Task<IActionResult> GetUserBySubjectId(
            string tenantId,
            string subjectId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) ||
                string.IsNullOrWhiteSpace(subjectId))
            {
                return BadRequest(
                    "tenantId と subjectId は必須です。");
            }

            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.TenantId == tenantId &&
                    u.SubjectId == subjectId);

            if (user == null)
            {
                return NotFound("ユーザーが見つかりません。");
            }

            return Ok(new
            {
                subjectId = user.SubjectId,
                tenantId = user.TenantId,
                loginId = user.LoginId,
                userName = user.UserName,
                email = user.Email,
                createdAt = user.CreatedAt,
                updatedAt = user.UpdatedAt
            });
        }

        /// <summary>
        /// テナントのユーザー削除
        /// DELETE /api/mng/tenants/{tenantId}/users/{subjectId}
        /// </summary>
        [HttpDelete("/api/mng/tenants/{tenantId}/users/{subjectId}")]
        public async Task<IActionResult> DeleteUser(
            string tenantId,
            string subjectId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) ||
                string.IsNullOrWhiteSpace(subjectId))
            {
                return BadRequest(
                    "tenantId と subjectId は必須です。");
            }

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u =>
                    u.TenantId == tenantId &&
                    u.SubjectId == subjectId);

            if (user == null)
            {
                return NotFound(
                    "指定されたテナントにユーザーが存在しません。");
            }

            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 1. 管理DBから削除
                _dbContext.Users.Remove(user);
                await _dbContext.SaveChangesAsync();

                // 2. Auth APIから削除
                var client =
                    _httpClientFactory.CreateClient("AuthApiClient");

                var authEndpoint =
                    $"{ApiRoutes.Auth.InternalBase}/users/{subjectId}";

                var response =
                    await client.DeleteAsync(authEndpoint);

                if (!response.IsSuccessStatusCode &&
                    response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    await transaction.RollbackAsync();

                    var error =
                        await response.Content.ReadAsStringAsync();

                    return StatusCode(
                        (int)response.StatusCode,
                        $"認証DBからの削除に失敗しました: {error}");
                }

                await transaction.CommitAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(
                    500,
                    $"ユーザー削除中にエラーが発生しました: {ex.Message}");
            }
        }
    }

    // ==========================================
    // 2. 内部管理 API
    // ==========================================
    [ApiController]
    [Route(ApiRoutes.Management.InternalBase)]
    public class InternalMngController : ControllerBase
    {
        private readonly IDbInitializerService _dbInitializer;

        public InternalMngController(
            IDbInitializerService dbInitializer)
        {
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
                    message = "mng スキーマおよび初期テーブルの作成が完了しました。",
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
    }
}