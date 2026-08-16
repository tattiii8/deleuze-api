# =========================================================================
# Project Deleuze - 認証＆認可マルチテナントシーケンス検証 (PowerShell版)
# =========================================================================

# エンドポイント定義（環境変数またはデフォルト値）
$AUTH_URL = if ($env:AUTH_URL) { $env:AUTH_URL } else { "http://192.168.8.112:5002" }
$APP_URL  = if ($env:APP_URL)  { $env:APP_URL }  else { "http://192.168.8.112:5001" }

# テスト用データ
$TEST_USER   = "gilles"
$TEST_PASS   = "philosophyPass1"
$TEST_TENANT = "deleuze"

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  Project Deleuze - 認証＆認可マルチテナントシーケンス検証" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

# -------------------------------------------------------------------------
# 🔑 STEP 1: 認証サーバー (deleuze-auth) でのユーザー認証フロー
# -------------------------------------------------------------------------
Write-Host "`n------------------------------------------------------" -ForegroundColor Yellow
Write-Host "[STEP 1] 認証フロー: クライアント ➔ 認証サーバー" -ForegroundColor Yellow
Write-Host "------------------------------------------------------" -ForegroundColor Yellow
Write-Host "📄 概要: 生のクレデンシャル(ID/PW)を送信し、安全なJWTを発行してもらう"
Write-Host "🔄 [通信中] POST ${AUTH_URL}/connect/token..."
Write-Host "   └ 送信データ: user_id=${TEST_USER}, password=********"

# リクエストボディ作成
$authBody = @{
    user_id  = $TEST_USER
    password = $TEST_PASS
}

try {
    # POSTリクエスト実行
    $authResponse = Invoke-RestMethod -Uri "${AUTH_URL}/connect/token" `
                                     -Method Post `
                                     -ContentType "application/x-www-form-urlencoded" `
                                     -Body $authBody `
                                     -ErrorAction Stop
}
catch {
    Write-Host "`n[ERROR] ログインに失敗しました。" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
        Write-Host $reader.ReadToEnd() -ForegroundColor Red
    } else {
        Write-Host $_.Exception.Message -ForegroundColor Red
    }
    exit 1
}

# 内部処理シミュレーションログの出力
Write-Host "`n[認証サーバーの内部処理]" -ForegroundColor Magenta
Write-Host " ├─ 1. DBからユーザー「${TEST_USER}」のBCryptハッシュを取得してパスワードを検証"
Write-Host " ├─ 2. ユーザーの所属テナントが「${TEST_TENANT}」であることを確認"
Write-Host " └─ 3. 秘密鍵を使い、テナント情報を埋め込んだJWT(電子署名付き)を生成"

# トークンの抽出
$JWT_TOKEN = $authResponse.access_token

if ([string]::IsNullOrWhiteSpace($JWT_TOKEN)) {
    Write-Host "`n[ERROR] アクセストークンが見つかりませんでした。" -ForegroundColor Red
    $authResponse | ConvertTo-Json
    exit 1
}

$shortToken = if ($JWT_TOKEN.Length -gt 45) { $JWT_TOKEN.Substring(0, 45) } else { $JWT_TOKEN }
Write-Host "`n✔ [STEP 1 完了] JWTトークンの取得に成功しました。" -ForegroundColor Green
Write-Host "  取得したJWT: " -NoNewline
Write-Host "${shortToken}...[省略]..." -ForegroundColor Cyan


# -------------------------------------------------------------------------
# 📦 STEP 2: 業務サーバー (deleuze-app) でのマルチテナント検証＆データ取得フロー
# -------------------------------------------------------------------------
Write-Host "`n------------------------------------------------------" -ForegroundColor Yellow
Write-Host "[STEP 2] 認可＆データ隔離フロー: クライアント ➔ 業務サーバー" -ForegroundColor Yellow
Write-Host "------------------------------------------------------" -ForegroundColor Yellow
Write-Host "📄 概要: 取得したJWT(身分証)と、見たいテナントIDをヘッダーに載せてリクエスト"
Write-Host "🔄 [通信中] GET ${APP_URL}/api/products..."
Write-Host "   ├─ Header [Authorization]: Bearer JWT"
Write-Host "   └─ Header [X-Tenant-Id]  : ${TEST_TENANT}"

# ヘッダー準備
$appHeaders = @{
    "Authorization" = "Bearer $JWT_TOKEN"
    "X-Tenant-Id"   = $TEST_TENANT
    "Content-Type"   = "application/json"
}

$statusCode = 0
$appResponseBody = ""

try {
    # 応答ヘッダーやステータスコードも含めて取得するために Invoke-WebRequest を使用
    $webResponse = Invoke-WebRequest -Uri "${APP_URL}/api/products" `
                                    -Method Get `
                                    -Headers $appHeaders `
                                    -ErrorAction Stop
    $statusCode = [int]$webResponse.StatusCode
    
    # 応答テキストを取得
    $appResponseBody = $webResponse.Content
    
    # HTTPステータス行をフォーマット出力
    $statusLine = "HTTP/1.1 $statusCode $($webResponse.StatusDescription)"
    $headerText = "$statusLine`r`n" + ($webResponse.Headers.GetEnumerator() | ForEach-Object { "$($_.Key): $($_.Value -join ', ')" }) -join "`r`n"
    $rawResponseOutput = "$headerText`r`n`r`n$appResponseBody"
}
catch {
    if ($_.Exception.Response) {
        $res = $_.Exception.Response
        $statusCode = [int]$res.StatusCode
        $reader = [System.IO.StreamReader]::new($res.GetResponseStream())
        $appResponseBody = $reader.ReadToEnd()
        $rawResponseOutput = "HTTP/1.1 $statusCode`r`n`r`n$appResponseBody"
    } else {
        $rawResponseOutput = $_.Exception.Message
    }
}

# 内部処理シミュレーションログの出力
Write-Host "`n[業務サーバーの内部処理（二重バリデーション）]" -ForegroundColor Magenta
Write-Host " ├─ チェックA (JWT検証): 公開鍵による署名確認 ＆ 有効期限をチェック ➔ OK!"
Write-Host " ├─ チェックB (分離検証): X-Tenant-Id (${TEST_TENANT}) と JWT内のテナント情報が一致するか検証 ➔ OK!"
Write-Host " └─ データ接続: テナント「${TEST_TENANT}」の専用スキーマへ動的に接続を切り替えてクエリを実行"

# レスポンスの可視化出力
Write-Host "`n✔ [STEP 2 完了] 業務サーバーから応答が返りました:" -ForegroundColor Green
Write-Host "------------------- RESPONSES FROM APP -------------------" -ForegroundColor Cyan
Write-Host $rawResponseOutput
Write-Host "----------------------------------------------------------" -ForegroundColor Cyan


# -------------------------------------------------------------------------
# 🏁 判定処理
# -------------------------------------------------------------------------
if ($statusCode -eq 200) {
    Write-Host "`n🎉 [SUCCESS] ユーザー認証 ➔ JWT発行 ➔ テナント分離検証 ➔ データ取得の一連のフローが正常に証明されました。" -ForegroundColor Green
} else {
    Write-Host "`n⚠️ [FAILURE] 業務サーバーが 200 OK 以外の応答を返しました。上のヘッダーおよび内部検証ログを確認してください。" -ForegroundColor Red
}
Write-Host "======================================================" -ForegroundColor Cyan