namespace DeleuzeAuth.Services.Authentication;

public class BCryptPasswordHasher : IPasswordHasher
{
    public string HashPassword(string password)
        => BCrypt.Net.BCrypt.HashPassword(
            password,
            workFactor: 11);

    public bool VerifyPassword(
        string password,
        string passwordHash)
        => BCrypt.Net.BCrypt.Verify(
            password,
            passwordHash);
}