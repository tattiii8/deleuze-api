using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;

namespace Deleuze.Shared.Infrastructure;

public class TenantSchemaManager :
    ITenantSchemaProvisioner,
    ITenantSchemaDeprovisioner,
    ITenantSchemaMigrator
{
    private readonly string _connectionString;
    private readonly string _serviceName;

    public TenantSchemaManager(
        string connectionString,
        string serviceName)
    {
        _connectionString =
            connectionString
            ?? throw new ArgumentNullException(nameof(connectionString));

        _serviceName =
            serviceName
            ?? throw new ArgumentNullException(nameof(serviceName));
    }

    /// <summary>
    /// テナントSchemaを作成し、全Migrationを適用する。
    ///
    /// 既にSchemaが存在する場合は、
    /// 未適用Migrationのみを適用する。
    /// </summary>
    public async Task ProvisionAsync(
        string tenantId,
        string migrationDirectory)
    {
        var schemaName =
            TenantSchemaNaming.GetSchemaName(
                _serviceName,
                tenantId);

        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync();

        // Schemaを作成
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";";

            await command.ExecuteNonQueryAsync();
        }

        // Migration履歴テーブルを作成
        await EnsureMigrationTableAsync(
            connection,
            schemaName);

        // 未適用Migrationをすべて適用
        await ApplyPendingMigrationsAsync(
            connection,
            schemaName,
            migrationDirectory);
    }

    /// <summary>
    /// テナントSchemaを削除する。
    /// </summary>
    public async Task DeprovisionAsync(
        string tenantId)
    {
        var schemaName =
            TenantSchemaNaming.GetSchemaName(
                _serviceName,
                tenantId);

        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;";

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 既存テナントSchemaに未適用Migrationを適用する。
    ///
    /// SchemaやSchemaMigrationsは作成しない。
    /// Provisioning済みであることを前提とする。
    /// </summary>
    public async Task MigrateAsync(
        string tenantId,
        string migrationDirectory)
    {
        var schemaName =
            TenantSchemaNaming.GetSchemaName(
                _serviceName,
                tenantId);

        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync();

        // MigrationではSchemaを作成しない
        if (!await SchemaExistsAsync(
                connection,
                schemaName))
        {
            throw new InvalidOperationException(
                $"Tenant schema does not exist: {schemaName}. " +
                "Provisioning must be completed before migration.");
        }

        // Migrationでは履歴テーブルも作成しない
        if (!await MigrationTableExistsAsync(
                connection,
                schemaName))
        {
            throw new InvalidOperationException(
                $"SchemaMigrations table does not exist in tenant schema: {schemaName}. " +
                "Provisioning must be completed before migration.");
        }

        // 未適用Migrationを適用
        await ApplyPendingMigrationsAsync(
            connection,
            schemaName,
            migrationDirectory);
    }

    private static async Task EnsureMigrationTableAsync(
        NpgsqlConnection connection,
        string schemaName)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText = $@"
            CREATE TABLE IF NOT EXISTS
            ""{schemaName}"".""SchemaMigrations"" (
                ""MigrationName"" VARCHAR(255) PRIMARY KEY,
                ""AppliedAt"" TIMESTAMP WITH TIME ZONE
                    DEFAULT CURRENT_TIMESTAMP
            );";

        await command.ExecuteNonQueryAsync();
    }

    private static async Task ApplyPendingMigrationsAsync(
        NpgsqlConnection connection,
        string schemaName,
        string migrationDirectory)
    {
        if (!Directory.Exists(migrationDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Migration directory not found: {migrationDirectory}");
        }

        var appliedMigrations =
            await GetAppliedMigrationsAsync(
                connection,
                schemaName);

        var sqlFiles = Directory
            .GetFiles(
                migrationDirectory,
                "*.sql")
            .Select(Path.GetFileName)
            .Where(f =>
                !string.IsNullOrWhiteSpace(f))
            .OrderBy(
                f => f,
                StringComparer.Ordinal)
            .ToList();

        foreach (var fileName in sqlFiles)
        {
            if (appliedMigrations.Contains(fileName!))
            {
                continue;
            }

            var filePath =
                Path.Combine(
                    migrationDirectory,
                    fileName!);

            var sql =
                await File.ReadAllTextAsync(
                    filePath);

            await using var transaction =
                await connection.BeginTransactionAsync();

            try
            {
                // tenant schemaをsearch_pathの先頭にする
                await using (var command =
                    connection.CreateCommand())
                {
                    command.Transaction =
                        transaction;

                    command.CommandText =
                        $"SET search_path TO \"{schemaName}\", public;";

                    await command.ExecuteNonQueryAsync();
                }

                // Migration SQL実行
                await using (var command =
                    connection.CreateCommand())
                {
                    command.Transaction =
                        transaction;

                    command.CommandText =
                        sql;

                    await command.ExecuteNonQueryAsync();
                }

                // Migration履歴を記録
                await using (var command =
                    connection.CreateCommand())
                {
                    command.Transaction =
                        transaction;

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

                appliedMigrations.Add(
                    fileName!);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }

    private static async Task<HashSet<string>>
        GetAppliedMigrationsAsync(
            NpgsqlConnection connection,
            string schemaName)
    {
        var result =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        await using var command =
            connection.CreateCommand();

        command.CommandText = $@"
            SELECT ""MigrationName""
            FROM ""{schemaName}"".""SchemaMigrations"";";

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(
                reader.GetString(0));
        }

        return result;
    }

    private static async Task<bool> SchemaExistsAsync(
        NpgsqlConnection connection,
        string schemaName)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText = @"
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.schemata
                WHERE schema_name = @schemaName
            );";

        command.Parameters.AddWithValue(
            "schemaName",
            schemaName);

        return (bool)(
            await command.ExecuteScalarAsync()
            ?? false);
    }

    private static async Task<bool> MigrationTableExistsAsync(
        NpgsqlConnection connection,
        string schemaName)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText = @"
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schemaName
                  AND table_name = 'SchemaMigrations'
            );";

        command.Parameters.AddWithValue(
            "schemaName",
            schemaName);

        return (bool)(
            await command.ExecuteScalarAsync()
            ?? false);
    }
}