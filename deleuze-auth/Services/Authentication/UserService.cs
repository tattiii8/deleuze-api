using System;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Deleuze.Shared.Infrastructure;
using DeleuzeAuth.Services.Authentication;

namespace DeleuzeAuth.Services;

public class UserService : IUserService
{
    private readonly string _connectionString;
    private readonly IPasswordHasher _passwordHasher;

    public UserService(
        string connectionString,
        IPasswordHasher passwordHasher)
    {
        _connectionString = connectionString 
            ?? throw new ArgumentNullException(nameof(connectionString));
        _passwordHasher = passwordHasher;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(
        string tenantId,
        Guid loginId,
        string password)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return new AuthenticationResult 
            { 
                IsSuccess = false, 
                ErrorMessage = "tenantId is required." 
            };
        }

        if (loginId == Guid.Empty || string.IsNullOrWhiteSpace(password))
        {
            return new AuthenticationResult 
            { 
                IsSuccess = false, 
                ErrorMessage = "loginId and password are required." 
            };
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // ------------------------------------------------------
        // ステップ1: auth_global.users から login_id をキーに取得
        // ------------------------------------------------------
        const string globalUserSql = @"
            SELECT password_hash AS PasswordHash
            FROM auth_global.users
            WHERE login_id = @LoginId;
        ";

        var passwordHash = await connection.QueryFirstOrDefaultAsync<string>(
            globalUserSql,
            new { LoginId = loginId }
        );

        if (string.IsNullOrEmpty(passwordHash))
        {
            return new AuthenticationResult 
            { 
                IsSuccess = false, 
                ErrorMessage = "ログインIDまたはパスワードが正しくありません。" 
            };
        }

        // パスワード検証
        var isValidPassword = _passwordHasher.VerifyPassword(password, passwordHash);
        if (!isValidPassword)
        {
            return new AuthenticationResult 
            { 
                IsSuccess = false, 
                ErrorMessage = "ログインIDまたはパスワードが正しくありません。" 
            };
        }

        // ------------------------------------------------------
        // ステップ2: 対象テナント (auth_{tenantId}.members) への所属確認
        // ------------------------------------------------------
        var tenantSchema = TenantSchemaNaming.GetSchemaName("auth", tenantId);
        var memberCheckSql = $@"
            SELECT COUNT(1)
            FROM ""{tenantSchema}"".""members""
            WHERE login_id = @LoginId;
        ";

        try
        {
            var isMember = await connection.ExecuteScalarAsync<bool>(
                memberCheckSql,
                new { LoginId = loginId }
            );

            if (!isMember)
            {
                return new AuthenticationResult 
                { 
                    IsSuccess = false, 
                    ErrorMessage = $"ユーザーは指定されたテナント '{tenantId}' に所属していません。" 
                };
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "3F000") // スキーマが存在しない場合
        {
            return new AuthenticationResult 
            { 
                IsSuccess = false, 
                ErrorMessage = $"指定されたテナント '{tenantId}' が見つかりません。" 
            };
        }

        return new AuthenticationResult
        {
            IsSuccess = true,
            LoginId = loginId
        };
    }
}