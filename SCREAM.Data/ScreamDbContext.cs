using Microsoft.EntityFrameworkCore;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Entities.Backup.BackupItems;
using SCREAM.Data.Entities.Database;
using SCREAM.Data.Entities.Restore;
using SCREAM.Data.Entities.StorageTargets;

namespace SCREAM.Data;

public class ScreamDbContext(DbContextOptions<ScreamDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseItem> DatabaseItems { get; set; }
    public DbSet<BackupPlan> BackupPlans { get; set; }
    public DbSet<DatabaseTarget> DatabaseTargets { get; set; }
    public DbSet<BackupJob> BackupJobs { get; set; }
    public DbSet<BackupItemStatus> BackupItemStatuses { get; set; }
    public DbSet<BackupJobLog> BackupJobLogs { get; set; }
    public DbSet<BackupSettings> BackupSettings { get; set; }
    public DbSet<BackupItem> BackupItems { get; set; }
    public DbSet<StorageTarget> StorageTargets { get; set; }

    //Restore
    public DbSet<RestorePlan> RestorePlans { get; set; }
    public DbSet<RestoreJob> RestoreJobs { get; set; }
    public DbSet<RestoreJobLog> RestoreJobLogs { get; set; }
    public DbSet<RestoreSettings> RestoreSettings { get; set; }

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

            e.HasOne(p => p.DatabaseTarget)
                .WithMany()
                .HasForeignKey(p => p.DatabaseTargetId)
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

        mb.Entity<BackupItem>(e =>
        {
            e.ToTable("BackupItems");

            e.HasOne(b => b.DatabaseItem)
                .WithMany()
                .HasForeignKey(b => b.DatabaseItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<DatabaseTableStructureItems>().Property(e => e.Engine).HasMaxLength(50);
        mb.Entity<DatabaseTableDataItems>().Property(e => e.RowCount);

        // DatabaseConnection configuration
        mb.Entity<DatabaseTarget>(e =>
        {
            e.ToTable("DatabaseTargets");
            e.Property(p => p.Type).HasConversion<string>();
        });

        mb.Entity<DatabaseItem>(e =>
        {
            e.ToTable("DatabaseItems");

            e.HasDiscriminator<DatabaseItemType>(d => d.Type)
                .HasValue<DatabaseTableStructureItems>(DatabaseItemType.TableStructure)
                .HasValue<DatabaseTableDataItems>(DatabaseItemType.TableData)
                .HasValue<DatabaseViewItems>(DatabaseItemType.View)
                .HasValue<DatabaseTriggerItems>(DatabaseItemType.Trigger)
                .HasValue<DatabaseEventItems>(DatabaseItemType.Event)
                .HasValue<DatabaseFunctionProcedureItems>(DatabaseItemType.FunctionProcedure);

            e.Property(p => p.Schema).IsRequired().HasMaxLength(100);
            e.Property(p => p.Name).HasMaxLength(100);
        });

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
            e.HasMany(m => m.BackupItemStatuses)
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
            e.Property(p => p.Status).HasConversion<string>();
            e.Property(p => p.ErrorMessage).IsRequired(false);
            e.Property(p => p.RetryCount).HasDefaultValue(0);

            e.HasOne(s => s.DatabaseItem)
                .WithMany()
                .HasForeignKey(k => k.DatabaseItemId)
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

            e.HasOne(p => p.DatabaseTarget)
                .WithMany()
                .HasForeignKey(p => p.DatabaseTargetId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.SourceBackupPlan)
                .WithMany()
                .HasForeignKey(p => p.SourceBackupPlanId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(p => p.Items)
                .WithMany()
                .UsingEntity<RestorePlanBackupItem>(
                    j => j.HasOne(rb => rb.BackupItem)
                        .WithMany()
                        .HasForeignKey(rb => rb.BackupItemId),
                    j => j.HasOne(rb => rb.RestorePlan)
                        .WithMany()
                        .HasForeignKey(rb => rb.RestorePlanId)
                );
        });

        mb.Entity<RestoreJob>(e =>
        {
            e.ToTable("RestoreJobs");
            e.Property(p => p.Status).HasConversion<string>();
            e.Property(p => p.IsCompressed).HasDefaultValue(false);
            e.Property(p => p.IsEncrypted).HasDefaultValue(false);

            e.HasMany(j => j.RestoreItems)
                .WithOne(i => i.RestoreJob)
                .HasForeignKey(i => i.RestoreJobId)
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
            e.HasOne(l => l.RestoreItem)
                .WithMany()
                .HasForeignKey(k => k.RestoreItemId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // RestoreSettings configuration
        mb.Entity<RestoreSettings>(e =>
        {
            e.ToTable("RestoreSettings");
            e.HasKey(k => k.Id);
            e.Property(p => p.OverwriteExistingByDefault).HasDefaultValue(false);
            e.Property(p => p.UseParallelExecution).HasDefaultValue(true);
            e.Property(p => p.SendEmailNotifications).HasDefaultValue(false);
            e.Property(p => p.NotificationEmail).IsRequired(false);
            e.Property(p => p.ImportTimeout).HasDefaultValue(3600);
        });

        base.OnModelCreating(mb);
    }
}