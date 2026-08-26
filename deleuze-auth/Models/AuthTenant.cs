using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeleuzeAuth.Models
{
    [Table("tenants", Schema = "auth")]
    public class AuthTenant
    {
        [Key]
        [Column("tenant_id")]
        public string TenantId { get; set; } = string.Empty;
    }
}