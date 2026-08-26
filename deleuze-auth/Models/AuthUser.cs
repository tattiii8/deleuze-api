using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeleuzeAuth.Models
{
    [Table("users", Schema = "auth")]
    public class AuthUser
    {
        [Key]
        [Column("subject_id")]
        public string SubjectId { get; set; } = string.Empty;

        [Required]
        [Column("tenant_id")]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        [Column("login_id")]
        public string LoginId { get; set; } = string.Empty;

        [Required]
        [Column("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Column("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}