-- v1.3__create_tags_table.sql
-- ファイルの分類・検索性を高めるためのタグテーブルと紐付けテーブル

CREATE TABLE IF NOT EXISTS "Tags" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name" VARCHAR(100) NOT NULL UNIQUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Files と Tags の多対多リレーション（中間テーブル）
CREATE TABLE IF NOT EXISTS "FileTags" (
    "FileId" UUID REFERENCES "Files"("Id") ON DELETE CASCADE,
    "TagId" UUID REFERENCES "Tags"("Id") ON DELETE CASCADE,
    PRIMARY KEY ("FileId", "TagId")
);

-- 検索パフォーマンス向上のためのインデックス
CREATE INDEX IF NOT EXISTS "IX_FileTags_TagId" ON "FileTags"("TagId");