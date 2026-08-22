using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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