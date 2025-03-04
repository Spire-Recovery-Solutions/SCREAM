using Microsoft.EntityFrameworkCore;
using SCREAM.Data.Entties;
using SCREAM.Models.Database;

namespace SCREAM.Data;

public class ScreamDbContext(DbContextOptions<ScreamDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseConnection> DatabaseConnections { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<DatabaseConnection>(entity =>
            {
                entity.ToTable("DatabaseConnections");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Type)
                      .HasConversion<string>();
            });   
        base.OnModelCreating(modelBuilder);
    }
}