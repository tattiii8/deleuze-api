-- auth スキーマの作成（存在しない場合）
CREATE SCHEMA IF NOT EXISTS auth;

-- auth.users テーブルの作成
CREATE TABLE IF NOT EXISTS auth.users (
    subject_id VARCHAR(255) NOT NULL,
    login_id VARCHAR(255) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    CONSTRAINT pk_auth_users PRIMARY KEY (subject_id)
);

-- login_id のユニークインデックス（必要に応じて）
CREATE UNIQUE INDEX IF NOT EXISTS idx_auth_users_login_id ON auth.users (login_id);