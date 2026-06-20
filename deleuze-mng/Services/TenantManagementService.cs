using Dapper;
using Npgsql;

namespace DeleuzeMng.Services;

public class TenantManagementService

{
    private readonly string _connectionString;

    public TenantManagementService(string connectionString)
    {
        _connectionString = connectionString;
    }

    // 1. テナントの動的作成（物理スキーマ ＆ テーブルの自動生成）
    public async Task CreateTenantAsync(string tenantId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        using var tx = await conn.BeginTransactionAsync();
        try
        {
            // ① 動的にスキーマを作成 (SQLインジェクション対策として英数字チェックを挟むのが本番では推奨)
            await conn.ExecuteAsync($@"CREATE SCHEMA IF NOT EXISTS ""{tenantId}"";", transaction: tx);

            // ② 新スキーマ内に Products テーブルを動的に作成
            await conn.ExecuteAsync($@"
                CREATE TABLE IF NOT EXISTS ""{tenantId}"".""Products"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""Name"" VARCHAR(256) NOT NULL
                );", transaction: tx);

            // ③ 初期データの投入
            await conn.ExecuteAsync($@"
                INSERT INTO ""{tenantId}"".""Products"" (""Name"") 
                VALUES ('{tenantId}専用の初期データ');", transaction: tx);

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // 2. 新規ユーザーの登録（BCryptでのハッシュ化 ＆ 共通DBへのインサート）
    public async Task RegisterUserAsync(string loginId, string rawPassword, string tenantId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        
        // パスワードを安全にハッシュ化 (WorkFactor=11)
        string passwordHash = BCrypt.Net.BCrypt.HashPassword(rawPassword, workFactor: 11);

        var sql = @"
            INSERT INTO ""public"".""Users"" (""LoginId"", ""PasswordHash"", ""TenantId"")
            VALUES (@LoginId, @PasswordHash, @TenantId)
            ON CONFLICT (""LoginId"") DO NOTHING;";

        await conn.ExecuteAsync(sql, new { LoginId = loginId, PasswordHash = passwordHash, TenantId = tenantId });
    }
}