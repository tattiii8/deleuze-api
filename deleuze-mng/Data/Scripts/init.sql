-- mng スキーマの作成
CREATE SCHEMA IF NOT EXISTS mng;

-- mng.users テーブルの作成
CREATE TABLE IF NOT EXISTS mng.users (
    subject_id VARCHAR(255) NOT NULL,
    tenant_id  VARCHAR(255) NOT NULL,
    login_id   VARCHAR(255) NOT NULL,
    user_name  VARCHAR(255) NOT NULL,
    email      VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT pk_mng_users
        PRIMARY KEY (subject_id)
);

-- テナントごとに login_id を一意にする
--
-- 例:
-- flaubert / admin  → OK
-- germinal / admin  → OK
-- flaubert / admin  → NG
CREATE UNIQUE INDEX IF NOT EXISTS idx_mng_users_tenant_login_id
    ON mng.users (tenant_id, login_id);

-- email はシステム全体で一意
CREATE UNIQUE INDEX IF NOT EXISTS idx_mng_users_email
    ON mng.users (email);

-- tenant_id による検索用
CREATE INDEX IF NOT EXISTS idx_mng_users_tenant_id
    ON mng.users (tenant_id);

-- updated_at 自動更新用のトリガー関数
CREATE OR REPLACE FUNCTION mng.update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE 'plpgsql';

-- トリガーの適用
DROP TRIGGER IF EXISTS trg_mng_users_updated_at ON mng.users;

CREATE TRIGGER trg_mng_users_updated_at
    BEFORE UPDATE ON mng.users
    FOR EACH ROW
    EXECUTE FUNCTION mng.update_updated_at_column();

    -- mng.tenants
CREATE TABLE IF NOT EXISTS mng.tenants (
    tenant_id    VARCHAR(255) NOT NULL,
    tenant_name  VARCHAR(255) NOT NULL,
    display_name VARCHAR(255),
    created_at   TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT pk_mng_tenants
        PRIMARY KEY (tenant_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mng_tenants_tenant_name
    ON mng.tenants (tenant_name);

CREATE OR REPLACE FUNCTION mng.update_tenants_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE 'plpgsql';

DROP TRIGGER IF EXISTS trg_mng_tenants_updated_at
    ON mng.tenants;

CREATE TRIGGER trg_mng_tenants_updated_at
    BEFORE UPDATE ON mng.tenants
    FOR EACH ROW
    EXECUTE FUNCTION mng.update_tenants_updated_at_column();