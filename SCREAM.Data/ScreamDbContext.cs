using Microsoft.EntityFrameworkCore;
using SCREAM.Data.Entities;
using SCREAM.Data.Entties;
using SCREAM.Models.Database;

namespace SCREAM.Data;

public class ScreamDbContext(DbContextOptions<ScreamDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseConnection> DatabaseConnections { get; set; }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<DatabaseConnection>(e =>
        {
            e.ToTable("DatabaseConnections");
            e.HasKey(k => k.Id);
            e.Property(p => p.Id).ValueGeneratedOnAdd();


            e.Property(p => p.Type)
                .HasConversion<string>();

            e.Property(p => p.CreatedAt).ValueGeneratedOnAdd();
            e.Property(p => p.UpdatedAt).ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        });
        base.OnModelCreating(mb);
    }
}