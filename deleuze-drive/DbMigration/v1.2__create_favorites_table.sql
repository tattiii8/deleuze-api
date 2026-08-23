-- v1.2__create_favorites_table.sql
CREATE TABLE IF NOT EXISTS "Favorites" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "FileId" UUID REFERENCES "Files"("Id") ON DELETE CASCADE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);