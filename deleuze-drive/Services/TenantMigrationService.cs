using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
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

        public TenantMigrationService(DriveDbContext dbContext, IHostEnvironment env)
        {
            _dbContext = dbContext;
            _env = env;
        }

        public async Task MigrateTenantSchemaAsync(string schemaName)
        {
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

            // 4. DbMigration/ フォルダからすべての .sql ファイルを取得して名前順にソート
            var migrationDir = Path.Combine(_env.ContentRootPath, "DbMigration");
            if (!Directory.Exists(migrationDir))
            {
                return;
            }

            var sqlFiles = Directory.GetFiles(migrationDir, "*.sql")
                .Select(Path.GetFileName)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            // 5. 未適用のSQLファイルを上から順に実行
            foreach (var fileName in sqlFiles)
            {
                if (appliedMigrations.Contains(fileName))
                {
                    continue;
                }

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
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            #pragma warning restore EF1002
        }
    }
}