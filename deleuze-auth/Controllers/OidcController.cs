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

    /// <summary>
    /// auth_global にログイン認証用ユーザーを作成します。
    /// </summary>
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

    /// <summary>
    /// ユーザー用の API Key を発行し auth_global に保存します。
    /// </summary>
    [HttpPost("global/users/{userId:guid}/apikeys")]
    public async Task<IActionResult> IssueApiKey(Guid userId)
    {
        try
        {
            var apiKeyResult = await _apiKeyService.IssueApiKeyAsync(userId);
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

    /// <summary>
    /// ApiKeyを検証し、対応する tenantId / userId 情報を返します。
    /// </summary>
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

    /// <summary>
    /// テナント用 Auth Schema (auth_{tenantId}) を作成・セットアップします。
    /// </summary>
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

    /// <summary>
    /// 既存テナント(auth_{tenantId})の未適用Migrationを実行します。
    /// </summary>
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

    /// <summary>
    /// テナント用 Auth Schema (auth_{tenantId}) を削除します。
    /// </summary>
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

    /// <summary>
    /// グローバルユーザーをテナントメンバーとして追加し、ロールを割り当てます (auth_{tenantId})。
    /// </summary>
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

    /// <summary>
    /// テナント内のメンバー一覧（ロール・パーミッションを含む）を取得します。
    /// </summary>
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

    /// <summary>
    /// テナントから指定ユーザーの所属を解除します (auth_{tenantId})。
    /// ※ auth_global のユーザー自体は削除されません。
    /// </summary>
    [HttpDelete("tenants/{tenantId}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveTenantMember(string tenantId, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest(new { error = "tenantId が指定されていません。" });
        }

        try
        {
            await _userManagementService.RemoveMemberFromTenantAsync(tenantId, userId);

            return Ok(new
            {
                tenantId,
                userId,
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