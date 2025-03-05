namespace SCREAM.Data.Entities
{
    /// <summary>
    /// Represents a backup job that tracks the execution of a backup plan.
    /// </summary>
    public class BackupJob : ScreamDbBaseEntity
    {
        public long BackupPlanId { get; set; }
        /// <summary>
        /// The backup plan associated with this job.
        /// </summary>
        public required BackupPlan BackupPlan { get; set; }

        /// <summary>
        /// The status of the backup job (e.g., Pending, Running, Completed, Failed).
        /// </summary>
        public required TaskStatus Status { get; set; }

        /// <summary>
        /// The timestamp when the backup job started.
        /// </summary>
        public required DateTime StartedAt { get; set; }

        /// <summary>
        /// The timestamp when the backup job was completed.
        /// Nullable since a job may still be in progress.
        /// </summary>
        public DateTime? CompletedAt { get; set; }
    }
}