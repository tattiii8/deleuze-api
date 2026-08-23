using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DeleuzeMng.Services;

namespace DeleuzeMng.Controllers
{
    [ApiController]
    [Route("users")]
    public class UsersController : ControllerBase
    {
        private readonly ITenantManagementService _tenantService;

        public UsersController(ITenantManagementService tenantService)
        {
            _tenantService = tenantService;
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _tenantService.GetUsersAsync();
            return Ok(users);
        }

        [HttpPost]
        public async Task<IActionResult> RegisterUser([FromBody] RegisterUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.LoginId) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("LoginId と Password は必須です。");
            }

            var success = await _tenantService.RegisterUserAsync(request.LoginId, request.Password, request.TenantId);
            return success ? Ok() : StatusCode(500, "ユーザーの登録に失敗しました。");
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser([FromRoute] string id)
        {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest("ID は必須です。");
        }

        var success = await _tenantService.DeleteUserAsync(id);
        return success ? Ok() : NotFound("該当するユーザーが見つかりません。");
       }
    }

    public class RegisterUserRequest
    {
        public string LoginId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
    }
}