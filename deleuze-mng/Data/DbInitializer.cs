using System;
using System.Threading.Tasks;
using Npgsql;
using Dapper;

namespace DeleuzeMng.Data
{
    public static class DbInitializer
    {
        public static async Task EnsureSeedDataAsync(string connectionString)
        {
            int retryCount = 0;
            const int maxRetries = 5;
            const int delayMilliseconds = 3000;

            while (retryCount < maxRetries)
            {
                try
                {
                    using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync();

                    // 💡 カラム名を ""Password"" から ""PasswordHash"" に修正（Service側と一致させる）
                    var createTableSql = @"
                        CREATE TABLE IF NOT EXISTS public.""Users"" (
                            ""Id"" SERIAL PRIMARY KEY,
                            ""LoginId"" VARCHAR(100) NOT NULL UNIQUE,
                            ""PasswordHash"" VARCHAR(255) NOT NULL, 
                            ""TenantId"" VARCHAR(100) NOT NULL,
                            ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                        );";

                    await connection.ExecuteAsync(createTableSql);
                    
                    Console.WriteLine("[INIT-SUCCESS] public.\"Users\" テーブルの整合性を確認・自動生成しました。");
                    return; // 💡 成功したら処理を抜ける
                }
                catch (Exception ex)
                {
                    retryCount++;
                    Console.Error.WriteLine($"[INIT-RETRY] データベース接続に失敗しました。{delayMilliseconds / 1000}秒後に再試行します ({retryCount}/{maxRetries}): {ex.Message}");
                    
                    if (retryCount >= maxRetries)
                    {
                        Console.Error.WriteLine("[INIT-FATAL-ERROR] リトライ上限に達したため、初期化を断念します。");
                        // 起動を完全にストップさせてNomadに再起動を委ねる場合は rethrow
                        throw;
                    }

                    await Task.Delay(delayMilliseconds);
                }
            }
        }
    }
}