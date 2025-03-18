namespace SCREAM.Data.Entities.Backup
{
    public class BackupSettings : ScreamDbBaseEntity
    {
        /// <summary>
        /// Maximum number of auto-retries for failed backup items
        /// </summary>
        public int MaxAutoRetries { get; set; } = 3;

        /// <summary>
        /// How long to keep backup history (in days)
        /// </summary>
        public int BackupHistoryRetentionDays { get; set; } = 30;

        /// <summary>
        /// Default maximum allowed packet size for database connections
        /// </summary>
        public string DefaultMaxAllowedPacket { get; set; } = "64M";

        /// <summary>
        /// Whether to send email notifications on backup job completion/failure
        /// </summary>
        public bool SendEmailNotifications { get; set; } = false;

        /// <summary>
        /// Email address to send notifications to
        /// </summary>
        public string? NotificationEmail { get; set; }
    }
}