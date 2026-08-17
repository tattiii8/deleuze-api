using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace DeleuzeMng.Services
{
    public class TenantManagementService
    {
        private readonly string _appConnString;
        private readonly string _authConnString;

        // PostgreSQL の識別子制限(63バイト)を踏まえ、テナントIDは
        // 「小文字英字始まり + 英数字/アンダースコアのみ + 3〜63文字」に制限する。
        // ここで弾くことで、スキーマ名へのSQLインジェクションを防止する。
        private static readonly Regex ValidTenantIdPattern =
            new(@"^[a-z][a-z0-9_]{2,62}$", RegexOptions.Compiled);

        public TenantManagementService(IConfiguration configuration)
        {
            _appConnString = configuration.GetConnectionString("AppConnection")
                ?? throw new InvalidOperationException("接続文字列 'AppConnection' が設定されていません。");

            _authConnString = configuration.GetConnectionString("AuthConnection")
                ?? throw new InvalidOperationException("接続文字列 'AuthConnection' が設定されていません。");
        }

        /// <summary>
        /// テナント用のスキーマをアプリケーションDB側に作成する(冪等)。
        /// 既に存在する場合は何もしない。
        /// </summary>
        public async Task CreateTenantAsync(string tenantId)
        {
            EnsureValidTenantId(tenantId);

            await using var appConn = new NpgsqlConnection(_appConnString);
            await appConn.OpenAsync();

            var alreadyExists = await appConn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = @tenantId);",
                new { tenantId });

            // tenantId は EnsureValidTenantId で英数字・アンダースコアのみに限定済みのため、
            // ここでの動的SQL組み立ては安全(パラメータバインドが使えないDDLのための例外的対応)。
            var createSchemaCmd = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS \"{tenantId}\";", appConn);
            await createSchemaCmd.ExecuteNonQueryAsync();

            if (!alreadyExists)
            {
                await InitializeTenantTablesAsync(appConn, tenantId);
            }
        }

        /// <summary>
        /// 新規テナントのスキーマに初期テーブル群を作成する。
        /// 実際の運用では Flyway 等のマイグレーションツールに置き換えることを推奨。
        /// </summary>
        private static async Task InitializeTenantTablesAsync(NpgsqlConnection appConn, string tenantId)
        {  
            // 各テーブル作成SQLをセミコロンで区切って定義する。
            var sql = $@"
                -- 1. カテゴリマスタ
                CREATE TABLE ""{tenantId}"".""Categories"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""Name"" VARCHAR(100) NOT NULL,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                -- 2. 商品マスタ
                CREATE TABLE ""{tenantId}"".""Products"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""CategoryId"" INTEGER REFERENCES ""{tenantId}"".""Categories""(""Id""),
                    ""Name"" VARCHAR(255) NOT NULL,
                    ""Price"" DECIMAL(12, 2) DEFAULT 0,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                -- 3. 顧客マスタ
                CREATE TABLE ""{tenantId}"".""Customers"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""Name"" VARCHAR(100) NOT NULL,
                    ""Email"" VARCHAR(255),
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                -- 4. 注文トランザクション
                CREATE TABLE ""{tenantId}"".""Orders"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""CustomerId"" INTEGER NOT NULL REFERENCES ""{tenantId}"".""Customers""(""Id""),
                    ""OrderDate"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    ""TotalAmount"" DECIMAL(12, 2) DEFAULT 0
                );

                -- 5. 注文明細
                CREATE TABLE ""{tenantId}"".""OrderItems"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""OrderId"" INTEGER NOT NULL REFERENCES ""{tenantId}"".""Orders""(""Id"") ON DELETE CASCADE,
                    ""ProductId"" INTEGER NOT NULL REFERENCES ""{tenantId}"".""Products""(""Id""),
                    ""Quantity"" INTEGER NOT NULL,
                    ""UnitPrice"" DECIMAL(12, 2) NOT NULL
                );
            ";

            // トランザクションを利用して安全に一括実行する
            await using var transaction = await appConn.BeginTransactionAsync();
            try
            {
                await using var cmd = new NpgsqlCommand(sql, appConn, transaction);
                await cmd.ExecuteNonQueryAsync();
                
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// 認証DBにユーザーを登録する。
        /// </summary>
        public async Task RegisterUserAsync(string loginId, string password, string tenantId)
        {
            EnsureValidTenantId(tenantId);

            if (string.IsNullOrWhiteSpace(loginId))
                throw new ArgumentException("LoginId は必須です。", nameof(loginId));

            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                throw new ArgumentException("Password は8文字以上で指定してください。", nameof(password));

            await using var authConn = new NpgsqlConnection(_authConnString);
            await authConn.OpenAsync();

            // 既に同じ LoginId が存在しないか事前チェック(UNIQUE制約違反の生の例外を避ける)
            var loginIdExists = await authConn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM public.\"Users\" WHERE \"LoginId\" = @loginId);",
                new { loginId });

            if (loginIdExists)
                throw new InvalidOperationException($"LoginId '{loginId}' は既に使用されています。");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

            const string insertSql = @"
                INSERT INTO public.""Users"" (""LoginId"", ""PasswordHash"", ""TenantId"")
                VALUES (@loginId, @passwordHash, @tenantId);";

            await authConn.ExecuteAsync(insertSql, new { loginId, passwordHash, tenantId });
        }

        private static void EnsureValidTenantId(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !ValidTenantIdPattern.IsMatch(tenantId))
            {
                throw new ArgumentException(
                    $"不正なテナントID形式です。小文字英数字とアンダースコアのみ、3〜63文字で指定してください: '{tenantId}'",
                    nameof(tenantId));
            }
        }
    }
} 