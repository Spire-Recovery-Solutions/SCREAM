namespace SCREAM.Data.Entities.BackupItems
{
    /// <summary>
    /// Tracks the status of individual backup items within a backup job
    /// </summary>
    public class BackupItemStatus : ScreamDbBaseEntity
    {
        /// <summary>
        /// The backup job this status belongs to
        /// </summary>
        public long BackupJobId { get; set; }

        public BackupJob BackupJob { get; set; } = null!;

        /// <summary>
        /// The backup item this status is tracking
        /// </summary>
        public long BackupItemId { get; set; }

        public BackupItem BackupItem { get; set; } = null!;

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
        /// When the backup of this item started
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// When the backup of this item completed (successfully or not)
        /// </summary>
        public DateTime? CompletedAt { get; set; }
    }
}