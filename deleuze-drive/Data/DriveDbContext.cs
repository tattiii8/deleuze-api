using Microsoft.EntityFrameworkCore;
using DeleuzeDrive.Models;

namespace DeleuzeDrive.Data
{
    public class DriveDbContext : DbContext
    {
        public DriveDbContext(DbContextOptions<DriveDbContext> options) : base(options) { }

        public DbSet<FileMetadata> Files { get; set; }
    }
}