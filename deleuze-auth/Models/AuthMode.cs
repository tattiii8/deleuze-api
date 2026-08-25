namespace DeleuzeAuth.Models;

public enum AuthMode
{
    JwtOnly = 0,    // JWT (Bearer) のみ許可
    ApiKeyOnly = 1, // API Key のみ許可
    Both = 2        // 両方許可
}