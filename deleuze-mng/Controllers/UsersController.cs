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
    [ApiController]
    [Route(ApiRoutes.Management.Users)]
    public class UsersController : ControllerBase
    {
        private readonly MngDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;

        public UsersController(MngDbContext dbContext, IHttpClientFactory httpClientFactory)
        {
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
        }

        // ==========================================
        // 1. ユーザー作成 (Create)
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> CreateUser(string tenantId, [FromBody] CreateUserRequest request)
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

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 管理DBへの保存
                var mngUser = new MngUser
                {
                    SubjectId = subjectId,
                    LoginId = request.LoginId,
                    UserName = request.UserName,
                    Email = request.Email
                };

                _dbContext.Users.Add(mngUser);
                await _dbContext.SaveChangesAsync();

                // 認証API連携
                var client = _httpClientFactory.CreateClient("AuthApiClient");
                var authPayload = new
                {
                    SubjectId = subjectId,
                    LoginId = request.LoginId,
                    Password = request.Password
                };

                var response = await client.PostAsJsonAsync("/api/auth/internal/users", authPayload);

                if (!response.IsSuccessStatusCode)
                {
                    await transaction.RollbackAsync();
                    var error = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode, $"認証DBへの登録に失敗しました: {error}");
                }

                await transaction.CommitAsync();

                return CreatedAtAction(nameof(GetUserBySubjectId), new { tenantId, subjectId }, new
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
                return StatusCode(500, $"ユーザー作成中にエラーが発生しました: {ex.Message}");
            }
        }

        // ==========================================
        // 2. ユーザー削除 (Delete)
        // ==========================================
        [HttpDelete("{subjectId}")]
        public async Task<IActionResult> DeleteUser(string tenantId, string subjectId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(subjectId))
            {
                return BadRequest("パラメーターが無効です。");
            }

            var user = await _dbContext.Users.FindAsync(subjectId);
            if (user == null) return NotFound("指定されたユーザーが存在しません。");

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 1. 管理DBからの削除
                _dbContext.Users.Remove(user);
                await _dbContext.SaveChangesAsync();

                // 2. 認証APIを呼び出して認証DB側も削除
                var client = _httpClientFactory.CreateClient("AuthApiClient");
                var response = await client.DeleteAsync($"/api/auth/internal/users/{subjectId}");

                if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    // 認証DB削除失敗時は管理DBの削除をロールバック
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

        // ==========================================
        // 3. ユーザー取得 (Read)
        // ==========================================
        [HttpGet("{subjectId}")]
        public async Task<IActionResult> GetUserBySubjectId(string tenantId, string subjectId)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.SubjectId == subjectId);
            if (user == null) return NotFound("ユーザーが見つかりません。");

            return Ok(user);
        }
    }

    [Route(ApiRoutes.Management.InternalBase)]
    public class InternalMngController : ControllerBase
    {
        private readonly MngDbContext _dbContext;
        private readonly IDbInitializerService _dbInitializer;

        public InternalMngController(MngDbContext dbContext, IDbInitializerService dbInitializer)
        {
            _dbContext = dbContext;
            _dbInitializer = dbInitializer;
        }

        /// <summary>
        /// DBの初期スキーマおよびテーブルを .sql ファイルから作成します。
        /// </summary>
        [HttpPost("init")] // -> POST api/auth/internal/init
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