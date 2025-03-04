using Microsoft.EntityFrameworkCore;
using SCREAM.Data.Entities;

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

        // Configure the BackupItem hierarchy with TPH (Table Per Hierarchy)
        mb.Entity<BackupItem>(entity =>
        {
            entity.HasKey(k => k.Id);
            entity.Property(p => p.Id).ValueGeneratedOnAdd();
            
            // Configure the discriminator column using the BackupItemType enum
            entity.HasDiscriminator<BackupItemType>("Type")
                .HasValue<TableStructureItem>(BackupItemType.TableStructure)
                .HasValue<TableDataItem>(BackupItemType.TableData)
                .HasValue<ViewItem>(BackupItemType.View)
                .HasValue<TriggerItem>(BackupItemType.Trigger)
                .HasValue<EventItem>(BackupItemType.Event)
                .HasValue<FunctionProcedureItem>(BackupItemType.FunctionProcedure);

            // Configure the base properties
            entity.Property(e => e.Schema).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.IsSelected).HasDefaultValue(true);

            // Store enum as string for better readability in database
            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(50);
            
            entity.Property(p => p.CreatedAt).ValueGeneratedOnAdd();
            entity.Property(p => p.UpdatedAt).ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        });

        // Configure the derived entity specific properties
        mb.Entity<TableStructureItem>()
            .Property(e => e.Engine).HasMaxLength(50);

        mb.Entity<TableDataItem>()
            .Property(e => e.RowCount);

        // Ignore the non-persistent methods for all entities
        mb.Entity<BackupItem>()
            .Ignore(e => e.ConfigureArguments)
            .Ignore(e => e.GetOutputFileName);

        base.OnModelCreating(mb);
    }
}