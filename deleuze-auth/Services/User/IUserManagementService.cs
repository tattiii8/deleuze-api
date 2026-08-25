using System.Collections.Generic;
using System.Threading.Tasks;

namespace DeleuzeAuth.Services.User;

public interface IUserManagementService
{
    Task<UserDto> RegisterUserAsync(string tenantId, RegisterUserRequest request);

    Task<List<UserDto>> GetUsersAsync(string tenantId);

    Task DeleteUserAsync(string tenantId, int userId);
}

public class RegisterUserRequest
{
    public string LoginId { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public class UserDto
{
    public int Id { get; set; }

    public string LoginId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public System.DateTime CreatedAt { get; set; }
}