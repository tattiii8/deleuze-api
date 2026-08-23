using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DeleuzeMng.Services;
using DeleuzeMng.Models;

namespace DeleuzeMng.Controllers
{
    [ApiController]
    [Route("tenants")]
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

        [HttpPost("{tenantId}/apikey")]
        public async Task<IActionResult> GenerateApiKey(string tenantId)
        {
            var apiKey = await _tenantService.GenerateApiKeyAsync(tenantId);
            return Ok(new { apiKey });
        }

        [HttpPut("{tenantId}/authmode")]
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
}