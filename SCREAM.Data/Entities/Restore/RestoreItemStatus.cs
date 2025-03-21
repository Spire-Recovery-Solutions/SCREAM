using SCREAM.Data.Entities.Backup.BackupItems;

namespace SCREAM.Data.Entities.Restore
{
    /// <summary>
    /// Tracks the status of individual restore items within a restore job
    /// </summary>
    public class RestoreItemStatus : ScreamDbBaseEntity
    {
        /// <summary>
        /// The restore job this status belongs to
        /// </summary>
        public long RestoreJobId { get; set; }

        /// <summary>
        /// The restore item this status is tracking
        /// </summary>
        public long RestoreItemId { get; set; }
        public BackupItem RestoreItem { get; set; } = null!;

        /// <summary>
        /// Current execution status
        /// </summary>
        public TaskStatus Status { get; set; }

        /// <summary>
        /// Number of times this item has been retried
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// Most recent error message if any
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// When the restore of this item started
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// When the restore of this item completed (successfully or not)
        /// </summary>
        public DateTime? CompletedAt { get; set; }
    }
}
