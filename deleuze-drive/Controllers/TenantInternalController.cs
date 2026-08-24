using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<TenantInternalController> _logger;

        public TenantInternalController(
            DriveDbContext dbContext, 
            IStorageService storageService,
            ITenantMigrationService migrationService,
            ILogger<TenantInternalController> logger)
        {
            _dbContext = dbContext;
            _storageService = storageService;
            _migrationService = migrationService;
            _logger = logger;
        }

        [HttpPost("{tenantId}/initialize")]
        public async Task<IActionResult> InitializeTenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                _logger.LogWarning("[TenantService] Initialize failed. Invalid tenantId format: {TenantId}", tenantId);
                return BadRequest("無効なテナントID形式です。英数字、ハイフン、アンダースコアのみ使用できます。");
            }

            string schemaName = $"app_{tenantId}";
            _logger.LogInformation("[TenantService] Starting initialization for TenantId: {TenantId}, Schema: {SchemaName}", tenantId, schemaName);

            try
            {
                await _migrationService.MigrateTenantSchemaAsync(schemaName);
                _logger.LogInformation("[TenantService] Successfully initialized tenant schema: {SchemaName}", schemaName);
                return Ok(new { message = $"Drive schema '{schemaName}' initialized and migrated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TenantService] Failed to initialize tenant: {TenantId}", tenantId);
                return StatusCode(500, new { error = $"テナント '{tenantId}' の初期化中にエラーが発生しました: {ex.Message}" });
            }
        }

        [HttpPost("{tenantId}/migrate")]
        public async Task<IActionResult> MigrateTenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                _logger.LogWarning("[TenantService] Migration failed. Invalid tenantId format: {TenantId}", tenantId);
                return BadRequest("無効なテナントID形式です。英数字、ハイフン、アンダースコアのみ使用できます。");
            }

            string schemaName = $"app_{tenantId}";
            _logger.LogInformation("[TenantService] Starting schema migration for TenantId: {TenantId}, Schema: {SchemaName}", tenantId, schemaName);

            try
            {
                await _migrationService.MigrateTenantSchemaAsync(schemaName);
                _logger.LogInformation("[TenantService] Successfully completed migration for schema: {SchemaName}", schemaName);
                return Ok(new { message = $"Migration completed for tenant schema '{schemaName}' in drive." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TenantService] Failed to migrate tenant: {TenantId}", tenantId);
                return StatusCode(500, new { error = $"テナント '{tenantId}' のマイグレーション中にエラーが発生しました: {ex.Message}" });
            }
        }

        [HttpGet("{tenantId}/migrations")]
        public async Task<IActionResult> GetTenantMigrations(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                _logger.LogWarning("[TenantService] Fetching migrations failed. Invalid tenantId format: {TenantId}", tenantId);
                return BadRequest("無効なテナントID形式です。");
            }

            string schemaName = $"app_{tenantId}";
            _logger.LogInformation("[TenantService] Fetching migration history for TenantId: {TenantId}", tenantId);

            try
            {
                var query = $@"
                    SELECT ""MigrationName"", ""AppliedAt"" 
                    FROM ""{schemaName}"".""SchemaMigrations"" 
                    ORDER BY ""AppliedAt"" ASC;";

                var migrations = await _dbContext.Database
                    .SqlQueryRaw<MigrationHistoryDto>(query)
                    .ToListAsync();

                _logger.LogInformation("[TenantService] Retrieved {Count} migration records for TenantId: {TenantId}", migrations.Count, tenantId);
                return Ok(migrations);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TenantService] Could not retrieve migrations for schema: {SchemaName}. Returning empty list.", schemaName);
                return Ok(new List<MigrationHistoryDto>());
            }
        }

        [HttpGet("{tenantId}/health")]
        public async Task<IActionResult> CheckTenantHealth(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_-]+$"))
            {
                _logger.LogWarning("[TenantService] Health check failed. Invalid tenantId format: {TenantId}", tenantId);
                return BadRequest("無効なテナントID形式です。");
            }

            string schemaName = $"app_{tenantId}";
            string dbStatus = "Unknown";
            string storageStatus = "Unknown";

            _logger.LogInformation("[TenantService] Checking health for TenantId: {TenantId}", tenantId);

            try
            {
                var schemaExists = await _dbContext.Database
                    .SqlQueryRaw<int>($"SELECT COUNT(1) FROM information_schema.schemata WHERE schema_name = @p0", schemaName)
                    .FirstOrDefaultAsync();

                dbStatus = schemaExists > 0 ? "Healthy (Schema Exists)" : "Degraded (Schema Missing)";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TenantService] DB health check error for Schema: {SchemaName}", schemaName);
                dbStatus = $"Unhealthy: {ex.Message}";
            }

            try
            {
                storageStatus = "Healthy";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TenantService] Storage health check error for TenantId: {TenantId}", tenantId);
                storageStatus = $"Unhealthy: {ex.Message}";
            }

            _logger.LogInformation("[TenantService] Health check result for TenantId: {TenantId} -> DB: {DbStatus}, Storage: {StorageStatus}", tenantId, dbStatus, storageStatus);

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
                _logger.LogWarning("[TenantService] Delete tenant failed. Invalid tenantId format: {TenantId}", tenantId);
                return BadRequest("無効なテナントID形式です。英数字、ハイフン、アンダースコアのみ使用できます。");
            }

            _logger.LogWarning("[TenantService] INITIATING DELETION for TenantId: {TenantId}", tenantId);

            try
            {
                _logger.LogInformation("[TenantService] Deleting S3 objects with prefix: {TenantId}", tenantId);
                await _storageService.DeletePrefixAsync(tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TenantService] Error deleting S3 objects for TenantId: {TenantId}", tenantId);
                return StatusCode(500, new { error = $"S3データの削除中にエラーが発生しました: {ex.Message}" });
            }

            string schemaName = $"app_{tenantId}";

            try
            {
                _logger.LogInformation("[TenantService] Dropping DB schema: {SchemaName}", schemaName);
                #pragma warning disable EF1002
                await _dbContext.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;");
                #pragma warning restore EF1002

                _logger.LogInformation("[TenantService] COMPLETELY DELETED TenantId: {TenantId} (DB & Storage)", tenantId);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TenantService] Error dropping DB schema for TenantId: {TenantId}", tenantId);
                return StatusCode(500, new { error = $"スキーマ削除中にエラーが発生しました: {ex.Message}" });
            }
        }
    }

    public class MigrationHistoryDto
    {
        public string MigrationName { get; set; } = string.Empty;
        public DateTimeOffset AppliedAt { get; set; }
    }
}