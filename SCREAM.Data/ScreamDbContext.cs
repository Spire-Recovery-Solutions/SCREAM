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
    public DbSet<BackupItemStatus> BackupItemStatuses { get; set; }
    public DbSet<BackupJobLog> BackupJobLogs { get; set; }
    public DbSet<BackupSettings> BackupSettings { get; set; }

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
            
            // One-to-many relationship between BackupJob and BackupItemStatuses
            e.HasMany<BackupItemStatus>()
                .WithOne(s => s.BackupJob)
                .HasForeignKey(k => k.BackupJobId)
                .OnDelete(DeleteBehavior.Cascade);
                
            // One-to-many relationship between BackupJob and BackupJobLogs
            e.HasMany<BackupJobLog>()
                .WithOne(l => l.BackupJob)
                .HasForeignKey(k => k.BackupJobId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        // BackupItemStatus configuration
        mb.Entity<BackupItemStatus>(e =>
        {
            e.ToTable("BackupItemStatuses");
            e.Property(p => p.Status).HasConversion<string>();
            e.Property(p => p.ErrorMessage).IsRequired(false);
            
            // Many-to-one relationship between BackupItemStatus and BackupItem
            e.HasOne(s => s.BackupItem)
                .WithMany()
                .HasForeignKey(k => k.BackupItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        
        // BackupJobLog configuration
        mb.Entity<BackupJobLog>(e =>
        {
            e.ToTable("BackupJobLogs");
            e.Property(p => p.Severity).HasConversion<string>();
            e.Property(p => p.Title).IsRequired().HasMaxLength(100);
            e.Property(p => p.Message).IsRequired();
            
            // Optional relationship to BackupItemStatus
            e.HasOne(l => l.BackupItemStatus)
                .WithMany()
                .HasForeignKey(k => k.BackupItemStatusId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });
        
        // BackupSettings configuration
        mb.Entity<BackupSettings>(e =>
        {
            e.ToTable("BackupSettings");
            e.HasKey(k => k.Id);
            e.Property(p => p.MaxAutoRetries).HasDefaultValue(3);
            e.Property(p => p.BackupHistoryRetentionDays).HasDefaultValue(30);
            e.Property(p => p.DefaultMaxAllowedPacket).IsRequired().HasDefaultValue("64M");
            e.Property(p => p.SendEmailNotifications).HasDefaultValue(false);
            e.Property(p => p.NotificationEmail).IsRequired(false);
        });

        base.OnModelCreating(mb);
    }
}