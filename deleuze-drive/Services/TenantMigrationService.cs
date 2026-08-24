using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DeleuzeDrive.Services
{
    public interface ITenantMigrationService
    {
        Task MigrateTenantSchemaAsync(string schemaName);
    }

    public class TenantMigrationService : ITenantMigrationService
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _env;
        private readonly ILogger<TenantMigrationService> _logger;

        public TenantMigrationService(
            IConfiguration configuration,
            IHostEnvironment env,
            ILogger<TenantMigrationService> logger)
        {
            _configuration = configuration;
            _env = env;
            _logger = logger;
        }

        public async Task MigrateTenantSchemaAsync(string schemaName)
        {
            if (string.IsNullOrWhiteSpace(schemaName))
            {
                throw new ArgumentException(
                    "Schema name is required.",
                    nameof(schemaName));
            }

            // 念のため schema 名として許可する文字を制限
            if (!schemaName.All(c =>
                    char.IsLetterOrDigit(c) ||
                    c == '_' ||
                    c == '-'))
            {
                throw new ArgumentException(
                    $"Invalid schema name: {schemaName}",
                    nameof(schemaName));
            }

            _logger.LogInformation(
                "Starting migration process for tenant schema: {SchemaName}",
                schemaName);

            var baseConnectionString =
                _configuration.GetConnectionString("DefaultConnection")
                ?? "Host=deleuze-db;Database=deleuze_drive;Username=postgres;Password=postgres";

            await using var connection = new NpgsqlConnection(baseConnectionString);

            await connection.OpenAsync();

            // 1. スキーマ作成
            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";";

                await command.ExecuteNonQueryAsync();
            }

            // 2. Migration履歴テーブル作成
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS
                    ""{schemaName}"".""SchemaMigrations"" (
                        ""MigrationName"" VARCHAR(255) PRIMARY KEY,
                        ""AppliedAt"" TIMESTAMP WITH TIME ZONE
                            DEFAULT CURRENT_TIMESTAMP
                    );";

                await command.ExecuteNonQueryAsync();
            }

            // 3. 適用済みMigration取得
            var appliedMigrations =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $@"
                    SELECT ""MigrationName""
                    FROM ""{schemaName}"".""SchemaMigrations"";";

                await using var reader =
                    await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    appliedMigrations.Add(reader.GetString(0));
                }
            }

            _logger.LogInformation(
                "Already applied migrations for {SchemaName}: {Count} found.",
                schemaName,
                appliedMigrations.Count);

            // 4. SQLファイル取得
            var migrationDir =
                Path.Combine(_env.ContentRootPath, "DbMigration");

            if (!Directory.Exists(migrationDir))
            {
                _logger.LogWarning(
                    "Migration directory not found at: {MigrationDir}",
                    migrationDir);

                return;
            }

            var sqlFiles = Directory
                .GetFiles(migrationDir, "*.sql")
                .Select(Path.GetFileName)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            _logger.LogInformation(
                "Found {Count} migration script(s) in directory.",
                sqlFiles.Count);

            // 5. 未適用Migrationを順番に実行
            foreach (var fileName in sqlFiles)
            {
                if (appliedMigrations.Contains(fileName!))
                {
                    _logger.LogDebug(
                        "Skipping already applied migration: {MigrationName}",
                        fileName);

                    continue;
                }

                _logger.LogInformation(
                    "Applying migration: {MigrationName} to schema {SchemaName}...",
                    fileName,
                    schemaName);

                var filePath = Path.Combine(migrationDir, fileName!);
                var sql = await File.ReadAllTextAsync(filePath);

                await using var transaction =
                    await connection.BeginTransactionAsync();

                try
                {
                    // このトランザクション内だけ search_path を変更
                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText =
                            $"SET search_path TO \"{schemaName}\", public;";

                        await command.ExecuteNonQueryAsync();
                    }

                    // Migration SQL実行
                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = sql;

                        await command.ExecuteNonQueryAsync();
                    }

                    // 適用履歴を記録
                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = $@"
                            INSERT INTO
                            ""{schemaName}"".""SchemaMigrations""
                            (""MigrationName"")
                            VALUES (@migrationName);";

                        command.Parameters.AddWithValue(
                            "migrationName",
                            fileName!);

                        await command.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();

                    _logger.LogInformation(
                        "Successfully applied and recorded migration: {MigrationName}",
                        fileName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error occurred while applying migration {MigrationName} to schema {SchemaName}. Rolling back.",
                        fileName,
                        schemaName);

                    await transaction.RollbackAsync();
                    throw;
                }
            }

            _logger.LogInformation(
                "Completed migration process for tenant schema: {SchemaName}",
                schemaName);
        }
    }
}