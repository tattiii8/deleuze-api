using System.Text.Json;

namespace DeleuzeAuth.Models
{
    public class IssueTokenRequest
    {
        public string? GrantType { get; set; }

        public string? TenantId { get; set; }

        public string? Username { get; set; }

        public string? LoginId { get; set; }

        public string? Password { get; set; }

        public string? ClientId { get; set; }

        public string? ClientSecret { get; set; }

        public string? ResolvedUsername =>
            FirstNonEmpty(Username, LoginId);

        public static IssueTokenRequest FromForm(IFormCollection form)
        {
            return new IssueTokenRequest
            {
                GrantType = FirstForm(form, "grant_type", "grantType"),
                TenantId = FirstForm(form, "tenant_id", "tenantId"),
                Username = FirstForm(form, "username"),
                LoginId = FirstForm(form, "login_id", "loginId"),
                Password = FirstForm(form, "password"),
                ClientId = FirstForm(form, "client_id", "clientId"),
                ClientSecret = FirstForm(form, "client_secret", "clientSecret")
            };
        }

        public static IssueTokenRequest FromJson(JsonElement element)
        {
            return new IssueTokenRequest
            {
                GrantType = FirstJson(element, "grant_type", "grantType"),
                TenantId = FirstJson(element, "tenant_id", "tenantId"),
                Username = FirstJson(element, "username"),
                LoginId = FirstJson(element, "login_id", "loginId"),
                Password = FirstJson(element, "password"),
                ClientId = FirstJson(element, "client_id", "clientId"),
                ClientSecret = FirstJson(element, "client_secret", "clientSecret")
            };
        }

        public void ApplyHeaderAuth(HttpRequest httpRequest)
        {
            if (httpRequest.Headers.TryGetValue("Authorization", out var authHeaderValues))
            {
                var header = authHeaderValues.ToString().Trim();
                if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var encodedCredentials = header[6..].Trim();
                        var credentialsBytes = Convert.FromBase64String(encodedCredentials);
                        var credentials = System.Text.Encoding.UTF8.GetString(credentialsBytes);
                        var parts = credentials.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            if (string.IsNullOrWhiteSpace(ClientId)) ClientId = parts[0];
                            if (string.IsNullOrWhiteSpace(ClientSecret)) ClientSecret = parts[1];
                        }
                    }
                    catch
                    {
                        // Ignore invalid basic auth header formatting
                    }
                }
            }
        }

        private static string? FirstForm(IFormCollection form, params string[] names)
        {
            foreach (var name in names)
            {
                if (form.TryGetValue(name, out var value) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    return value.ToString();
                }
            }

            return null;
        }

        private static string? FirstJson(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var property) &&
                    property.ValueKind == JsonValueKind.String)
                {
                    var value = property.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                foreach (var name in names)
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }
            }

            return null;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}
