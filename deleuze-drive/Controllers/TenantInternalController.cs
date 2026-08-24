using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DeleuzeDrive.Services;
using Deleuze.Shared.Constants; // 共通定数を参照

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [Authorize]
    [Route(ApiRoutes.Drive.InternalBase + "/tenants")] // -> "api/drive/internal/tenants"
    public class TenantInternalController : ControllerBase
    {
        private readonly ITenantMigrationService _migrationService;

        public TenantInternalController(ITenantMigrationService migrationService)
        {
            _migrationService = migrationService;
        }

        [HttpPost("{tenantId}/migrate")] // -> /api/drive/internal/tenants/{tenantId}/migrate
        public async Task<IActionResult> MigrateTenant(string tenantId)
        {
            await _migrationService.MigrateTenantSchemaAsync(tenantId);
            return Ok(new { message = $"Tenant {tenantId} migrated successfully." });
        }
    }
}