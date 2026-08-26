// DeleuzeAuth/Data/AuthDbContext.cs

using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Models;

namespace DeleuzeAuth.Data

{
    public class AuthDbContext : DbContext // <- public が付いているか確認
    {
        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
        {
        }

        public DbSet<AuthUser> Users { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // モデル設定など
        }
    }
}