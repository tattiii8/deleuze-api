-- auth スキーマの作成
CREATE SCHEMA IF NOT EXISTS auth;

-- auth.users テーブルの作成
CREATE TABLE IF NOT EXISTS auth.users (
    subject_id     VARCHAR(255) NOT NULL,
    tenant_id      VARCHAR(255) NOT NULL,
    login_id       VARCHAR(255) NOT NULL,
    password_hash  VARCHAR(255) NOT NULL,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT pk_auth_users
        PRIMARY KEY (subject_id)
);

-- テナントごとに login_id を一意にする
CREATE UNIQUE INDEX IF NOT EXISTS idx_auth_users_tenant_login_id
    ON auth.users (tenant_id, login_id);

-- tenant_id による検索用
CREATE INDEX IF NOT EXISTS idx_auth_users_tenant_id
    ON auth.users (tenant_id);

-- updated_at 自動更新用
CREATE OR REPLACE FUNCTION auth.update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE 'plpgsql';

DROP TRIGGER IF EXISTS trg_auth_users_updated_at
    ON auth.users;

CREATE TRIGGER trg_auth_users_updated_at
    BEFORE UPDATE ON auth.users
    FOR EACH ROW
    EXECUTE FUNCTION auth.update_updated_at_column();

    -- auth.tenants
CREATE TABLE IF NOT EXISTS auth.tenants (
    tenant_id VARCHAR(255) NOT NULL,

    CONSTRAINT pk_auth_tenants
        PRIMARY KEY (tenant_id)
);


CREATE TABLE IF NOT EXISTS auth.apikeys (
    id UUID NOT NULL,
    subject_id VARCHAR(255) NOT NULL,
    tenant_id VARCHAR(255) NOT NULL,
    key_hash VARCHAR(255) NOT NULL,
    name VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMPTZ NULL,
    revoked_at TIMESTAMPTZ NULL,

    CONSTRAINT pk_auth_apikeys
        PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_auth_apikeys_key_hash
    ON auth.apikeys (key_hash);

CREATE INDEX IF NOT EXISTS idx_auth_apikeys_subject_id
    ON auth.apikeys (subject_id);

CREATE INDEX IF NOT EXISTS idx_auth_apikeys_tenant_id
    ON auth.apikeys (tenant_id);