using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using DeleuzeAuth.Services.Tenant;
using DeleuzeAuth.Services.User;
using DeleuzeAuth.Services.ApiKey;

using Deleuze.Shared.Constants;

namespace DeleuzeAuth.Controllers;

[ApiController]
[AllowAnonymous]
[Route(ApiRoutes.Auth.InternalBase)]
public class InternalAuthController : ControllerBase
{
    private readonly ITenantProvisioningService _provisioningService;
    private readonly ITenantMigrationService _migrationService;
    private readonly ITenantDeprovisioningService _deprovisioningService;
    private readonly IUserManagementService _userManagementService;
    private readonly IApiKeyService _apiKeyService;

    public InternalAuthController(
        ITenantProvisioningService provisioningService,
        ITenantMigrationService migrationService,
        ITenantDeprovisioningService deprovisioningService,
        IUserManagementService userManagementService,
        IApiKeyService apiKeyService)
    {
        _provisioningService = provisioningService;
        _migrationService = migrationService;
        _deprovisioningService = deprovisioningService;
        _userManagementService = userManagementService;
        _apiKeyService = apiKeyService;
    }

    // ==========================================================
    // Global User Management (auth_global)
    // ==========================================================

    [HttpPost("global/users")]
    public async Task<IActionResult> CreateGlobalUser([FromBody] CreateGlobalUserRequest request)
    {
        try
        {
            var user = await _userManagementService.CreateGlobalUserAsync(request);
            return Ok(user);
        }
        catch (DuplicateUserException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "グローバルユーザーの作成に失敗しました。",
                message = ex.Message
            });
        }
    }

    [HttpPost("global/users/{loginId:guid}/apikeys")]
    public async Task<IActionResult> IssueApiKey(Guid loginId)
    {
        try
        {
            var apiKeyResult = await _apiKeyService.IssueApiKeyAsync(loginId);
            return Ok(apiKeyResult);
        }
        catch (UserNotFoundException)
        {
            return NotFound(new { error = "指定されたユーザーが存在しません。" });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "API Keyの発行に失敗しました。",
                message = ex.Message
            });
        }
    }

    // ==========================================================
    // ApiKey Validation
    // ==========================================================

    [HttpPost("apikey/validate")]
    public async Task<IActionResult> ValidateApiKey([FromBody] ApiKeyValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.ApiKey))
        {
            return BadRequest(new { error = "ApiKey が指定されていません。" });
        }

        try
        {
            var validationResult = await _apiKeyService.ValidateAsync(request.ApiKey);

            if (validationResult == null || !validationResult.IsValid)
            {
                return Unauthorized(new { error = "無効または期限切れのApiKeyです。" });
            }

            return Ok(validationResult);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "ApiKeyの検証に失敗しました。",
                message = ex.Message
            });
        }
    }

    // ==========================================================
    // Tenant Provisioning & Migration
    // ==========================================================

    [HttpPost("tenants/{tenantId}")]
    public async Task<IActionResult> ProvisionTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest(new { error = "tenantId が指定されていません。" });
        }

        try
        {
            await _provisioningService.ProvisionAsync(tenantId);

            return Ok(new
            {
                tenantId,
                service = "auth",
                schema = $"auth_{tenantId}",
                status = "provisioned"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "テナントのProvisioningに失敗しました。",
                message = ex.Message
            });
        }
    }

    [HttpPost("tenants/{tenantId}/migrate")]
    public async Task<IActionResult> MigrateTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest(new { error = "tenantId が指定されていません。" });
        }

        try
        {
            await _migrationService.MigrateAsync(tenantId);

            return Ok(new
            {
                tenantId,
                service = "auth",
                schema = $"auth_{tenantId}",
                status = "migrated"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "テナントのMigrationに失敗しました。",
                message = ex.Message
            });
        }
    }

    [HttpDelete("tenants/{tenantId}")]
    public async Task<IActionResult> DeprovisionTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest(new { error = "tenantId が指定されていません。" });
        }

        try
        {
            await _deprovisioningService.DeprovisionAsync(tenantId);

            return Ok(new
            {
                tenantId,
                service = "auth",
                schema = $"auth_{tenantId}",
                status = "deprovisioned"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "テナントのDeprovisioningに失敗しました。",
                message = ex.Message
            });
        }
    }

    // ==========================================================
    // Tenant Member & Role Management (auth_{tenantId})
    // ==========================================================

    [HttpPost("tenants/{tenantId}/members")]
    public async Task<IActionResult> AddTenantMember(
        string tenantId,
        [FromBody] AddTenantMemberRequest request)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest(new { error = "tenantId が指定されていません。" });
        }

        try
        {
            var member = await _userManagementService.AddMemberToTenantAsync(tenantId, request);
            return Ok(member);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "テナントへのメンバー追加に失敗しました。",
                message = ex.Message
            });
        }
    }

    [HttpGet("tenants/{tenantId}/members")]
    public async Task<IActionResult> GetTenantMembers(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest(new { error = "tenantId が指定されていません。" });
        }

        try
        {
            var members = await _userManagementService.GetTenantMembersAsync(tenantId);
            return Ok(members);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "メンバー一覧の取得に失敗しました。",
                message = ex.Message
            });
        }
    }

    [HttpDelete("tenants/{tenantId}/members/{loginId:guid}")]
    public async Task<IActionResult> RemoveTenantMember(string tenantId, Guid loginId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest(new { error = "tenantId が指定されていません。" });
        }

        try
        {
            await _userManagementService.RemoveMemberFromTenantAsync(tenantId, loginId);

            return Ok(new
            {
                tenantId,
                loginId,
                status = "removed"
            });
        }
        catch (UserNotFoundException)
        {
            return NotFound(new { error = "テナント内に該当メンバーが存在しません。" });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "テナントメンバーの削除に失敗しました。",
                message = ex.Message
            });
        }
    }
}