using SCREAM.Data.Entities;
using SCREAM.Data.Entities.BackupItems;

namespace SCREAM.Business.Services
{
    /// <summary>
    /// Service for managing backup jobs
    /// </summary>
    public interface IBackupJobService
    {
        /// <summary>
        /// Gets all backup jobs
        /// </summary>
        Task<List<BackupJob>> GetAllJobsAsync();

        /// <summary>
        /// Gets detailed information about a specific backup job
        /// </summary>
        Task<BackupJob?> GetJobDetailsAsync(long jobId);

        /// <summary>
        /// Gets log entries for a specific backup job
        /// </summary>
        Task<List<BackupJobLog>> GetJobLogsAsync(long jobId);

        /// <summary>
        /// Retries a failed backup job
        /// </summary>
        Task RetryJobAsync(long jobId);

        /// <summary>
        /// Cancels a running backup job
        /// </summary>
        Task CancelJobAsync(long jobId);
    }

    /// <summary>
    /// Service for managing backup item statuses
    /// </summary>
    public interface IBackupItemStatusService
    {
        /// <summary>
        /// Gets all backup item statuses for a specific job
        /// </summary>
        Task<List<BackupItemStatus>> GetStatusesForJobAsync(long jobId);

        /// <summary>
        /// Retries a specific backup item
        /// </summary>
        Task RetryItemAsync(long itemStatusId);

        /// <summary>
        /// Skips a specific backup item
        /// </summary>
        Task SkipItemAsync(long itemStatusId);
    }

    /// <summary>
    /// Service for managing backup settings
    /// </summary>
    public interface IBackupSettingsService
    {
        /// <summary>
        /// Gets global backup settings
        /// </summary>
        Task<BackupSettings> GetSettingsAsync();
        
        /// <summary>
        /// Updates global backup settings
        /// </summary>
        Task UpdateSettingsAsync(BackupSettings settings);
    }

    /// <summary>
    /// Global backup settings
    /// </summary>
    public class BackupSettings
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