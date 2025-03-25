namespace SCREAM.Data.Entities.Backup
{
    /// <summary>
    /// Represents a backup job that tracks the execution of a backup plan.
    /// </summary>
    public class BackupJob : ScreamDbBaseEntity
    {
        public long BackupPlanId { get; set; }

        /// <summary>
        /// The status of the backup job (e.g., Pending, Running, Completed, Failed).
        /// TODO: THIS IS WRONG AI SLOP 
        /// </summary>
        public required TaskStatus Status { get; set; }

        public bool HasTriggeredRestore { get; set; } = false;

        /// <summary>
        /// The timestamp when the backup job started.
        /// </summary>
        public required DateTime StartedAt { get; set; }

        /// <summary>
        /// The timestamp when the backup job was completed.
        /// Nullable since a job may still be in progress.
        /// </summary>
        public DateTime? CompletedAt { get; set; }


        public ICollection<BackupItemStatus> BackupItemStatuses { get; set; }

    }
}