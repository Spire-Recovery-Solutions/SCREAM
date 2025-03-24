namespace SCREAM.Data.Entities.Restore
{
    /// <summary>
    /// Represents a restore job that tracks the execution of a restore plan.
    /// </summary>
    public class RestoreJob : ScreamDbBaseEntity
    {
        public long RestorePlanId { get; set; }

        /// <summary>
        /// The restore plan associated with this job.
        /// </summary>
        public RestorePlan RestorePlan { get; set; } = null!;

        /// <summary>
        /// The status of the restore job (e.g., Pending, Running, Completed, Failed).
        /// </summary>
        public required TaskStatus Status { get; set; }

        /// <summary>
        /// The timestamp when the restore job started.
        /// </summary>
        public required DateTime StartedAt { get; set; }

        public bool IsCompressed { get; set; }
        public bool IsEncrypted { get; set; }

        /// <summary>
        /// The timestamp when the restore job was completed.
        /// Nullable since a job may still be in progress.
        /// </summary>
        public DateTime? CompletedAt { get; set; }
        
        
        /// <summary>
        /// Collection of item statuses for this restore job
        /// </summary>
        public ICollection<RestoreItem> RestoreItems { get; set; }
    }

}
