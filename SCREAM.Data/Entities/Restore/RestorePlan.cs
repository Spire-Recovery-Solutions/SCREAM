using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Entities.Restore.RestoreItems;
using SCREAM.Data.Entities.StorageTargets;

namespace SCREAM.Data.Entities.Restore
{
    /// <summary>
    /// Represents a plan for restoring data from a completed backup job.
    /// </summary>
    public class RestorePlan : ScreamDbBaseEntity
    {
        public required string Name { get; set; }
        public required string Description { get; set; }

        // Navigation properties and foreign keys for related entities
        public long DatabaseConnectionId { get; set; }
        public DatabaseConnection DatabaseConnection { get; set; } = null!;

        public long StorageTargetId { get; set; }
        public StorageTarget StorageTarget { get; set; } = null!;

        public long SourceBackupJobId { get; set; }
        public BackupJob SourceBackupJob { get; set; } = null!;

        public bool IsActive { get; set; }
        public bool OverwriteExisting { get; set; }


        // Related collections
        public ICollection<RestoreJob> Jobs { get; set; } = new List<RestoreJob>();
        public ICollection<RestoreItem> Items { get; set; } = new List<RestoreItem>();
    }
}
