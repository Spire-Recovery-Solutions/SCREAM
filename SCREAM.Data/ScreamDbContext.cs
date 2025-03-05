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
    public DbSet<BackupJob> BackupJobs { get; set; }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Configure base entity timestamps
        foreach (var entityType in mb.Model.GetEntityTypes()
                     .Where(e => typeof(ScreamDbBaseEntity).IsAssignableFrom(e.ClrType)))
        {
            mb.Entity(entityType.ClrType, builder =>
            {
                builder.Property("CreatedAt")
                    .ValueGeneratedOnAdd()
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                builder.Property("UpdatedAt")
                    .ValueGeneratedOnAddOrUpdate()
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .IsConcurrencyToken();
            });
        }

        // DatabaseConnection configuration
        mb.Entity<DatabaseConnection>(e =>
        {
            e.ToTable("DatabaseConnections");
            e.Property(p => p.Type).HasConversion<string>();
        });

        // BackupItem configuration
        mb.Entity<BackupItem>(e =>
        {
            e.HasDiscriminator<BackupItemType>("Type")
                .HasValue<TableStructureItem>(BackupItemType.TableStructure)
                .HasValue<TableDataItem>(BackupItemType.TableData)
                .HasValue<ViewItem>(BackupItemType.View)
                .HasValue<TriggerItem>(BackupItemType.Trigger)
                .HasValue<EventItem>(BackupItemType.Event)
                .HasValue<FunctionProcedureItem>(BackupItemType.FunctionProcedure);

            e.Property(p => p.Schema).IsRequired().HasMaxLength(100);
            e.Property(p => p.Name).HasMaxLength(100);
            e.Property(p => p.IsSelected).HasDefaultValue(true);
            e.Property(p => p.Type).HasConversion<string>().HasMaxLength(50);
        });

        mb.Entity<TableStructureItem>().Property(e => e.Engine).HasMaxLength(50);
        mb.Entity<TableDataItem>().Property(e => e.RowCount);

        // StorageTarget configuration
        mb.Entity<StorageTarget>(e =>
        {
            e.ToTable("StorageTargets");
            e.Property(p => p.Name).IsRequired();
            e.Property(p => p.Type).IsRequired();
            e.Property(p => p.Description).IsRequired(false);
            e.HasDiscriminator(p => p.Type)
                .HasValue<LocalStorageTarget>(StorageTargetType.Local)
                .HasValue<S3StorageTarget>(StorageTargetType.S3)
                .HasValue<AzureBlobStorageTarget>(StorageTargetType.AzureBlob)
                .HasValue<GoogleCloudStorageTarget>(StorageTargetType.GoogleCloudStorage);
        });

        // StorageTarget derived types
        mb.Entity<S3StorageTarget>(e =>
        {
            e.Property(p => p.BucketName).IsRequired();
            e.Property(p => p.AccessKey).IsRequired();
            e.Property(p => p.SecretKey).IsRequired();
        });

        mb.Entity<AzureBlobStorageTarget>(e =>
        {
            e.Property(p => p.ConnectionString).IsRequired();
            e.Property(p => p.ContainerName).IsRequired();
        });

        mb.Entity<GoogleCloudStorageTarget>(e =>
        {
            e.Property(p => p.BucketName).IsRequired();
            e.Property(p => p.ServiceAccountKey).IsRequired();
        });

        mb.Entity<LocalStorageTarget>().Property(e => e.Path).IsRequired();

        // BackupPlan configuration
        mb.Entity<BackupPlan>(e =>
        {
            e.ToTable("BackupPlans");

            // One-to-one relationship between BackupPlan and BackupSchedule
            e.HasOne(p => p.Schedule)
                .WithOne(w => w.BackupPlan)
                .HasForeignKey<BackupSchedule>(k => k.BackupPlanId);
            
            // One-to-many relationship between BackupPlan and BackupJobs
            e.HasMany<BackupJob>()
                .WithOne(j => j.BackupPlan)
                .HasForeignKey(k => k.BackupPlanId);

            e.Property(p => p.Name).IsRequired();
        });

        // BackupSchedule configuration
        mb.Entity<BackupSchedule>(e =>
        {
            e.ToTable("BackupSchedules");
            e.Property(p => p.ScheduledType).HasConversion<string>();
        });

        // BackupJob configuration
        mb.Entity<BackupJob>(e =>
        {
            e.ToTable("BackupJobs");
            e.Property(p => p.Status).HasConversion<string>();
        });

        base.OnModelCreating(mb);
    }
}