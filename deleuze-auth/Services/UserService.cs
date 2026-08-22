using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Data;

namespace DeleuzeAuth.Services;

public class UserService : IUserService
{
    private readonly AuthDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public UserService(AuthDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task<string?> AuthenticateAndGetTenantAsync(string loginId, string password)
    {
        // 1. 認証DBからLoginIdでユーザーを検索
        var user = await _context.Users.FirstOrDefaultAsync(u => u.LoginId == loginId);
        if (user == null) return null;

        if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            return null; 
        }

        // 3. 一致したら所属するテナントIDを返却
        return user.TenantId;
    }
}