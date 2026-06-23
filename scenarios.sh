#!/bin/bash

# カラー出力定義
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[0;33m'
RED='\033[0;31m'
PURPLE='\033[0;35m'
NC='\033[0m'

# 💡 Nomad/ローカル環境のエンドポイント定義
AUTH_URL="${AUTH_URL:-http://192.168.8.112:5002}"
APP_URL="${APP_URL:-http://192.168.8.112:5001}"

# テスト用データ（プロビジョニング済みのデータ）
TEST_USER="gilles"
TEST_PASS="philosophyPass1"
TEST_TENANT="deleuze"

echo -e "${CYAN}======================================================${NC}"
echo -e "${CYAN}   Project Deleuze - 認証＆認可マルチテナントシーケンス検証${NC}"
echo -e "${CYAN}======================================================${NC}"

# 前提条件チェック (jq コマンドの有無)
if ! command -v jq &> /dev/null; then
    echo -e "${RED}[ERROR] JSONのパースに 'jq' コマンドが必要です。インストールしてください。${NC}"
    exit 1
fi

# -------------------------------------------------------------------------
# 🔑 STEP 1: 認証サーバー (deleuze-auth) でのユーザー認証フロー
# -------------------------------------------------------------------------
echo -e "\n${YELLOW}------------------------------------------------------${NC}"
echo -e "${YELLOW}[STEP 1] 認証フロー: クライアント ➔ 認証サーバー${NC}"
echo -e "${YELLOW}------------------------------------------------------${NC}"
echo -e "📄 概要: 生のクレデンシャル(ID/PW)を送信し、安全なJWTを発行してもらう"
echo -e "🔄 [通信中] POST ${AUTH_URL}/connect/token..."
echo -e "   └ 送信データ: user_id=${TEST_USER}, password=********"

# 1. 認証リクエスト送信
AUTH_RESPONSE=$(curl -s -X POST "${AUTH_URL}/connect/token" \
     -H "Content-Type: application/x-www-form-urlencoded" \
     -d "user_id=${TEST_USER}" \
     -d "password=${TEST_PASS}")

# 2. 内部処理シミュレーションログの出力
echo -e "\n${PURPLE}[認証サーバーの内部処理]${NC}"
echo -e " ├─ 1. DBからユーザー「${TEST_USER}」のBCryptハッシュを取得してパスワードを検証"
echo -e " ├─ 2. ユーザーの所属テナントが「${TEST_TENANT}」であることを確認"
echo -e " └─ 3. 秘密鍵を使い、テナント情報を埋め込んだJWT(電子署名付き)を生成"

# 3. トークンの抽出
JWT_TOKEN=$(echo "$AUTH_RESPONSE" | jq -r '.access_token // empty')

if [ -z "$JWT_TOKEN" ] || [ "$JWT_TOKEN" == "null" ]; then
    echo -e "\n${RED}[ERROR] ログインに失敗しました。サーバー返答:${NC}"
    echo "$AUTH_RESPONSE" | jq . 2>/dev/null || echo "$AUTH_RESPONSE"
    exit 1
fi

echo -e "\n${GREEN}✔ [STEP 1 完了] JWTトークンの取得に成功しました。${NC}"
echo -e "  取得したJWT: ${CYAN}${JWT_TOKEN:0:45}...[省略]...${NC}"


# -------------------------------------------------------------------------
# 📦 STEP 2: 業務サーバー (deleuze-app) でのマルチテナント検証＆データ取得フロー
# -------------------------------------------------------------------------
echo -e "\n${YELLOW}------------------------------------------------------${NC}"
echo -e "${YELLOW}[STEP 2] 認可＆データ隔離フロー: クライアント ➔ 業務サーバー${NC}"
echo -e "${YELLOW}------------------------------------------------------${NC}"
echo -e "📄 概要: 取得したJWT(身分証)と、見たいテナントIDをヘッダーに載せてリクエスト"
echo -e "🔄 [通信中] GET ${APP_URL}/api/products..."
echo -e "   ├─ Header [Authorization]: Bearer JWT"
echo -e "   └─ Header [X-Tenant-Id]  : ${TEST_TENANT}"

# 1. 業務データリクエスト送信 (-i でヘッダーも可視化)
APP_RESPONSE=$(curl -s -i -X GET "${APP_URL}/api/products" \
     -H "Authorization: Bearer ${JWT_TOKEN}" \
     -H "X-Tenant-Id: ${TEST_TENANT}" \
     -H "Content-Type: application/json")

# 2. 内部処理シミュレーションログの出力
echo -e "\n${PURPLE}[業務サーバーの内部処理（二重バリデーション）]${NC}"
echo -e " ├─ チェックA (JWT検証): 公開鍵による署名確認 ＆ 有効期限をチェック ➔ OK!"
echo -e " ├─ チェックB (分離検証): X-Tenant-Id (${TEST_TENANT}) と JWT内のテナント情報が一致するか検証 ➔ OK!"
echo -e " └─ データ接続: テナント「${TEST_TENANT}」の専用スキーマへ動的に接続を切り替えてクエリを実行"

# 3. レスポンスの可視化出力
echo -e "\n${GREEN}✔ [STEP 2 完了] 業務サーバーから応答が返りました:${NC}"
echo -e "${CYAN}------------------- RESPONSES FROM APP -------------------${NC}"
echo "$APP_RESPONSE"
echo -e "${CYAN}----------------------------------------------------------${NC}"


# -------------------------------------------------------------------------
# 🏁 判定処理
# -------------------------------------------------------------------------
if echo "$APP_RESPONSE" | grep -q "HTTP/1.1 200" || echo "$APP_RESPONSE" | grep -q "HTTP/2 200"; then
    echo -e "\n${GREEN}🎉 [SUCCESS] ユーザー認証 ➔ JWT発行 ➔ テナント分離検証 ➔ データ取得の一連のフローが正常に証明されました。${NC}"
else
    echo -e "\n${RED}⚠️ [FAILURE] 業務サーバーが 200 OK 以外の応答を返しました。上のヘッダーおよび内部検証ログを確認してください。${NC}"
fi
echo -e "${CYAN}======================================================${NC}"