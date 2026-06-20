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
            try
            {
                using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                // 全テナントの認証情報を横断管理する共通の Users テーブルを定義
                var createTableSql = @"
                    CREATE TABLE IF NOT EXISTS public.""Users"" (
                        ""Id"" SERIAL PRIMARY KEY,
                        ""LoginId"" VARCHAR(100) NOT NULL UNIQUE,
                        ""Password"" VARCHAR(255) NOT NULL,
                        ""TenantId"" VARCHAR(100) NOT NULL,
                        ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                    );";

                await connection.ExecuteAsync(createTableSql);
                
                // コンテナの標準出力（Nomadのログ）に刻まれるメッセージ
                Console.WriteLine("[INIT] public.\"Users\" テーブルの整合性を確認・自動生成しました。");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[INIT ERROR] データベースの初期化に失敗しました: {ex.Message}");
                // 必要に応じて、ここでアプリケーションの起動を完全に停止させたい場合は rethrow します
                // throw;
            }
        }
    }
}