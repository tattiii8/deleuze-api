-- =========================================================================
-- 1. 全テナント共通：認証データベース（public スキーマ）の構築
-- =========================================================================

CREATE TABLE IF NOT EXISTS "public"."Users" (
    "Id" SERIAL PRIMARY KEY,
    "LoginId" VARCHAR(100) NOT NULL UNIQUE,
    "PasswordHash" VARCHAR(255) NOT NULL,
    "TenantId" VARCHAR(50) NOT NULL
);

-- テストユーザーのデータ投入（すでに存在する場合はスキップ）
-- パスワード「password123」をBCrypt（WorkFactor=11）でハッシュ化した文字列を格納しています
INSERT INTO "public"."Users" ("LoginId", "PasswordHash", "TenantId")
VALUES 
('tatsuki', '$2a$11$R9h/lSVOEbvNySGsU.1vQuNIF7L6k6.ZHeMIFbN60D3Uf3VPh3.12', 'beta'),
('johndoe', '$2a$11$R9h/lSVOEbvNySGsU.1vQuNIF7L6k6.ZHeMIFbN60D3Uf3VPh3.12', 'alpha')
ON CONFLICT ("LoginId") DO NOTHING;


-- =========================================================================
-- 2. 業務データベース：各テナント専用の隔離スキーマの構築
-- =========================================================================

-- alpha テナント環境
CREATE SCHEMA IF NOT EXISTS "alpha";
CREATE TABLE IF NOT EXISTS "alpha"."Products" (
    "Id" SERIAL PRIMARY KEY,
    "Name" VARCHAR(256) NOT NULL
);
INSERT INTO "alpha"."Products" ("Name") VALUES ('alpha専用のスキーマ隔離データ') ON CONFLICT DO NOTHING;

-- beta テナント環境
CREATE SCHEMA IF NOT EXISTS "beta";
CREATE TABLE IF NOT EXISTS "beta"."Products" (
    "Id" SERIAL PRIMARY KEY,
    "Name" VARCHAR(256) NOT NULL
);
INSERT INTO "beta"."Products" ("Name") VALUES ('beta専用のスキーマ隔離データ') ON CONFLICT DO NOTHING;