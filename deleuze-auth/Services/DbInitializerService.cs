using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Npgsql; // PostgreSQLの場合 (SQL Serverなら SqlConnection / Microsoft.Data.SqlClient)

namespace DeleuzeAuth.Services;

public interface IDbInitializerService
{
    Task ExecuteInitSqlAsync();
}

public class DbInitializerService : IDbInitializerService
{
    private readonly string _connectionString;
    private readonly string _sqlFilePath;

    public DbInitializerService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("authConnection") 
            ?? throw new System.InvalidOperationException("authConnection 接続文字列が設定されていません。");
        
        // 出力ディレクトリからの相対パス
        _sqlFilePath = Path.Combine(System.AppContext.BaseDirectory, "Data", "Scripts", "init.sql");
    }

    public async Task ExecuteInitSqlAsync()
    {
        if (!File.Exists(_sqlFilePath))
        {
            throw new FileNotFoundException($"SQLファイルが見つかりません: {_sqlFilePath}");
        }

        string sql = await File.ReadAllTextAsync(_sqlFilePath);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}