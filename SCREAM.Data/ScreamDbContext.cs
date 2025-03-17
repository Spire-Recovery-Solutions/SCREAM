using Microsoft.EntityFrameworkCore;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Entities.Backup.BackupItems;
using SCREAM.Data.Entities.Restore;
using SCREAM.Data.Entities.Restore.RestoreItems;
using SCREAM.Data.Entities.StorageTargets;

namespace SCREAM.Data;

public class ScreamDbContext(DbContextOptions<ScreamDbContext> options) : DbContext(options)
{
    public DbSet<BackupPlan> BackupPlans { get; set; }
    public DbSet<DatabaseConnection> DatabaseConnections { get; set; }
    public DbSet<BackupJob> BackupJobs { get; set; }
    public DbSet<BackupItemStatus> BackupItemStatuses { get; set; }
    public DbSet<BackupJobLog> BackupJobLogs { get; set; }
    public DbSet<BackupSettings> BackupSettings { get; set; }
    public DbSet<BackupItem> BackupItems { get; set; }
    public DbSet<StorageTarget> StorageTargets { get; set; }

    //Restore
    public DbSet<RestorePlan> RestorePlans { get; set; }
    public DbSet<RestoreJob> RestoreJobs { get; set; }
    public DbSet<RestoreItemStatus> RestoreItemStatuses { get; set; }
    public DbSet<RestoreJobLog> RestoreJobLogs { get; set; }
    public DbSet<RestoreSettings> RestoreSettings { get; set; }
    public DbSet<RestoreItem> RestoreItems { get; set; }

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

