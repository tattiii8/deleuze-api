using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using DeleuzeMng.Models;
using DeleuzeMng.Services;

namespace DeleuzeMng.Controllers
{
    [ApiController]
    [Route("tenants")]
    public class TenantsController : ControllerBase
    {
        private readonly TenantManagementService _mngService;
        private readonly ILogger<TenantsController> _logger;
        private static readonly Regex TenantIdPattern = new(@"^[a-z][a-z0-9_]{2,62}$", RegexOptions.Compiled);

        public TenantsController(TenantManagementService mngService, ILogger<TenantsController> logger)
        {
            _mngService = mngService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetTenants()
        {
            var tenants = await _mngService.GetTenantsAsync();
            return Ok(tenants);
        }

        [HttpPost]
        public async Task<IActionResult> CreateTenant([FromBody] TenantCreationRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.TenantId))
                return BadRequest(new { error = "TenantId は必須です。" });

            string normalizedTenantId = req.TenantId.ToLower();
            if (!TenantIdPattern.IsMatch(normalizedTenantId))
                return BadRequest(new { error = "TenantId の形式が不正です。" });

            try
            {
                await _mngService.CreateTenantAsync(normalizedTenantId, req.EnabledServices);
                return Ok(new { message = $"テナント '{normalizedTenantId}' の構築処理が完了しました。" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "テナント作成エラー: {TenantId}", normalizedTenantId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "処理中にエラーが発生しました。" });
            }
        }

        [HttpPost("{tenantId}/services")]
        public async Task<IActionResult> EnableService(string tenantId, [FromBody] EnableServiceRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ServiceKey))
                return BadRequest(new { error = "ServiceKey は必須です。" });

            string normalizedTenantId = tenantId.ToLower();

            try
            {
                await _mngService.EnableServiceForTenantAsync(normalizedTenantId, req.ServiceKey);
                return Ok(new { message = $"テナント '{normalizedTenantId}' にサービス '{req.ServiceKey}' を追加有効化しました。" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        /// <summary>
        /// テナントの API Key を発行（または再発行）します
        /// </summary>
        [HttpPost("{tenantId}/api-key")]
        public async Task<IActionResult> GenerateApiKey(string tenantId)
        {
            string normalizedTenantId = tenantId.ToLower();

            try
            {
                var apiKey = await _mngService.GenerateApiKeyAsync(normalizedTenantId);
                return Ok(new { apiKey });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API Key 発行エラー: {TenantId}", normalizedTenantId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "API Key の発行中にエラーが発生しました。" });
            }
        }

        /// <summary>
        /// テナントの認証モード（JwtOnly, ApiKeyOnly, Both）を変更します
        /// </summary>
        [HttpPatch("{tenantId}/auth-mode")]
        public async Task<IActionResult> UpdateAuthMode(string tenantId, [FromBody] UpdateAuthModeRequest req)
        {
            string normalizedTenantId = tenantId.ToLower();

            try
            {
                await _mngService.UpdateAuthModeAsync(normalizedTenantId, req.AuthMode);
                return Ok(new
                {
                    message = $"テナント '{normalizedTenantId}' の認証モードを '{req.AuthMode}' に更新しました。",
                    tenantId = normalizedTenantId,
                    authMode = req.AuthMode.ToString()
                });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "認証モード更新エラー: {TenantId}", normalizedTenantId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "認証モードの更新中にエラーが発生しました。" });
            }
        }

        [HttpDelete("{tenantId}")]
        public async Task<IActionResult> DeleteTenant(string tenantId)
        {
            try
            {
                await _mngService.DeleteTenantAsync(tenantId.ToLower());
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "テナント削除エラー: {TenantId}", tenantId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "削除処理中にエラーが発生しました。" });
            }
        }
    }
}