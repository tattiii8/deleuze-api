using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DeleuzeDrive.Data;

namespace DeleuzeDrive.Services
{
    public interface ITenantMigrationService
    {
        Task MigrateTenantSchemaAsync(string schemaName);
    }

    public class TenantMigrationService : ITenantMigrationService
    {
        private readonly DriveDbContext _dbContext;
        private readonly IHostEnvironment _env;
        private readonly ILogger<TenantMigrationService> _logger;

        public TenantMigrationService(
            DriveDbContext dbContext, 
            IHostEnvironment env, 
            ILogger<TenantMigrationService> logger)
        {
            _dbContext = dbContext;
            _env = env;
            _logger = logger;
        }

        public async Task MigrateTenantSchemaAsync(string schemaName)
        {
            _logger.LogInformation("Starting migration process for tenant schema: {SchemaName}", schemaName);

            #pragma warning disable EF1002
            // 1. スキーマ自体がなければ作成
            await _dbContext.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";");

            // 2. 適用済み履歴を管理するテーブルがなければ作成
            var createHistoryTableSql = $@"
                CREATE TABLE IF NOT EXISTS ""{schemaName}"".""SchemaMigrations"" (
                    ""MigrationName"" VARCHAR(255) PRIMARY KEY,
                    ""AppliedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );";
            await _dbContext.Database.ExecuteSqlRawAsync(createHistoryTableSql);

            // 3. すでに実行済みのマイグレーション名一覧を取得
            var appliedMigrationsRaw = await _dbContext.Database
                .SqlQueryRaw<string>($"SELECT \"MigrationName\" FROM \"{schemaName}\".\"SchemaMigrations\"")
                .ToListAsync();
            var appliedMigrations = appliedMigrationsRaw.ToHashSet(StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation("Already applied migrations for {SchemaName}: {Count} found.", schemaName, appliedMigrations.Count);

            // 4. DbMigration/ フォルダからすべての .sql ファイルを取得して名前順にソート
            var migrationDir = Path.Combine(_env.ContentRootPath, "DbMigration");
            if (!Directory.Exists(migrationDir))
            {
                _logger.LogWarning("Migration directory not found at: {MigrationDir}", migrationDir);
                return;
            }

            var sqlFiles = Directory.GetFiles(migrationDir, "*.sql")
                .Select(Path.GetFileName)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            _logger.LogInformation("Found {Count} migration script(s) in directory.", sqlFiles.Count);

            // 5. 未適用のSQLファイルを上から順に実行
            foreach (var fileName in sqlFiles)
            {
                if (appliedMigrations.Contains(fileName))
                {
                    _logger.LogDebug("Skipping already applied migration: {MigrationName}", fileName);
                    continue;
                }

                _logger.LogInformation("Applying migration: {MigrationName} to schema {SchemaName}...", fileName, schemaName);

                var filePath = Path.Combine(migrationDir, fileName);
                var sql = await File.ReadAllTextAsync(filePath);

                using var transaction = await _dbContext.Database.BeginTransactionAsync();
                try
                {
                    // 検索パスを指定してスキーマコンテキストでSQLを実行
                    await _dbContext.Database.ExecuteSqlRawAsync($"SET search_path TO \"{schemaName}\", public;");
                    await _dbContext.Database.ExecuteSqlRawAsync(sql);

                    // 適用成功したため履歴に記録
                    await _dbContext.Database.ExecuteSqlRawAsync(
                        $"INSERT INTO \"{schemaName}\".\"SchemaMigrations\" (\"MigrationName\") VALUES (@p0);", 
                        fileName);

                    await transaction.CommitAsync();
                    _logger.LogInformation("Successfully applied and recorded migration: {MigrationName}", fileName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while applying migration {MigrationName} to schema {SchemaName}. Rolling back.", fileName, schemaName);
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            _logger.LogInformation("Completed migration process for tenant schema: {SchemaName}", schemaName);
            #pragma warning restore EF1002
        }
    }
}