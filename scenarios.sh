#!/bin/bash

# カラー出力定義
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[0;33m'
RED='\033[0;31m'
NC='\033[0m'

# 💡 Nomad/ローカル環境のエンドポイント定義（環境に合わせて調整してください）
AUTH_URL="${AUTH_URL:-http://192.168.8.112:5002}"
APP_URL="${APP_URL:-http://192.168.8.112:5001}"

# テスト用データ（登録済みのプロビジョニングデータ）
TEST_USER="gilles"
TEST_PASS="philosophyPass1"
TEST_TENANT="deleuze"

echo -e "${CYAN}======================================================${NC}"
echo -e "${CYAN}   Project Deleuze - 認証 ＆ データ閲覧 エンドツーエンド検証${NC}"
echo -e "${CYAN}======================================================${NC}"

# 前提条件チェック (jq コマンドの有無)
if ! command -v jq &> /dev/null; then
    echo -e "${RED}[ERROR] JSONのパースに 'jq' コマンドが必要です。インストールしてください。${NC}"
    exit 1
fi

# -------------------------------------------------------------------------
# 🔑 STEP 1: 認証サーバー (deleuze-auth) へログインリクエスト
# -------------------------------------------------------------------------
echo -e "\n${YELLOW}[STEP 1] 認証サーバー (${AUTH_URL}) でログインを実行します...${NC}"
echo -e "🔄 ユーザー: ${TEST_USER} | OIDC トークンエンドポイントへリクエスト中..."

AUTH_RESPONSE=$(curl -s -X POST "${AUTH_URL}/connect/token" \
     -H "Content-Type: application/x-www-form-urlencoded" \
     -d "user_id=${TEST_USER}" \
     -d "password=${TEST_PASS}")

# `access_token` というキー名で返ってくる JWT を抽出
JWT_TOKEN=$(echo "$AUTH_RESPONSE" | jq -r '.access_token // empty')

if [ -z "$JWT_TOKEN" ] || [ "$JWT_TOKEN" == "null" ]; then
    echo -e "${RED}[ERROR] ログインに失敗しました。サーバー返答:${NC}"
    echo "$AUTH_RESPONSE" | jq . 2>/dev/null || echo "$AUTH_RESPONSE"
    exit 1
fi

echo -e "${GREEN}  └ ログイン成功！OIDC準拠のJWTトークンを取得しました。${NC}"
echo -e "    Token (冒頭のみ): ${CYAN}${JWT_TOKEN:0:30}...${NC}"

# -------------------------------------------------------------------------
# 📦 STEP 2: 取得したJWTを使ってアプリケーションサーバー (deleuze-app) からデータ取得
# -------------------------------------------------------------------------
echo -e "\n${YELLOW}[STEP 2] 業務サーバー (${APP_URL}) からアプリケーションデータを取得します...${NC}"
echo -e "🔄 テナント「${TEST_TENANT}」の隔離データ（Products）をリクエスト中..."
echo -e "💡 原因特定のため、HTTPヘッダー情報（-i）を含めて出力します。"

# 💡 変更ポイント: `-s` に `-i` を追加して、HTTPステータスコードやエラーヘッダーを可視化
APP_RESPONSE=$(curl -s -i -X GET "${APP_URL}/api/products" \
     -H "Authorization: Bearer ${JWT_TOKEN}" \
     -H "X-Tenant-Id: ${TEST_TENANT}" \
     -H "Content-Type: application/json")

echo -e "${GREEN}  └ 業務サーバー返答 (ステータス・ヘッダー・データ):${NC}"
echo -e "${CYAN}------------------------------------------------------${NC}"
# レスポンス全体（ヘッダー＋ボディ）をそのまま出力
echo "$APP_RESPONSE"
echo -e "${CYAN}------------------------------------------------------${NC}"

# 簡易的なステータスチェック判定
if echo "$APP_RESPONSE" | grep -q "HTTP/1.1 200" || echo "$APP_RESPONSE" | grep -q "HTTP/2 200"; then
    echo -e "\n${GREEN}🎉 一連の認証・データ閲覧シーケンスの検証が正常に完了しました。${NC}"
else
    echo -e "\n${RED}⚠️ 業務サーバーが 200 OK 以外の応答を返しました。上記のヘッダー出力を確認してください。${NC}"
fi