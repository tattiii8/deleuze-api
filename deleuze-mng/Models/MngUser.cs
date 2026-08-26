using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeleuzeMng.Models
{
    [Table("users", Schema = "mng")]
    public class MngUser
    {
        [Key]
        [Column("subject_id")]
        public string SubjectId { get; set; } = string.Empty;

        [Required]
        [Column("login_id")]
        public string LoginId { get; set; } = string.Empty;

        [Required]
        [Column("user_name")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Column("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}