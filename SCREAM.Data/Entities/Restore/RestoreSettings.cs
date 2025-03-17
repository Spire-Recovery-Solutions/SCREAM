namespace SCREAM.Data.Entities.Restore
{
    /// <summary>
    /// Global settings for restore operations
    /// </summary>
    public class RestoreSettings : ScreamDbBaseEntity
    {
        /// <summary>
        /// Maximum number of auto-retries for failed restore items
        /// </summary>
        public int MaxAutoRetries { get; set; } = 3;

        /// <summary>
        /// Whether to overwrite existing database objects by default
        /// </summary>
        public bool OverwriteExistingByDefault { get; set; } = false;

        /// <summary>
        /// Whether to send email notifications on restore job completion/failure
        /// </summary>
        public bool SendEmailNotifications { get; set; } = false;

        /// <summary>
        /// Email address to send notifications to
        /// </summary>
        public string? NotificationEmail { get; set; }
    }
}