        // BackupPlan configuration
        mb.Entity<BackupPlan>(e =>
        {
            e.ToTable("BackupPlans");

            e.HasMany(p => p.Jobs)
                .WithOne(j => j.BackupPlan)
                .HasForeignKey(j => j.BackupPlanId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(p => p.DatabaseConnection)
                .WithMany()
                .HasForeignKey(p => p.DatabaseConnectionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.StorageTarget)
                .WithMany()
                .HasForeignKey(p => p.StorageTargetId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(p => p.Items)
                .WithOne()
                .HasForeignKey(i => i.BackupPlanId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(p => p.Name).IsRequired();
        });

        // BackupItem configuration
        mb.Entity<BackupItem>(e =>
        {
            e.ToTable("BackupItems");

            e.HasDiscriminator<DatabaseItemType>("Type")
                .HasValue<TableStructureItem>(DatabaseItemType.TableStructure)
                .HasValue<TableDataItem>(DatabaseItemType.TableData)
                .HasValue<ViewItem>(DatabaseItemType.View)
                .HasValue<TriggerItem>(DatabaseItemType.Trigger)
                .HasValue<EventItem>(DatabaseItemType.Event)
                .HasValue<FunctionProcedureItem>(DatabaseItemType.FunctionProcedure);

            e.Property(p => p.Schema).IsRequired().HasMaxLength(100);
            e.Property(p => p.Name).HasMaxLength(100);
            e.Property(p => p.IsSelected).HasDefaultValue(true);
            e.Property(p => p.Type).HasConversion<string>().HasMaxLength(50);
        });

        mb.Entity<TableStructureItem>().Property(e => e.Engine).HasMaxLength(50);
        mb.Entity<TableDataItem>().Property(e => e.RowCount);

        // DatabaseConnection configuration
        mb.Entity<DatabaseConnection>(e =>
        {
            e.ToTable("DatabaseConnections");
            e.Property(p => p.Type).HasConversion<string>();
        });

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

        // BackupJob configuration
        mb.Entity<BackupJob>(e =>
        {
            e.ToTable("BackupJobs");
            e.Property(p => p.Status).HasConversion<string>();

            // One-to-many relationship between BackupJob and BackupItemStatuses
            e.HasMany<BackupItemStatus>()
                .WithOne()
                .HasForeignKey(k => k.BackupJobId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-many relationship between BackupJob and BackupJobLogs
            e.HasMany<BackupJobLog>()
                .WithOne()
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

      mb.Entity<RestoreItem>(e =>
        {
            e.ToTable("RestoreItems");

            e.HasDiscriminator<DatabaseItemType>("Type")
                .HasValue<TableStructureItem>(DatabaseItemType.TableStructure)
                .HasValue<TableDataItem>(DatabaseItemType.TableData)
                .HasValue<ViewItem>(DatabaseItemType.View)
                .HasValue<TriggerItem>(DatabaseItemType.Trigger)
                .HasValue<EventItem>(DatabaseItemType.Event)
                .HasValue<FunctionProcedureItem>(DatabaseItemType.FunctionProcedure);

            e.Property(p => p.Schema).IsRequired().HasMaxLength(100);
            e.Property(p => p.Name).HasMaxLength(100);
            e.Property(p => p.IsSelected).HasDefaultValue(true);
            e.Property(p => p.Type).HasConversion<string>().HasMaxLength(50);
            
            e.HasOne(i => i.BackupItem)
                .WithMany()
                .HasForeignKey(i => i.BackupItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<RestoreItemStatus>(e =>
        {
            e.ToTable("RestoreItemStatuses");
            e.Property(p => p.Status).HasConversion<string>();
            e.Property(p => p.ErrorMessage).IsRequired(false);

            e.HasOne(s => s.RestoreItem)
                .WithMany()
                .HasForeignKey(k => k.RestoreItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<RestorePlan>(e =>
        {
            e.ToTable("RestorePlans");

            e.Property(p => p.Name).IsRequired();

            e.HasMany(p => p.Jobs)
                .WithOne(j => j.RestorePlan)
                .HasForeignKey(j => j.RestorePlanId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(p => p.DatabaseConnection)
                .WithMany()
                .HasForeignKey(p => p.DatabaseConnectionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.StorageTarget)
                .WithMany()
                .HasForeignKey(p => p.StorageTargetId)
                .OnDelete(DeleteBehavior.Restrict);
                
            e.HasOne(p => p.SourceBackupJob)
                .WithMany()
                .HasForeignKey(p => p.SourceBackupJobId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(p => p.Items)
                .WithOne()
                .HasForeignKey(i => i.RestorePlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

         mb.Entity<RestoreJob>(e =>
        {
            e.ToTable("RestoreJobs");
            e.Property(p => p.Status).HasConversion<string>();

            e.HasMany(h => h.RestoreItemStatuses)
                .WithOne()
                .HasForeignKey(k => k.RestoreJobId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-many relationship between RestoreJob and RestoreJobLogs
            e.HasMany<RestoreJobLog>()
                .WithOne()
                .HasForeignKey(k => k.RestoreJobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // RestoreJobLog configuration
        mb.Entity<RestoreJobLog>(e =>
        {
            e.ToTable("RestoreJobLogs");
            e.Property(p => p.Severity).HasConversion<string>();
            e.Property(p => p.Title).IsRequired().HasMaxLength(100);
            e.Property(p => p.Message).IsRequired();

            // Optional relationship to RestoreItemStatus
            e.HasOne(l => l.RestoreItemStatus)
                .WithMany()
                .HasForeignKey(k => k.RestoreItemStatusId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });
        
        // RestoreSettings configuration
        mb.Entity<RestoreSettings>(e =>
        {
            e.ToTable("RestoreSettings");
            e.HasKey(k => k.Id);
            e.Property(p => p.MaxAutoRetries).HasDefaultValue(3);
            e.Property(p => p.OverwriteExistingByDefault).HasDefaultValue(false);
            e.Property(p => p.SendEmailNotifications).HasDefaultValue(false);
            e.Property(p => p.NotificationEmail).IsRequired(false);
        });

        base.OnModelCreating(mb);
    }
}