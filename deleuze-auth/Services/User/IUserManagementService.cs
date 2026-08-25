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
    private readonly GlobalAuthDbContext _globalDbContext;
    private readonly TenantAuthDbContext _tenantDbContext;
    private readonly IPasswordHasher _passwordHasher;

    public UserManagementService(
        GlobalAuthDbContext globalDbContext,
        TenantAuthDbContext tenantDbContext,
        IPasswordHasher passwordHasher)
    {
        _globalDbContext = globalDbContext;
        _tenantDbContext = tenantDbContext;
        _passwordHasher = passwordHasher;
    }

    // ==========================================================
    // Global User Management (auth_global)
    // ==========================================================

    public async Task<GlobalUserDto> CreateGlobalUserAsync(CreateGlobalUserRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new ArgumentException("email is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("password is required.", nameof(request));
        }

        var exists = await _globalDbContext.Users
            .AnyAsync(u => u.Email == request.Email);

        if (exists)
        {
            throw new DuplicateUserException($"Email '{request.Email}' はすでに使用されています。");
        }

        var user = new Models.GlobalUser
        {
            LoginId = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };

        _globalDbContext.Users.Add(user);
        await _globalDbContext.SaveChangesAsync();

        return new GlobalUserDto
        {
            LoginId = user.LoginId,
            Email = user.Email,
            CreatedAt = user.CreatedAt
        };
    }

    // ==========================================================
    // Tenant Member Management (auth_{tenantId})
    // ==========================================================

    public async Task<TenantMemberDto> AddMemberToTenantAsync(string tenantId, AddTenantMemberRequest request)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("tenantId is required.", nameof(tenantId));
        }

        if (request == null || request.LoginId == Guid.Empty)
        {
            throw new ArgumentException("loginId is required.", nameof(request));
        }

        var globalUserExists = await _globalDbContext.Users
            .AnyAsync(u => u.LoginId == request.LoginId);

        if (!globalUserExists)
        {
            throw new UserNotFoundException($"loginId '{request.LoginId}' のグローバルユーザーが存在しません。");
        }

        var memberExists = await _tenantDbContext.Members
            .AnyAsync(m => m.LoginId == request.LoginId);

        if (memberExists)
        {
            throw new DuplicateUserException($"loginId '{request.LoginId}' はすでにテナント '{tenantId}' に所属しています。");
        }

        var member = new Models.TenantMember
        {
            LoginId = request.LoginId,
            TenantId = tenantId,
            Role = string.IsNullOrWhiteSpace(request.Role) ? "Member" : request.Role,
            JoinedAt = DateTime.UtcNow
        };

        _tenantDbContext.Members.Add(member);
        await _tenantDbContext.SaveChangesAsync();

        return new TenantMemberDto
        {
            LoginId = member.LoginId,
            TenantId = member.TenantId,
            Role = member.Role,
            JoinedAt = member.JoinedAt
        };
    }

    public async Task<List<TenantMemberDto>> GetTenantMembersAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("tenantId is required.", nameof(tenantId));
        }

        var members = await _tenantDbContext.Members
            .OrderBy(m => m.JoinedAt)
            .ToListAsync();

        return members.Select(m => new TenantMemberDto
        {
            LoginId = m.LoginId,
            TenantId = tenantId,
            Role = m.Role,
            JoinedAt = m.JoinedAt
        }).ToList();
    }

    public async Task RemoveMemberFromTenantAsync(string tenantId, Guid loginId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("tenantId is required.", nameof(tenantId));
        }

        if (loginId == Guid.Empty)
        {
            throw new ArgumentException("loginId is required.", nameof(loginId));
        }

        var member = await _tenantDbContext.Members
            .FirstOrDefaultAsync(m => m.LoginId == loginId);

        if (member == null)
        {
            throw new UserNotFoundException($"loginId '{loginId}' がテナント '{tenantId}' 内に見つかりません。");
        }

        _tenantDbContext.Members.Remove(member);
        await _tenantDbContext.SaveChangesAsync();
    }
}