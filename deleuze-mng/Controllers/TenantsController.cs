using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Deleuze.Shared.Constants;
using DeleuzeMng.Data;
using DeleuzeMng.Models;

namespace DeleuzeMng.Controllers
{
    [ApiController]
    [Route(ApiRoutes.Management.Tenants)]
    public class TenantsController : ControllerBase
    {
        private readonly MngDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;

        public TenantsController(
            MngDbContext dbContext,
            IHttpClientFactory httpClientFactory)
        {
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// テナント作成
        /// POST /api/mng/tenants
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateTenant(
            [FromBody] CreateTenantRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.TenantId))
            {
                return BadRequest("TenantId は必須です。");
            }

            var tenantId = request.TenantId;

            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 1. Mng DBへ保存
                var tenant = new Tenant
                {
                    TenantId = tenantId,
                    TenantName = tenantId,
                    DisplayName = request.DisplayName
                };

                _dbContext.Tenants.Add(tenant);

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
                        error = "TenantAlreadyExists",
                        message = "指定されたテナントIDは既に使用されています。",
                        tenantName = request.TenantId
                    });
                }

                // 2. Authへ同期
                var client =
                    _httpClientFactory.CreateClient("AuthApiClient");

                var authPayload = new
                {
                    TenantId = tenantId
                };

                var authEndpoint =
                    $"{ApiRoutes.Auth.InternalBase}/tenants";

                var response = await client.PostAsJsonAsync(
                    authEndpoint,
                    authPayload);

                if (!response.IsSuccessStatusCode)
                {
                    await transaction.RollbackAsync();

                    var error =
                        await response.Content.ReadAsStringAsync();

                    return StatusCode(
                        (int)response.StatusCode,
                        $"認証DBへのテナント登録に失敗しました: {error}");
                }

                await transaction.CommitAsync();

                return Created(
                    $"/api/mng/tenants/{tenantId}",
                    new
                    {
                        tenantId,
                        tenantName = tenant.TenantName,
                        displayName = tenant.DisplayName,
                        createdAt = tenant.CreatedAt,
                        updatedAt = tenant.UpdatedAt
                    });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(
                    500,
                    $"テナント作成中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// テナント一覧取得
        /// GET /api/mng/tenants
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTenants()
        {
            var tenants = await _dbContext.Tenants
                .AsNoTracking()
                .OrderBy(t => t.CreatedAt)
                .Select(t => new
                {
                    tenantId = t.TenantId,
                    tenantName = t.TenantName,
                    displayName = t.DisplayName,
                    createdAt = t.CreatedAt,
                    updatedAt = t.UpdatedAt
                })
                .ToListAsync();

            return Ok(tenants);
        }

        /// <summary>
        /// テナント取得
        /// GET /api/mng/tenants/{tenantId}
        /// </summary>
        [HttpGet("{tenantId}")]
        public async Task<IActionResult> GetTenant(string tenantId)
        {
            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t =>
                    t.TenantId == tenantId);

            if (tenant == null)
            {
                return NotFound("テナントが見つかりません。");
            }

            return Ok(new
            {
                tenantId = tenant.TenantId,
                tenantName = tenant.TenantName,
                displayName = tenant.DisplayName,
                createdAt = tenant.CreatedAt,
                updatedAt = tenant.UpdatedAt
            });
        }

        /// <summary>
        /// テナント削除
        /// DELETE /api/mng/tenants/{tenantId}
        /// </summary>
        [HttpDelete("{tenantId}")]
        public async Task<IActionResult> DeleteTenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return BadRequest("tenantId は必須です。");
            }

            var tenant = await _dbContext.Tenants
                .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            if (tenant == null)
            {
                return NotFound("指定されたテナントが存在しません。");
            }

            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 1. Auth側のテナントを削除
                var client =
                    _httpClientFactory.CreateClient("AuthApiClient");

                var authEndpoint =
                    $"{ApiRoutes.Auth.InternalBase}/tenants/{tenantId}";

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
                        $"認証DBからのテナント削除に失敗しました: {error}");
                }

                // 2. Mng側のユーザーを削除
                var users = await _dbContext.Users
                    .Where(u => u.TenantId == tenantId)
                    .ToListAsync();

                _dbContext.Users.RemoveRange(users);

                // 3. Mng側のテナントを削除
                _dbContext.Tenants.Remove(tenant);

                await _dbContext.SaveChangesAsync();

                await transaction.CommitAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(
                    500,
                    $"テナント削除中にエラーが発生しました: {ex.Message}");
            }
        }
    }
}