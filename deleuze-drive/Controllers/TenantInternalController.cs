using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DeleuzeDrive.Services;
using Deleuze.Shared.Constants;

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route(ApiRoutes.Drive.InternalBase + "/tenants")]
    public class TenantInternalController : ControllerBase
    {
        private readonly ITenantProvisioningService _provisioningService;
        private readonly ITenantMigrationService _migrationService;

        public TenantInternalController(
            ITenantProvisioningService provisioningService,
            ITenantMigrationService migrationService)
        {
            _provisioningService = provisioningService;
            _migrationService = migrationService;
        }

        /// <summary>
        /// テナントの初期スキーマを作成します。
        /// </summary>
        /// <remarks>
        /// 新規テナントのSchemaを作成し、
        /// DbMigration配下のMigrationをすべて適用します。
        /// </remarks>
        [HttpPost("{tenantId}")]
        public async Task<IActionResult> ProvisionTenant(string tenantId)
        {
            await _provisioningService.ProvisionTenantSchemaAsync(tenantId);

            return Ok(new
            {
                message = $"Tenant {tenantId} provisioned successfully."
            });
        }

        /// <summary>
        /// 既存テナントの未適用Migrationを実行します。
        /// </summary>
        [HttpPost("{tenantId}/migrate")]
        public async Task<IActionResult> MigrateTenant(string tenantId)
        {
            await _migrationService.MigrateTenantSchemaAsync(tenantId);

            return Ok(new
            {
                message = $"Tenant {tenantId} migrated successfully."
            });
        }
    }
}