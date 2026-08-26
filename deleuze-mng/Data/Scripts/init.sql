-- mng スキーマの作成
CREATE SCHEMA IF NOT EXISTS mng;

-- mng.users テーブルの作成
CREATE TABLE IF NOT EXISTS mng.users (
    subject_id VARCHAR(255) NOT NULL,
    login_id   VARCHAR(255) NOT NULL,
    user_name  VARCHAR(255) NOT NULL,
    email      VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT pk_mng_users PRIMARY KEY (subject_id)
);

-- ユニーク制約・検索用インデックス
CREATE UNIQUE INDEX IF NOT EXISTS idx_mng_users_login_id ON mng.users (login_id);
CREATE INDEX IF NOT EXISTS idx_mng_users_email ON mng.users (email);

-- updated_at 自動更新用のトリガー関数（存在しない場合のみ作成）
CREATE OR REPLACE FUNCTION mng.update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

-- トリガーの適用（既に存在する場合は一度削除して再作成）
DROP TRIGGER IF EXISTS trg_mng_users_updated_at ON mng.users;
CREATE TRIGGER trg_mng_users_updated_at
    BEFORE UPDATE ON mng.users
    FOR EACH ROW
    EXECUTE FUNCTION mng.update_updated_at_column();