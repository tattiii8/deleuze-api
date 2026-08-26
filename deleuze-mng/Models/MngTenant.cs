using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeleuzeMng.Models
{
    [Table("tenants", Schema = "mng")]
    public class Tenant
    {
        [Key]
        [Column("tenant_id")]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        [Column("tenant_name")]
        public string TenantName { get; set; } = string.Empty;

        [Column("display_name")]
        public string? DisplayName { get; set; }

        [Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Column("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}