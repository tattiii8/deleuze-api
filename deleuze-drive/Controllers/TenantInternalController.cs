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

        [HttpPost("{tenantId}/init")] // -> /api/drive/internal/tenants/{tenantId}/init
        public async Task<IActionResult> InitializeTenant(string tenantId)
        {
            await _migrationService.MigrateTenantSchemaAsync(tenantId);
            return Ok(new { message = $"Tenant {tenantId} initialized successfully." });
        }
    }
}