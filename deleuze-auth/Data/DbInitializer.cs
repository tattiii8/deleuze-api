using System;
using System.Threading.Tasks;
using Npgsql;

namespace DeleuzeAuth.Data;

/// </summary>
public static class DbInitializer
{
    public static async Task EnsureDatabaseAsync(
        string connectionString)
    {
        int retryCount = 0;

        const int maxRetries = 5;
        const int delayMilliseconds = 3000;

        while (retryCount < maxRetries)
        {
            try
            {
                await using var connection =
                    new NpgsqlConnection(connectionString);

                await connection.OpenAsync();

                Console.WriteLine(
                    "[INIT-SUCCESS] " +
                    "DeleuzeAuth DBへの接続を確認しました。");

                return;
            }
            catch (Exception ex)
            {
                retryCount++;

                Console.Error.WriteLine(
                    $"[INIT-RETRY] " +
                    $"データベース接続に失敗しました。" +
                    $"{delayMilliseconds / 1000}秒後に再試行します " +
                    $"({retryCount}/{maxRetries}): " +
                    ex.Message);

                if (retryCount >= maxRetries)
                {
                    Console.Error.WriteLine(
                        "[INIT-FATAL-ERROR] " +
                        "リトライ上限に達したため、" +
                        "DB初期化を断念します。");

                    throw;
                }

                await Task.Delay(
                    delayMilliseconds);
            }
        }
    }
}