using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DeleuzeDrive.Services;

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [Route("internal/tenants")]
    [Authorize]
    public class TenantInternalController : ControllerBase
    {
        private readonly ITenantMigrationService _migrationService;

        public TenantInternalController(ITenantMigrationService migrationService)
        {
            _migrationService = migrationService;
        }

        [HttpPost("{tenantId}/init")]
        public async Task<IActionResult> InitializeTenant(string tenantId)
        {
            await _migrationService.MigrateTenantSchemaAsync(tenantId);
            return Ok(new { message = $"Tenant {tenantId} initialized successfully." });
        }
    }
}