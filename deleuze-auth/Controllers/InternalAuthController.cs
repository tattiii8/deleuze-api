using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using DeleuzeAuth.Services.Tenant;
using DeleuzeAuth.Services.User;

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

    public InternalAuthController(
        ITenantProvisioningService provisioningService,
        ITenantMigrationService migrationService,
        ITenantDeprovisioningService deprovisioningService,
        IUserManagementService userManagementService)
    {
        _provisioningService = provisioningService;
        _migrationService = migrationService;
        _deprovisioningService = deprovisioningService;
        _userManagementService = userManagementService;
    }

    // ==========================================================
    // Tenant Provisioning
    // ==========================================================

    /// <summary>
    /// テナント用 Auth Schema を作成し、
    /// Tenant Migration を適用します。
    ///
    /// auth_{tenantId} Schemaが対象です。
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
            await _provisioningService.ProvisionAsync(
                tenantId);

            return Ok(new
            {
                tenant_id = tenantId,
                service = "auth",
                schema = $"auth_{tenantId}",
                status = "provisioned"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error =
                        "テナントのProvisioningに失敗しました。",
                    message = ex.Message
                });
        }
    }

    // ==========================================================
    // Tenant Migration
    // ==========================================================

    /// <summary>
    /// 既存テナントの未適用Migrationを実行します。
    ///
    /// auth_{tenantId} Schemaが存在していることを前提とします。
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
            await _migrationService.MigrateAsync(
                tenantId);

            return Ok(new
            {
                tenant_id = tenantId,
                service = "auth",
                schema = $"auth_{tenantId}",
                status = "migrated"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error =
                        "テナントのMigrationに失敗しました。",
                    message = ex.Message
                });
        }
    }

    // ==========================================================
    // Tenant Deprovisioning
    // ==========================================================

    /// <summary>
    /// テナント用 Auth Schema を削除します。
    ///
    /// auth_{tenantId} SchemaをCASCADEで削除します。
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
            await _deprovisioningService.DeprovisionAsync(
                tenantId);

            return Ok(new
            {
                tenant_id = tenantId,
                service = "auth",
                schema = $"auth_{tenantId}",
                status = "deprovisioned"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error =
                        "テナントのDeprovisioningに失敗しました。",
                    message = ex.Message
                });
        }
    }

    // ==========================================================
    // User Management
    // ==========================================================

    /// <summary>
    /// テナントSchemaにユーザーを登録します。
    /// </summary>
    [HttpPost("tenants/{tenantId}/users")]
    public async Task<IActionResult> RegisterUser(
        string tenantId,
        [FromBody] RegisterUserRequest request)
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
            var user =
                await _userManagementService.RegisterUserAsync(
                    tenantId,
                    request);

            return Ok(user);
        }
        catch (DuplicateUserException ex)
        {
            return Conflict(new
            {
                error = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = "ユーザー登録に失敗しました。",
                    message = ex.Message
                });
        }
    }

    /// <summary>
    /// テナントSchema内のユーザー一覧を取得します。
    /// </summary>
    [HttpGet("tenants/{tenantId}/users")]
    public async Task<IActionResult> GetUsers(
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
            var users =
                await _userManagementService.GetUsersAsync(
                    tenantId);

            return Ok(users);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = "ユーザー一覧の取得に失敗しました。",
                    message = ex.Message
                });
        }
    }

    /// <summary>
    /// テナントSchema内のユーザーを削除します。
    /// </summary>
    [HttpDelete("tenants/{tenantId}/users/{userId:int}")]
    public async Task<IActionResult> DeleteUser(
        string tenantId,
        int userId)
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
            await _userManagementService.DeleteUserAsync(
                tenantId,
                userId);

            return Ok(new
            {
                tenant_id = tenantId,
                user_id = userId,
                status = "deleted"
            });
        }
        catch (UserNotFoundException)
        {
            return NotFound(new
            {
                error = "指定されたユーザーが存在しません。"
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = "ユーザー削除に失敗しました。",
                    message = ex.Message
                });
        }
    }
}