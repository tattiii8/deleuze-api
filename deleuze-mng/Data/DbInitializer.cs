using System;
using System.Threading.Tasks;
using Npgsql;
using Dapper;

namespace DeleuzeMng.Data
{
    /// <summary>
    /// 認証DB(Auth DB)側の初期化処理。
    /// アプリ起動時に Users および Tenants テーブルの存在を保証する。
    /// </summary>
    public static class DbInitializer
    {
        public static async Task EnsureSeedDataAsync(string authConnectionString)
        {
            int retryCount = 0;
            const int maxRetries = 5;
            const int delayMilliseconds = 3000;

            while (retryCount < maxRetries)
            {
                try
                {
                    using var connection = new NpgsqlConnection(authConnectionString);
                    await connection.OpenAsync();

                    // 1. Users テーブルの生成
                    const string createUsersTableSql = @"
                        CREATE TABLE IF NOT EXISTS public.""Users"" (
                            ""Id"" SERIAL PRIMARY KEY,
                            ""LoginId"" VARCHAR(100) NOT NULL UNIQUE,
                            ""PasswordHash"" VARCHAR(255) NOT NULL,
                            ""TenantId"" VARCHAR(100) NOT NULL,
                            ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                        );";

                    await connection.ExecuteAsync(createUsersTableSql);

                    const string createUsersIndexSql = @"
                        CREATE INDEX IF NOT EXISTS ""IX_Users_TenantId""
                        ON public.""Users"" (""TenantId"");";

                    await connection.ExecuteAsync(createUsersIndexSql);

                    // 2. Tenants テーブルの生成（API Key & AuthMode 管理用）
                    const string createTenantsTableSql = @"
                        CREATE TABLE IF NOT EXISTS public.""Tenants"" (
                            ""Id"" VARCHAR(100) PRIMARY KEY,
                            ""Name"" VARCHAR(255) NOT NULL,
                            ""ApiKey"" VARCHAR(255),
                            ""AuthMode"" INT NOT NULL DEFAULT 0,
                            ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                        );";

                    await connection.ExecuteAsync(createTenantsTableSql);

                    const string createTenantsIndexSql = @"
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Tenants_ApiKey""
                        ON public.""Tenants"" (""ApiKey"")
                        WHERE ""ApiKey"" IS NOT NULL;";

                    await connection.ExecuteAsync(createTenantsIndexSql);

                    Console.WriteLine("[INIT-SUCCESS] public.\"Users\" および public.\"Tenants\" テーブルの整合性を確認・自動生成しました。");
                    return; // 成功したら処理を抜ける
                }
                catch (Exception ex)
                {
                    retryCount++;
                    Console.Error.WriteLine(
                        $"[INIT-RETRY] データベース接続に失敗しました。{delayMilliseconds / 1000}秒後に再試行します ({retryCount}/{maxRetries}): {ex.Message}");

                    if (retryCount >= maxRetries)
                    {
                        Console.Error.WriteLine("[INIT-FATAL-ERROR] リトライ上限に達したため、初期化を断念します。");
                        throw;
                    }

                    await Task.Delay(delayMilliseconds);
                }
            }
        }
    }
}