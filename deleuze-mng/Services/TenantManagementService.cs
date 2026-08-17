using Npgsql;
using BCrypt.Net;

namespace DeleuzeMng.Services;

public class TenantManagementService
{
    private readonly IConfiguration _configuration;

    public TenantManagementService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task CreateTenantAsync(string tenantId, string adminLoginId, string adminPassword)
    {
        var appDbConnString = _configuration.GetConnectionString("AppConnection");
        var authDbConnString = _configuration.GetConnectionString("AuthConnection");

        // 1. App DB (業務DB) にスキーマを作成
        await using var appConn = new NpgsqlConnection(appDbConnString);
        await appConn.OpenAsync();
        
        await using var createSchemaCmd = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS \"{tenantId}\";", appConn);
        await createSchemaCmd.ExecuteNonQueryAsync();

        // 必要に応じて App DB のマイグレーションスクリプトをここで実行（またはEF Coreの機能を利用）
        // await using var initTableCmd = new NpgsqlCommand($"CREATE TABLE \"{tenantId}\".\"Products\" (...)", appConn);
        // await initTableCmd.ExecuteNonQueryAsync();

        // 2. Auth DB (認証DB) にユーザーを登録
        await using var authConn = new NpgsqlConnection(authDbConnString);
        await authConn.OpenAsync();

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);
        
        await using var insertUserCmd = new NpgsqlCommand(
            "INSERT INTO public.\"Users\" (\"LoginId\", \"PasswordHash\", \"TenantId\") VALUES (@loginId, @hash, @tenantId)", authConn);
        
        insertUserCmd.Parameters.AddWithValue("loginId", adminLoginId);
        insertUserCmd.Parameters.AddWithValue("hash", passwordHash);
        insertUserCmd.Parameters.AddWithValue("tenantId", tenantId);
        
        await insertUserCmd.ExecuteNonQueryAsync();
    }
}