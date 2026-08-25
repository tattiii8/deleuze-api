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
        _connectionString =
            connectionString
            ?? throw new ArgumentNullException(
                nameof(connectionString));

        _passwordHasher = passwordHasher;
    }

    public async Task<bool> AuthenticateAsync(
        string tenantId,
        string loginId,
        string password)
    {
        var schemaName =
            TenantSchemaNaming.GetSchemaName(
                "auth",
                tenantId);

        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync();

        var passwordHash =
            await connection.QueryFirstOrDefaultAsync<string>(
                $@"
                SELECT ""PasswordHash""
                FROM ""{schemaName}"".""Users""
                WHERE ""LoginId"" = @LoginId;
                ",
                new
                {
                    LoginId = loginId
                });

        if (passwordHash == null)
        {
            return false;
        }

        return _passwordHasher.VerifyPassword(
            password,
            passwordHash);
    }
}