using Microsoft.EntityFrameworkCore;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.BackupItems;
using SCREAM.Data.Entities.StorageTargets;

namespace SCREAM.Data;

public class ScreamDbContext(DbContextOptions<ScreamDbContext> options) : DbContext(options)
{
    public DbSet<BackupPlan> BackupPlans { get; set; }
    public DbSet<DatabaseConnection> DatabaseConnections { get; set; }
    public DbSet<BackupSchedule> BackupSchedules { get; set; }

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



        // Configure the TPH inheritance for StorageTarget
        mb.Entity<StorageTarget>(entity =>
        {
            // Configure the base entity
            entity.ToTable("StorageTargets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.Description).IsRequired(false);

            // Configure the inheritance mapping
            entity.HasDiscriminator(e => e.Type)
                .HasValue<LocalStorageTarget>(StorageTargetType.Local)
                .HasValue<S3StorageTarget>(StorageTargetType.S3)
                .HasValue<AzureBlobStorageTarget>(StorageTargetType.AzureBlob)
                .HasValue<GoogleCloudStorageTarget>(StorageTargetType.GoogleCloudStorage);
        });

        mb.Entity<BackupPlan>(entity =>
           {
               entity.ToTable("BackupPlans");
               entity.HasKey(bp => bp.Id);
               entity.Property(p => p.Id).ValueGeneratedOnAdd();
               entity.Property(e => e.Name).IsRequired();
               entity.Property(p => p.CreatedAt).ValueGeneratedOnAdd();
               entity.Property(p => p.UpdatedAt).ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
           });

         mb.Entity<BackupSchedule>(entity =>
            {
                entity.ToTable("BackupSchedules");
                entity.HasKey(bs => bs.Id);
                entity.Property(p => p.Id).ValueGeneratedOnAdd();
                entity.Property(p => p.ScheduledType)
                .HasConversion<string>();
                entity.Property(bs => bs.CronExpression)
                      .IsRequired();
               entity.Property(p => p.CreatedAt).ValueGeneratedOnAdd();
               entity.Property(p => p.UpdatedAt).ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            });

        // Configure the S3StorageTarget specific properties
        mb.Entity<S3StorageTarget>()
            .Property(e => e.BucketName).IsRequired();
        mb.Entity<S3StorageTarget>()
            .Property(e => e.AccessKey).IsRequired();
        mb.Entity<S3StorageTarget>()
            .Property(e => e.SecretKey).IsRequired();

        // Configure the AzureBlobStorageTarget specific properties
        mb.Entity<AzureBlobStorageTarget>()
            .Property(e => e.ConnectionString).IsRequired();
        mb.Entity<AzureBlobStorageTarget>()
            .Property(e => e.ContainerName).IsRequired();

        // Configure the GoogleCloudStorageTarget specific properties
        mb.Entity<GoogleCloudStorageTarget>()
            .Property(e => e.BucketName).IsRequired();
        mb.Entity<GoogleCloudStorageTarget>()
            .Property(e => e.ServiceAccountKey).IsRequired();

        // Configure the LocalStorageTarget specific properties
        mb.Entity<LocalStorageTarget>()
            .Property(e => e.Path).IsRequired();

        base.OnModelCreating(mb);
    }
}