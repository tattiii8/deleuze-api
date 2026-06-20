namespace DeleuzeAuth.Services;

public class BCryptPasswordHasher : IPasswordHasher
{
    // 新規ユーザー登録時に使用（今回はログインのみですが拡張用として保持）
    public string HashPassword(string password) => 
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);

    // 生入力されたパスワードと、DB内のハッシュを安全にソルト込みで検証
    public bool VerifyPassword(string password, string passwordHash) => 
        BCrypt.Net.BCrypt.Verify(password, passwordHash);
}