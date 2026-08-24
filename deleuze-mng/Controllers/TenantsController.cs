using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DeleuzeMng.Services;
using DeleuzeMng.Models;
using Deleuze.Shared.Constants; // 共通定数を参照

namespace DeleuzeMng.Controllers
{
    [ApiController]
    [Route(ApiRoutes.Management.Tenants)]
    public class TenantsController : ControllerBase
    {
        private readonly ITenantManagementService _tenantService;

        public TenantsController(ITenantManagementService tenantService)
        {
            _tenantService = tenantService;
        }

        [HttpGet]
        public async Task<IActionResult> GetTenants()
        {
            var tenants = await _tenantService.GetTenantsAsync();
            return Ok(tenants);
        }

        [HttpGet("{tenantId}")]
        public async Task<IActionResult> GetTenantById(string tenantId)
        {
            var tenant = await _tenantService.GetTenantByIdAsync(tenantId);
            if (tenant is null)
            {
                return NotFound("該当するテナントが見つかりません。");
            }

            return Ok(tenant);
        }

        [HttpPost]
        public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TenantId))
            {
                return BadRequest("TenantId は必須です。");
            }

            var success = await _tenantService.CreateTenantAsync(request.TenantId, request.Name ?? request.TenantId);
            
            if (request.Services != null && request.Services.Count > 0)
            {
                foreach (var serviceKey in request.Services)
                {
                    await _tenantService.EnableServiceForTenantAsync(request.TenantId, serviceKey);
                }
            }

            return success ? Ok() : StatusCode(500, "テナントの作成に失敗しました。");
        }

        [HttpPost("{tenantId}/services")]
        public async Task<IActionResult> EnableService(string tenantId, [FromBody] EnableServiceRequest request)
        {
            var success = await _tenantService.EnableServiceForTenantAsync(tenantId, request.ServiceKey);
            return success ? Ok() : StatusCode(500, "サービスの有効化に失敗しました。");
        }

        [HttpDelete("{tenantId}/services")]
        public async Task<IActionResult> DisableService(string tenantId, [FromBody] DisableServiceRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ServiceKey))
            {
                return BadRequest("ServiceKey は必須です。");
            }

            var success = await _tenantService.DisableServiceForTenantAsync(tenantId, request.ServiceKey);
            return success ? Ok() : StatusCode(500, "サービスの無効化に失敗しました。");
        }

        // 指定テナントの全サービススキーマに対してマイグレーションを実行
        [HttpPost("{tenantId}/migrate")]
        public async Task<IActionResult> MigrateTenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return BadRequest("テナントIDが無効です。");
            }

            var success = await _tenantService.MigrateAllServicesForTenantAsync(tenantId);
            
            if (!success)
            {
                return StatusCode(500, new { error = $"テナント '{tenantId}' のマイグレーション処理中にエラーが発生しました。" });
            }

            return Ok(new { message = $"Tenant '{tenantId}' migrated successfully across all services." });
        }

        // マイグレーション履歴を取得するエンドポイント
        [HttpGet("{tenantId}/migrations")]
        public async Task<IActionResult> GetMigrations(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return BadRequest("テナントIDが無効です。");
            }

            try
            {
                var history = await _tenantService.GetTenantMigrationsAsync(tenantId);
                return Ok(history);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"マイグレーション履歴の取得に失敗しました: {ex.Message}" });
            }
        }

        // ヘルスチェックを実行するエンドポイント
        [HttpGet("{tenantId}/health")]
        public async Task<IActionResult> CheckHealth(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return BadRequest("テナントIDが無効です。");
            }

            try
            {
                var health = await _tenantService.CheckTenantHealthAsync(tenantId);
                return Ok(health);
            }
            catch (Exception ex)
            {
                return Ok(new { dbStatus = "Unreachable", storageStatus = "Unreachable", message = ex.Message });
            }
        }

        // テナントのステータス変更（一時停止 / 有効化）
        [HttpPatch("{tenantId}/status")]
        public async Task<IActionResult> UpdateStatus(string tenantId, [FromBody] UpdateStatusRequest request)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || request == null)
            {
                return BadRequest("リクエストが無効です。");
            }

            var success = await _tenantService.UpdateTenantStatusAsync(tenantId, request.Status);
            return success ? Ok(new { message = $"テナント '{tenantId}' のステータスを更新しました。" }) : StatusCode(500, "ステータスの更新に失敗しました。");
        }

        [HttpPost("{tenantId}/apikey")]
        public async Task<IActionResult> GenerateApiKey(string tenantId)
        {
            var apiKey = await _tenantService.GenerateApiKeyAsync(tenantId);
            return Ok(new { apiKey });
        }

        [HttpPatch("{tenantId}/authmode")]
        public async Task<IActionResult> UpdateAuthMode(string tenantId, [FromBody] UpdateAuthModeRequest request)
        {
            var success = await _tenantService.UpdateAuthModeAsync(tenantId, (int)request.AuthMode);
            return success ? Ok() : StatusCode(500, "認証モードの更新に失敗しました。");
        }

        [HttpDelete("{tenantId}")]
        public async Task<IActionResult> DeleteTenant(string tenantId)
        {
            var success = await _tenantService.DeleteTenantAsync(tenantId);
            return success ? Ok() : NotFound("該当するテナントが見つかりません。");
        }
    }

    public class UpdateStatusRequest
    {
        public string Status { get; set; } = "active";
    }
}