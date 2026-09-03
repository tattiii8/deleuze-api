using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace DeleuzeAuth.Services;

public class TokenGenerator
{
    public const string TokenUseUser = "user";
    public const string TokenUseApi = "api";

    private readonly IConfiguration _configuration;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _key;

    public TokenGenerator(IConfiguration configuration)
    {
        _configuration = configuration;

        _rsa = RSA.Create(2048);
        _key = new RsaSecurityKey(_rsa)
        {
            KeyId = "deleuze-auth-key"
        };
    }

    public SecurityKey SigningKey => _key;

    public string Issuer =>
        (Environment.GetEnvironmentVariable("AUTH_EXTERNAL_URL")
         ?? _configuration["Jwt:Issuer"]
         ?? _configuration["Authentication:Issuer"]
         ?? "http://deleuze-auth:8080")
        .TrimEnd('/');

    public string Audience =>
        _configuration["Jwt:Audience"] ?? Issuer;

    public AccessTokenResult GenerateUserToken(
        string subjectId,
        string tenantId,
        string loginId)
    {
        var lifetimeMinutes = GetUserLifetimeMinutes();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subjectId),
            new Claim("tenant_id", tenantId),
            new Claim(JwtRegisteredClaimNames.UniqueName, loginId),
            new Claim("login_id", loginId),
            new Claim("token_use", TokenUseUser),
            new Claim("gty", "password")
        };

        return CreateToken(claims, lifetimeMinutes);
    }

    public AccessTokenResult GenerateApiToken(
        string subjectId,
        string tenantId,
        Guid apiKeyId)
    {
        var lifetimeMinutes = GetApiLifetimeMinutes();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subjectId),
            new Claim("tenant_id", tenantId),
            new Claim("token_use", TokenUseApi),
            new Claim("api_key_id", apiKeyId.ToString()),
            new Claim("client_id", apiKeyId.ToString()),
            new Claim("gty", "client_credentials")
        };

        return CreateToken(claims, lifetimeMinutes);
    }

    /// <summary>
    /// JWKS (Json Web Key Set) の公開鍵情報を返却
    /// </summary>
    public object GetJwks()
    {
        var parameters = _rsa.ExportParameters(false);

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

    private AccessTokenResult CreateToken(
        IEnumerable<Claim> claims,
        int lifetimeMinutes)
    {
        var credentials = new SigningCredentials(
            _key,
            SecurityAlgorithms.RsaSha256);

        var expires = DateTime.UtcNow.AddMinutes(lifetimeMinutes);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new AccessTokenResult
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresIn = lifetimeMinutes * 60
        };
    }

    private int GetUserLifetimeMinutes()
    {
        if (int.TryParse(
                _configuration["Jwt:ExpiresMinutes"],
                out var minutes) &&
            minutes > 0)
        {
            return minutes;
        }

        return 60;
    }

    private int GetApiLifetimeMinutes()
    {
        if (int.TryParse(
                _configuration["Jwt:ApiExpiresMinutes"],
                out var minutes) &&
            minutes > 0)
        {
            return minutes;
        }

        return 15;
    }
}

public class AccessTokenResult
{
    public string AccessToken { get; set; } = string.Empty;

    public int ExpiresIn { get; set; }
}
