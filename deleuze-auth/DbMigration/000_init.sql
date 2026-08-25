-- ==========================================================
-- 000_init.sql (AuthService コンテナ用)
-- AuthService Global Schema & Tables Initialization
-- ==========================================================

-- ----------------------------------------------------------
-- Global Auth Schema (auth_global) & Tables
-- ----------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS auth_global;

-- グローバルユーザー（全テナント共通のログイン・認証情報）
CREATE TABLE IF NOT EXISTS auth_global.users (
    login_id UUID NOT NULL,
    email VARCHAR(255) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_global_users PRIMARY KEY (login_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_global_users_email 
    ON auth_global.users (email);

-- グローバルAPI Key管理テーブル
CREATE TABLE IF NOT EXISTS auth_global.api_keys (
    id INT GENERATED ALWAYS AS IDENTITY,
    key_hash VARCHAR(255) NOT NULL,
    login_id UUID NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMPTZ NULL,
    is_revoked BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT pk_global_api_keys PRIMARY KEY (id),
    CONSTRAINT fk_global_api_keys_users FOREIGN KEY (login_id) 
        REFERENCES auth_global.users (login_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_global_api_keys_hash 
    ON auth_global.api_keys (key_hash);

CREATE INDEX IF NOT EXISTS ix_global_api_keys_login_id 
    ON auth_global.api_keys (login_id);