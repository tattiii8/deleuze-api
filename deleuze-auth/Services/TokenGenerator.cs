using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace DeleuzeAuth.Services;

public class TokenGenerator
{
    private readonly RsaSecurityKey _signingKey;
    private readonly string _issuer;
    public string KeyId { get; } = "deleuze-auth-key-v1";

    public TokenGenerator()
    {
        // ★ 修正ポイント: Nomad環境等のURLズレに対応するため、環境変数から外側の正規URLを取得。
        // 未設定時はフォールバックとして従来の内部コンテナURLを使用します。
        _issuer = Environment.GetEnvironmentVariable("AUTH_EXTERNAL_URL") ?? "http://deleuze-auth:8080";

        // 本番では固定の鍵ファイルから読み込みますが、開発のため起動時オンメモリ生成
        var rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(rsa) { KeyId = KeyId };
    }

    // JWT（通行手形）の発行
    public string GenerateJwt(string loginId, string tenantId)
    {
        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, loginId),
            new Claim(JwtRegisteredClaimNames.Name, loginId),
            new Claim("tenant_id", tenantId), // APIガードレールが検査する超重要クレーム
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer, // ★ 常に外側から見える正規URL（または指定URL）が iss に刻まれる
            audience: null,  // 単一IssuerのためAudienceは検証対象外
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // APIコンテナ（deleuze-app）に公開する鍵情報の構造化
    public object GetJwks()
    {
        var parameters = _signingKey.Rsa.ExportParameters(false); // 公開鍵のみエクスポート(false)
        
        return new
        {
            keys = new[]
            {
                new {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = KeyId,
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent)
                }
            }
        };
    }
}