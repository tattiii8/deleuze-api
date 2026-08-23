using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DeleuzeMng.Models;
using DeleuzeMng.Services;

namespace DeleuzeMng.Controllers
{
    [ApiController]
    [Authorize]
    [Route("users")] // POST/GET /api/mng/users
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
            var users = await _tenantService.GetAllUsersAsync();
            return Ok(users);
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            var user = await _tenantService.CreateUserAsync(request);
            return Ok(user);
        }
    }
}