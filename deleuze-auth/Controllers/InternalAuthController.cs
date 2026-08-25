using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Data;
using DeleuzeAuth.Models;
using DeleuzeAuth.Services.Tenant;
using Deleuze.Shared.Constants;

namespace DeleuzeAuth.Controllers;

[ApiController]
[AllowAnonymous]
[Route(ApiRoutes.Auth.InternalBase)]
public class InternalAuthController : ControllerBase
{
    private readonly AuthDbContext _dbContext;

    private readonly ITenantProvisioningService _provisioningService;
    private readonly ITenantMigrationService _migrationService;
    private readonly ITenantDeprovisioningService _deprovisioningService;

    public InternalAuthController(
        AuthDbContext dbContext,
        ITenantProvisioningService provisioningService,
        ITenantMigrationService migrationService,
        ITenantDeprovisioningService deprovisioningService)
    {
        _dbContext = dbContext;
        _provisioningService = provisioningService;
        _migrationService = migrationService;
        _deprovisioningService = deprovisioningService;
    }

    // ==========================================================
    // API Key
    // ==========================================================

    /// <summary>
    /// API Key の有効性とテナントの認証モードを検証します。
    /// </summary>
    [HttpPost("apikey")]
    public async Task<IActionResult> ValidateApiKey(
        [FromBody] ValidateApiKeyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return BadRequest(new
            {
                error = "API Key が指定されていません。"
            });
        }

        var tenant = await _dbContext.Tenants
            .FirstOrDefaultAsync(t => t.ApiKey == request.ApiKey);

        if (tenant == null)
        {
            return Unauthorized(new
            {
                error = "無効な API Key です。"
            });
        }

        if (tenant.AuthMode == AuthMode.JwtOnly)
        {
            return Unauthorized(new
            {
                error =
                    "このテナントでは API Key 認証が許可されていません。"
            });
        }

        return Ok(new ValidateApiKeyResponse
        {
            TenantId = tenant.Id,
            AuthMode = tenant.AuthMode.ToString()
        });
    }


    // ==========================================================
    // Tenant Provisioning
    // ==========================================================

    /// <summary>
    /// テナント用 Auth Schema を作成し、
    /// Tenant Migration を適用します。
    /// </summary>
    [HttpPost("tenants/{tenantId}")]
    public async Task<IActionResult> ProvisionTenant(
        string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest(new
            {
                error = "Tenant ID が指定されていません。"
            });
        }

        try
        {
            await _provisioningService.ProvisionAsync(tenantId);

            return Ok(new
            {
                tenant_id = tenantId,
                service = "auth",
                status = "provisioned"
            });
        }
        catch (System.Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = "テナントのProvisioningに失敗しました。",
                    message = ex.Message
                });
        }
    }


    // ==========================================================
    // Tenant Deprovisioning
    // ==========================================================

    /// <summary>
    /// テナント用 Auth Schema を削除します。
    /// </summary>
    [HttpDelete("tenants/{tenantId}")]
    public async Task<IActionResult> DeprovisionTenant(
        string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest(new
            {
                error = "Tenant ID が指定されていません。"
            });
        }

        try
        {
            await _deprovisioningService.DeprovisionAsync(tenantId);

            return Ok(new
            {
                tenant_id = tenantId,
                service = "auth",
                status = "deprovisioned"
            });
        }
        catch (System.Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = "テナントのDeprovisioningに失敗しました。",
                    message = ex.Message
                });
        }
    }


    // ==========================================================
    // Tenant Migration
    // ==========================================================

    /// <summary>
    /// 既存テナントの未適用Migrationを実行します。
    /// </summary>
    [HttpPost("tenants/{tenantId}/migrate")]
    public async Task<IActionResult> MigrateTenant(
        string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest(new
            {
                error = "Tenant ID が指定されていません。"
            });
        }

        try
        {
            await _migrationService.MigrateAsync(tenantId);

            return Ok(new
            {
                tenant_id = tenantId,
                service = "auth",
                status = "migrated"
            });
        }
        catch (System.Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = "テナントのMigrationに失敗しました。",
                    message = ex.Message
                });
        }
    }


    // ==========================================================
    // Tenant existence
    // ==========================================================

    /// <summary>
    /// Auth側でテナントが存在するか確認します。
    /// </summary>
    [HttpGet("tenants/{tenantId}")]
    public async Task<IActionResult> GetTenant(
        string tenantId)
    {
        var exists = await _dbContext.Tenants
            .AnyAsync(t => t.Id == tenantId);

        if (!exists)
        {
            return NotFound(new
            {
                tenant_id = tenantId
            });
        }

        return Ok(new
        {
            tenant_id = tenantId,
            service = "auth",
            exists = true
        });
    }
}


// ==========================================================
// DTO
// ==========================================================

public class ValidateApiKeyRequest
{
    public string ApiKey { get; set; } = string.Empty;
}

public class ValidateApiKeyResponse
{
    public string TenantId { get; set; } = string.Empty;

    public string AuthMode { get; set; } = string.Empty;
}