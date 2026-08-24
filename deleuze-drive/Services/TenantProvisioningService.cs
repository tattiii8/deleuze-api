using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DeleuzeDrive.Services
{
    public interface ITenantProvisioningService
    {
        Task ProvisionTenantSchemaAsync(string schemaName);
    }

    public class TenantProvisioningService : ITenantProvisioningService
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _env;
        private readonly ILogger<TenantProvisioningService> _logger;

        public TenantProvisioningService(
            IConfiguration configuration,
            IHostEnvironment env,
            ILogger<TenantProvisioningService> logger)
        {
            _configuration = configuration;
            _env = env;
            _logger = logger;
        }

        public async Task ProvisionTenantSchemaAsync(string schemaName)
        {
            if (string.IsNullOrWhiteSpace(schemaName))
            {
                throw new ArgumentException(
                    "Schema name is required.",
                    nameof(schemaName));
            }

            // Schema名として許可する文字を制限
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
                "Starting provisioning process for tenant schema: {SchemaName}",
                schemaName);

            var baseConnectionString =
                _configuration.GetConnectionString("DefaultConnection")
                ?? "Host=deleuze-db;Database=deleuze_drive;Username=postgres;Password=postgres";

            await using var connection =
                new NpgsqlConnection(baseConnectionString);

            await connection.OpenAsync();

            // 1. テナントSchemaを作成
            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";";

                await command.ExecuteNonQueryAsync();
            }

            // 2. Migration履歴テーブルを作成
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

            // 3. Migration SQLを取得
            var migrationDir =
                Path.Combine(_env.ContentRootPath, "DbMigration");

            if (!Directory.Exists(migrationDir))
            {
                throw new DirectoryNotFoundException(
                    $"Migration directory not found: {migrationDir}");
            }

            var sqlFiles = Directory
                .GetFiles(migrationDir, "*.sql")
                .Select(Path.GetFileName)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            _logger.LogInformation(
                "Found {Count} migration script(s) for provisioning tenant schema {SchemaName}.",
                sqlFiles.Count,
                schemaName);

            // 4. 新規SchemaなのでMigrationをすべて適用
            foreach (var fileName in sqlFiles)
            {
                _logger.LogInformation(
                    "Applying initial migration: {MigrationName} to schema {SchemaName}...",
                    fileName,
                    schemaName);

                var filePath =
                    Path.Combine(migrationDir, fileName!);

                var sql =
                    await File.ReadAllTextAsync(filePath);

                await using var transaction =
                    await connection.BeginTransactionAsync();

                try
                {
                    // このTransaction内だけsearch_pathを変更
                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText =
                            $"SET search_path TO \"{schemaName}\", public;";

                        await command.ExecuteNonQueryAsync();
                    }

                    // Migration SQLを実行
                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = sql;

                        await command.ExecuteNonQueryAsync();
                    }

                    // Migration履歴を記録
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
                        "Successfully applied initial migration: {MigrationName}",
                        fileName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error occurred while provisioning tenant schema {SchemaName} with migration {MigrationName}. Rolling back.",
                        schemaName,
                        fileName);

                    await transaction.RollbackAsync();
                    throw;
                }
            }

            _logger.LogInformation(
                "Completed provisioning process for tenant schema: {SchemaName}",
                schemaName);
        }
    }
}