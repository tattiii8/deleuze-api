using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace DeleuzeAuth.Services;

public class TokenGenerator
{
    private readonly IConfiguration _configuration;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _key;

    public TokenGenerator(IConfiguration configuration)
    {
        _configuration = configuration;
        
        // シングルトンとしてインスタンス化される際、RSA鍵ペアを生成
        _rsa = RSA.Create(2048);
        _key = new RsaSecurityKey(_rsa)
        {
            KeyId = "deleuze-auth-key" // 鍵識別子 (kid)
        };
    }

    /// <summary>
    /// loginId と tenantId から JWT を生成（Program.cs から呼び出される）
    /// </summary>
    public string GenerateJwt(string loginId, string tenantId)
    {
        var externalUrl = Environment.GetEnvironmentVariable("AUTH_EXTERNAL_URL") 
            ?? _configuration["Authentication:Issuer"] 
            ?? "http://deleuze-auth:8080";

        var credentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, loginId),
            new Claim("login_id", loginId),
            new Claim("tenant_id", tenantId) // 業務アプリDBのスキーマ等を特定するためのクレーム
        };

        var token = new JwtSecurityToken(
            issuer: externalUrl,
            audience: externalUrl,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// JWKS (Json Web Key Set) の公開鍵情報を返却（Program.cs /.well-known/jwks から呼び出される）
    /// </summary>
    public object GetJwks()
    {
        var parameters = _rsa.ExportParameters(false); // 公開鍵パラメータのみ取得

        var jwk = new
        {
            kty = "RSA",
            use = "sig",
            alg = "RS256",
            kid = _key.KeyId,
            n = Base64UrlEncoder.Encode(parameters.Modulus),
            e = Base64UrlEncoder.Encode(parameters.Exponent)
        };

        return new { keys = new[] { jwk } };
    }
}


//deleuse-auth/Services/TokenGenerator.cs

