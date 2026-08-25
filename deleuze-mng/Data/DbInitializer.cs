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
                    using var connection =
                        new NpgsqlConnection(authConnectionString);

                    await connection.OpenAsync();

                    // =========================================================
                    // 1. Users テーブルの生成
                    // =========================================================

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


                    // =========================================================
                    // 2. Tenants テーブルの生成
                    // =========================================================

                    const string createTenantsTableSql = @"
                        CREATE TABLE IF NOT EXISTS public.""Tenants"" (
                            ""Id"" VARCHAR(100) PRIMARY KEY,
                            ""Name"" VARCHAR(255) NOT NULL,
                            ""ApiKey"" VARCHAR(255),
                            ""AuthMode"" INT NOT NULL DEFAULT 0,
                            ""Status"" VARCHAR(20) NOT NULL DEFAULT 'active',
                            ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                        );";

                    await connection.ExecuteAsync(createTenantsTableSql);


                    // =========================================================
                    // 3. 既存 Tenants テーブルへの Status カラム追加
                    //
                    // CREATE TABLE IF NOT EXISTS では、
                    // 既に存在するテーブルのカラムは追加されないため、
                    // 明示的に ADD COLUMN IF NOT EXISTS を実行する。
                    // =========================================================

                    const string addTenantStatusColumnSql = @"
                        ALTER TABLE public.""Tenants""
                        ADD COLUMN IF NOT EXISTS
                            ""Status"" VARCHAR(20) NOT NULL DEFAULT 'active';";

                    await connection.ExecuteAsync(addTenantStatusColumnSql);


                    // =========================================================
                    // 4. 既存データの Status を補正
                    //
                    // 念のため NULL が存在する場合は active にする。
                    // =========================================================

                    const string updateNullTenantStatusSql = @"
                        UPDATE public.""Tenants""
                        SET ""Status"" = 'active'
                        WHERE ""Status"" IS NULL;";

                    await connection.ExecuteAsync(updateNullTenantStatusSql);


                    // =========================================================
                    // 5. ApiKey のユニークインデックス
                    // =========================================================

                    const string createTenantsIndexSql = @"
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Tenants_ApiKey""
                        ON public.""Tenants"" (""ApiKey"")
                        WHERE ""ApiKey"" IS NOT NULL;";

                    await connection.ExecuteAsync(createTenantsIndexSql);


                    // =========================================================
                    // 初期化完了
                    // =========================================================

                    Console.WriteLine(
                        "[INIT-SUCCESS] public.\"Users\" および " +
                        "public.\"Tenants\" テーブルの整合性を確認・自動生成しました。");

                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;

                    Console.Error.WriteLine(
                        $"[INIT-RETRY] データベース接続に失敗しました。" +
                        $"{delayMilliseconds / 1000}秒後に再試行します " +
                        $"({retryCount}/{maxRetries}): {ex.Message}");

                    if (retryCount >= maxRetries)
                    {
                        Console.Error.WriteLine(
                            "[INIT-FATAL-ERROR] リトライ上限に達したため、初期化を断念します。");

                        throw;
                    }

                    await Task.Delay(delayMilliseconds);
                }
            }
        }
    }
}