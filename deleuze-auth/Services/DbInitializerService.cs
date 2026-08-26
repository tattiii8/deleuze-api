using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DeleuzeAuth.Services;

public interface IDbInitializerService
{
    Task ExecuteInitSqlAsync();
    Task ExecuteWithRetryAsync(int maxRetries = 5, int delaySeconds = 1);
}

public class DbInitializerService : IDbInitializerService
{
    private readonly string _connectionString;
    private readonly string _sqlFilePath;
    private readonly ILogger<DbInitializerService> _logger;

    public DbInitializerService(
        IConfiguration configuration,
        ILogger<DbInitializerService> logger)
    {
        _connectionString = configuration.GetConnectionString("authConnection")
            ?? throw new InvalidOperationException(
                "authConnection 接続文字列が設定されていません。");

        // 出力ディレクトリからの相対パス
        _sqlFilePath = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "Scripts",
            "init.sql");

        _logger = logger;
    }

    /// <summary>
    /// init.sqlを実行します。
    /// </summary>
    public async Task ExecuteInitSqlAsync()
    {
        if (!File.Exists(_sqlFilePath))
        {
            throw new FileNotFoundException(
                $"SQLファイルが見つかりません: {_sqlFilePath}");
        }

        _logger.LogInformation(
            "DB初期化SQLを読み込みます: {SqlFilePath}",
            _sqlFilePath);

        string sql = await File.ReadAllTextAsync(_sqlFilePath);

        await using var connection = new NpgsqlConnection(_connectionString);

        _logger.LogInformation("DBへ接続しています...");

        await connection.OpenAsync();

        _logger.LogInformation(
            "DB接続に成功しました。SQLを実行します...");

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await command.ExecuteNonQueryAsync();

        _logger.LogInformation(
            "DB初期化SQLの実行が完了しました。");
    }

    /// <summary>
    /// DB初期化を指定回数リトライします。
    /// デフォルトは1秒間隔で最大5回です。
    /// </summary>
    public async Task ExecuteWithRetryAsync(
        int maxRetries = 5,
        int delaySeconds = 1)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "DB初期化を実行します ({Attempt}/{MaxRetries})",
                    attempt,
                    maxRetries);

                await ExecuteInitSqlAsync();

                _logger.LogInformation(
                    "DB初期化が正常に完了しました。");

                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "DB初期化に失敗しました ({Attempt}/{MaxRetries})",
                    attempt,
                    maxRetries);

                if (attempt >= maxRetries)
                {
                    _logger.LogError(
                        "DB初期化に最大試行回数 {MaxRetries} 回失敗しました。",
                        maxRetries);

                    throw;
                }

                _logger.LogInformation(
                    "{DelaySeconds}秒後にDB初期化を再試行します。",
                    delaySeconds);

                await Task.Delay(
                    TimeSpan.FromSeconds(delaySeconds));
            }
        }
    }
}