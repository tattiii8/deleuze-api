using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using DeleuzeMng.Services;

namespace DeleuzeMng.Controllers
{
    [ApiController]
    [Route("users")]
    public class UsersController : ControllerBase
    {
        private readonly TenantManagementService _mngService;

        public UsersController(TenantManagementService mngService)
        {
            _mngService = mngService;
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _mngService.GetUsersAsync();
            return Ok(users);
        }

        [HttpPost]
        public async Task<IActionResult> RegisterUser([FromBody] UserRegistrationRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.LoginId) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.TenantId))
                return BadRequest(new { error = "すべての項目を入力してください。" });

            string normalizedTenantId = req.TenantId.ToLower();

            try
            {
                var existingTenants = await _mngService.GetTenantsAsync();
                if (!existingTenants.Any(t => t.TenantId == normalizedTenantId))
                {
                    await _mngService.CreateTenantAsync(normalizedTenantId);
                }

                await _mngService.RegisterUserAsync(req.LoginId, req.Password, normalizedTenantId);
                return Ok(new { message = $"テナント '{normalizedTenantId}' にユーザー '{req.LoginId}' を登録しました。" });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            bool deleted = await _mngService.DeleteUserAsync(id);
            return deleted ? NoContent() : NotFound(new { error = "指定されたユーザーが見つかりません。" });
        }
    }
}