using System;

namespace DeleuzeAuth.Models
{
    public class ApiKey
    {
        public Guid Id { get; set; }

        public string SubjectId { get; set; } = string.Empty;

        public string TenantId { get; set; } = string.Empty;

        public string KeyHash { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        public DateTimeOffset? RevokedAt { get; set; }
    }
}