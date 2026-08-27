using System.Security.Cryptography;
using System.Text;

namespace DeleuzeAuth.Services
{
    public static class ApiKeyHasher
    {
        public static string Hash(string apiKey)
        {
            var bytes = SHA256.HashData(
                Encoding.UTF8.GetBytes(apiKey));

            return Convert.ToHexString(bytes)
                .ToLowerInvariant();
        }
    }
}
