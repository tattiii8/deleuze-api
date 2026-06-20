#!/bin/bash

BLUE='\033[0;34m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[0;33m'
MAGENTA='\033[0;35m'
NC='\033[0m'

echo -e "${BLUE}======================================================${NC}"
echo -e "${BLUE}   マルチテナント・本番ログインフロー完全観察スクリプト${NC}"
echo -e "${BLUE}======================================================${NC}"

# ─── STEP 1 ───
echo -e "\n${CYAN}[STEP 1: クライアント ➡️ 認証サーバー (/connect/token)]${NC}"
echo -e "${YELLOW}要求: tatsuki ユーザーとしてログインを試みます。${NC}"

# 生パスワード文字列（エスケープ付き）で認証DBへアタック
HASH_PASS="\$2a\$11\$R9h/lSVOEbvNySGsU.1vQuNIF7L6k6.ZHeMIFbN60D3Uf3VPh3.12"
RESPONSE=$(curl -s -X POST http://localhost:5002/connect/token \
     -H "Content-Type: application/x-www-form-urlencoded" \
     -d "user_id=tatsuki" \
     -d "password=$HASH_PASS")

echo -e "${GREEN}▼ 認証サーバーからのレスポンス (JWTトークンを発行):${NC}"
echo "$RESPONSE" | json_pp 2>/dev/null || echo "$RESPONSE"

TOKEN=$(echo $RESPONSE | sed -E 's/.*"access_token":"([^"]+)".*/\1/')

# ─── STEP 2 ───
echo -e "\n${CYAN}[STEP 2: クライアント ➡️ APIサーバー (/api/products)]${NC}"
echo -e "${YELLOW}要求: 取得したJWTをヘッダーに乗せ、betaテナントのデータを要求します。${NC}"
echo -e "${MAGENTA}--- クライアントが送信したHTTPヘッダー ---${NC}"
echo "GET /api/products HTTP/1.1"
echo "Authorization: Bearer ${TOKEN:0:30}..."
echo "X-Tenant-ID: beta"
echo -e "${MAGENTA}---------------------------------------${NC}"

API_RESPONSE=$(curl -s -H "Authorization: Bearer $TOKEN" \
     -H "X-Tenant-ID: beta" \
     http://localhost:5001/api/products)

echo -e "${GREEN}▼ APIサーバーからの最終レスポンス:${NC}"
echo "$API_RESPONSE"

echo -e "\n${BLUE}======================================================${NC}"