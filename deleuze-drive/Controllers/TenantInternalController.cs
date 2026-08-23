using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeleuzeDrive.Data;
using DeleuzeDrive.Services;

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [AllowAnonymous] // 内部管理APIはアクセストークン不要
    [Route("internal/tenants")]
    public class TenantInternalController : ControllerBase
    {
        private readonly DriveDbContext _dbContext;
        private readonly IStorageService _storageService;
        private readonly ITenantMigrationService _migrationService; // 👈 追加

        public TenantInternalController(
            DriveDbContext dbContext, 
            IStorageService storageService,
            ITenantMigrationService migrationService) // 👈 追加
        {
            _dbContext = dbContext;
            _storageService = storageService;
            _migrationService = migrationService; // 👈 追加
        }

        [HttpPost("{tenantId}/initialize")]
        public async Task<IActionResult> InitializeTenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                return BadRequest("無効なテナントID形式です。英数字、ハイフン、アンダースコアのみ使用できます。");
            }

            string schemaName = $"app_{tenantId}";

            // 初回も共通のマイグレーションサービス（v1.0__initial.sqlを含む全適用）を呼ぶようにするとスッキリします
            try
            {
                await _migrationService.MigrateTenantSchemaAsync(schemaName);
                return Ok(new { message = $"Drive schema '{schemaName}' initialized and migrated successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"テナント '{tenantId}' の初期化中にエラーが発生しました: {ex.Message}" });
            }
        }

        [HttpPost("{tenantId}/migrate")]
        public async Task<IActionResult> MigrateTenant(string tenantId)
        {
            // SQLインジェクションを防ぐための入力検証
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                return BadRequest("無効なテナントID形式です。英数字、ハイフン、アンダースコアのみ使用できます。");
            }

            string schemaName = $"app_{tenantId}";

            try
            {
                // 👈 ここで正しく TenantMigrationService を呼び出す！
                await _migrationService.MigrateTenantSchemaAsync(schemaName);

                return Ok(new { message = $"Migration completed for tenant schema '{schemaName}' in drive." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"テナント '{tenantId}' のマイグレーション中にエラーが発生しました: {ex.Message}" });
            }
        }

        [HttpDelete("{tenantId}")]
        public async Task<IActionResult> DeleteTenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                return BadRequest("無効なテナントID形式です。英数字、ハイフン、アンダースコアのみ使用できます。");
            }

            try
            {
                await _storageService.DeletePrefixAsync(tenantId);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"S3データの削除中にエラーが発生しました: {ex.Message}" });
            }

            string schemaName = $"app_{tenantId}";

            #pragma warning disable EF1002
            await _dbContext.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;");
            #pragma warning restore EF1002

            return NoContent();
        }
    }
}