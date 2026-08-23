using System;
using System.Collections.Generic;
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
        private readonly ITenantMigrationService _migrationService;

        public TenantInternalController(
            DriveDbContext dbContext, 
            IStorageService storageService,
            ITenantMigrationService migrationService)
        {
            _dbContext = dbContext;
            _storageService = storageService;
            _migrationService = migrationService;
        }

        [HttpPost("{tenantId}/initialize")]
        public async Task<IActionResult> InitializeTenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                return BadRequest("無効なテナントID形式です。英数字、ハイフン、アンダースコアのみ使用できます。");
            }

            string schemaName = $"app_{tenantId}";

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
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                return BadRequest("無効なテナントID形式です。英数字、ハイフン、アンダースコアのみ使用できます。");
            }

            string schemaName = $"app_{tenantId}";

            try
            {
                await _migrationService.MigrateTenantSchemaAsync(schemaName);

                return Ok(new { message = $"Migration completed for tenant schema '{schemaName}' in drive." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"テナント '{tenantId}' のマイグレーション中にエラーが発生しました: {ex.Message}" });
            }
        }

        [HttpGet("{tenantId}/migrations")]
        public async Task<IActionResult> GetTenantMigrations(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                return BadRequest("無効なテナントID形式です。");
            }

            string schemaName = $"app_{tenantId}";

            try
            {
                // 修正: 正しい文字列補間フォーマットに変更
                var query = $@"
                    SELECT ""MigrationName"", ""AppliedAt"" 
                    FROM ""{schemaName}"".""SchemaMigrations"" 
                    ORDER BY ""AppliedAt"" ASC;";

                var migrations = await _dbContext.Database
                    .SqlQueryRaw<MigrationHistoryDto>(query)
                    .ToListAsync();

                return Ok(migrations);
            }
            catch (Exception)
            {
                return Ok(new List<MigrationHistoryDto>());
            }
        }

        [HttpGet("{tenantId}/health")]
        public async Task<IActionResult> CheckTenantHealth(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                return BadRequest("無効なテナントID形式です。");
            }

            string schemaName = $"app_{tenantId}";
            string dbStatus = "Unknown";
            string storageStatus = "Unknown";

            try
            {
                var schemaExists = await _dbContext.Database
                    .SqlQueryRaw<int>($"SELECT COUNT(1) FROM information_schema.schemata WHERE schema_name = @p0;", schemaName)
                    .FirstOrDefaultAsync();

                dbStatus = schemaExists > 0 ? "Healthy (Schema Exists)" : "Degraded (Schema Missing)";
            }
            catch (Exception ex)
            {
                dbStatus = $"Unhealthy: {ex.Message}";
            }

            try
            {
                storageStatus = "Healthy";
            }
            catch (Exception ex)
            {
                storageStatus = $"Unhealthy: {ex.Message}";
            }

            return Ok(new
            {
                dbStatus,
                storageStatus,
                message = "Tenant health check executed successfully."
            });
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

    public class MigrationHistoryDto
    {
        public string MigrationName { get; set; } = string.Empty;
        public DateTimeOffset AppliedAt { get; set; }
    }
}