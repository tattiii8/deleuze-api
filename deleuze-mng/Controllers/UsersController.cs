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
    // 1. 公開ユーザー管理 API (api/mng/users)
    // ==========================================
    [ApiController]
    [Route(ApiRoutes.Management.Users)] // -> "api/mng/users"
    public class UsersController : ControllerBase
    {
        private readonly MngDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;

        public UsersController(MngDbContext dbContext, IHttpClientFactory httpClientFactory)
        {
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// ユーザー作成
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateUser(
            [FromQuery] string tenantId,
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
                        message = "指定された login_id は既に使用されています。",
                        loginId = request.LoginId
                    });
                }

                // 2. 認証API連携
                var client = _httpClientFactory.CreateClient("AuthApiClient");

                var authPayload = new
                {
                    SubjectId = subjectId,
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

                    var error = await response.Content.ReadAsStringAsync();

                    return StatusCode(
                        (int)response.StatusCode,
                        $"認証DBへの登録に失敗しました: {error}");
                }

                // 4. 両方成功
                await transaction.CommitAsync();

                return CreatedAtAction(
                    nameof(GetUserBySubjectId),
                    new { subjectId },
                    new
                    {
                        subjectId,
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
        /// ユーザー削除
        /// </summary>
        [HttpDelete("{subjectId}")]
        public async Task<IActionResult> DeleteUser(string subjectId)
        {
            if (string.IsNullOrWhiteSpace(subjectId))
            {
                return BadRequest("subjectId は必須です。");
            }

            var user = await _dbContext.Users.FindAsync(subjectId);
            if (user == null) return NotFound("指定されたユーザーが存在しません。");

            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 1. 管理DBからの削除
                _dbContext.Users.Remove(user);
                await _dbContext.SaveChangesAsync();

                // 2. 認証APIを呼び出して認証DB側も削除
                var client = _httpClientFactory.CreateClient("AuthApiClient");
                var authEndpoint = $"{ApiRoutes.Auth.InternalBase}/users/{subjectId}"; // -> "api/auth/internal/users/{subjectId}"
                var response = await client.DeleteAsync(authEndpoint);

                if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    await transaction.RollbackAsync();
                    var error = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode, $"認証DBからの削除に失敗しました: {error}");
                }

                await transaction.CommitAsync();
                return NoContent(); // 204 Success
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"ユーザー削除中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// ユーザー取得
        /// </summary>
        [HttpGet("{subjectId}")]
        public async Task<IActionResult> GetUserBySubjectId(string subjectId)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.SubjectId == subjectId);
            if (user == null) return NotFound("ユーザーが見つかりません。");

            return Ok(user);
        }
    }

    // ==========================================
    // 2. 内部管理 API (api/mng/internal)
    // ==========================================
    [ApiController]
    [Route(ApiRoutes.Management.InternalBase)] // -> "api/mng/internal"
    public class InternalMngController : ControllerBase
    {
        private readonly IDbInitializerService _dbInitializer;

        public InternalMngController(IDbInitializerService dbInitializer)
        {
            _dbInitializer = dbInitializer;
        }

        /// <summary>
        /// DBの初期スキーマおよびテーブルを .sql ファイルから作成します。
        /// </summary>
        [HttpPost("init")] // -> POST api/mng/internal/init
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