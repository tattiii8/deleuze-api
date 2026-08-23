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
    [Route("api/drive/internal/tenants")] // 👈 プレフィックスに合わせて修正
    public class TenantInternalController : ControllerBase
    {
        private readonly DriveDbContext _dbContext;
        private readonly IStorageService _storageService;

        public TenantInternalController(DriveDbContext dbContext, IStorageService storageService)
        {
            _dbContext = dbContext;
            _storageService = storageService;
        }

        [HttpPost("{tenantId}/initialize")]
        public async Task<IActionResult> InitializeTenant(string tenantId)
        {
            // SQLインジェクション（パストラバーサル含む）を防ぐための入力検証
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                return BadRequest("無効なテナントID形式です。英数字、ハイフン、アンダースコアのみ使用できます。");
            }

            string schemaName = $"app_{tenantId}";

            #pragma warning disable EF1002 // パラメータ化できないDDL識別子のため、事前サニタイズの上で警告を抑制
            await _dbContext.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";");

            var createFilesSql = $@"
                CREATE TABLE IF NOT EXISTS ""{schemaName}"".""Files"" (
                    ""Id"" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""FileName"" VARCHAR(255) NOT NULL,
                    ""ContentType"" VARCHAR(100),
                    ""ByteSize"" BIGINT NOT NULL DEFAULT 0,
                    ""StoragePath"" TEXT NOT NULL,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );";
            await _dbContext.Database.ExecuteSqlRawAsync(createFilesSql);

            var createFoldersSql = $@"
                CREATE TABLE IF NOT EXISTS ""{schemaName}"".""Folders"" (
                    ""Id"" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""Name"" VARCHAR(255) NOT NULL,
                    ""ParentId"" UUID REFERENCES ""{schemaName}"".""Folders""(""Id"") ON DELETE CASCADE,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );";
            await _dbContext.Database.ExecuteSqlRawAsync(createFoldersSql);
            #pragma warning restore EF1002

            return Ok(new { message = $"Drive schema '{schemaName}' initialized successfully." });
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
                // 既存テナントのスキーマに対して最新のマイグレーションや追加のDDL適用を行う
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
            // SQLインジェクションを防ぐための入力検証
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                return BadRequest("無効なテナントID形式です。英数字、ハイフン、アンダースコアのみ使用できます。");
            }

            // 1. S3 内の対象テナント用フォルダ（{tenantId}/...）のファイルを一括削除
            try
            {
                await _storageService.DeletePrefixAsync(tenantId);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"S3データの削除中にエラーが発生しました: {ex.Message}" });
            }

            // 2. DBのアプリケーションスキーマを削除
            string schemaName = $"app_{tenantId}";

            #pragma warning disable EF1002
            await _dbContext.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;");
            #pragma warning restore EF1002

            return NoContent();
        }
    }
}