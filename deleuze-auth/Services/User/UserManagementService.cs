using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Data;
using DeleuzeAuth.Services.Authentication;

namespace DeleuzeAuth.Services.User;

public class UserManagementService : IUserManagementService
{
    private readonly TenantAuthDbContext _tenantDbContext;
    private readonly IPasswordHasher _passwordHasher;

    public UserManagementService(
        TenantAuthDbContext tenantDbContext,
        IPasswordHasher passwordHasher)
    {
        _tenantDbContext = tenantDbContext;
        _passwordHasher = passwordHasher;
    }

    // ==========================================================
    // User Registration
    // ==========================================================

    public async Task<UserDto> RegisterUserAsync(
        string tenantId,
        RegisterUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "Tenant ID is required.",
                nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(request.LoginId))
        {
            throw new ArgumentException(
                "LoginId is required.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException(
                "Password is required.",
                nameof(request));
        }

        // ------------------------------------------------------
        // LoginId 重複チェック
        //
        // TenantAuthDbContext は
        // auth_{tenantId} Schemaを参照しているため、
        // この検索は対象テナント内だけで行われる。
        // ------------------------------------------------------

        var exists =
            await _tenantDbContext.Users
                .AnyAsync(u =>
                    u.LoginId == request.LoginId);

        if (exists)
        {
            throw new DuplicateUserException(
                $"LoginId '{request.LoginId}' はすでに使用されています。");
        }

        // ------------------------------------------------------
        // User作成
        // ------------------------------------------------------

        var user = new Models.User
        {
            LoginId = request.LoginId,
            PasswordHash =
                _passwordHasher.HashPassword(
                    request.Password),
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow
        };

        _tenantDbContext.Users.Add(user);

        await _tenantDbContext.SaveChangesAsync();

        return MapToDto(user);
    }

    // ==========================================================
    // Get Users
    // ==========================================================

    public async Task<List<UserDto>> GetUsersAsync(
        string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "Tenant ID is required.",
                nameof(tenantId));
        }

        var users =
            await _tenantDbContext.Users
                .Where(u =>
                    u.TenantId == tenantId)
                .OrderBy(u =>
                    u.CreatedAt)
                .ToListAsync();

        return users
            .Select(MapToDto)
            .ToList();
    }

    // ==========================================================
    // Delete User
    // ==========================================================

    public async Task DeleteUserAsync(
        string tenantId,
        int userId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "Tenant ID is required.",
                nameof(tenantId));
        }

        var user =
            await _tenantDbContext.Users
                .FirstOrDefaultAsync(u =>
                    u.Id == userId &&
                    u.TenantId == tenantId);

        if (user == null)
        {
            throw new UserNotFoundException(
                $"User '{userId}' がテナント '{tenantId}' 内に見つかりません。");
        }

        _tenantDbContext.Users.Remove(user);

        await _tenantDbContext.SaveChangesAsync();
    }

    // ==========================================================
    // Mapping
    // ==========================================================

    private static UserDto MapToDto(
        Models.User user)
    {
        return new UserDto
        {
            Id = user.Id,
            LoginId = user.LoginId,
            TenantId = user.TenantId,
            CreatedAt = user.CreatedAt
        };
    }
}

// ==========================================================
// Exceptions
// ==========================================================

public class DuplicateUserException : Exception
{
    public DuplicateUserException(
        string message)
        : base(message)
    {
    }
}

public class UserNotFoundException : Exception
{
    public UserNotFoundException(
        string message)
        : base(message)
    {
    }
}