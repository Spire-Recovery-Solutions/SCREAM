using Microsoft.Extensions.Logging;

namespace SCREAM.Data.Entities.Backup
{
    /// <summary>
    /// Represents a log entry for a backup job
    /// </summary>
    public class BackupJobLog : ScreamDbBaseEntity
    {
        /// <summary>
        /// The backup job this log belongs to
        /// </summary>
        public long BackupJobId { get; set; }

        /// <summary>
        /// The related backup item status if applicable
        /// </summary>
        public long? BackupItemStatusId { get; set; }

        public BackupItemStatus? BackupItemStatus { get; set; }

        /// <summary>
        /// When the log entry was created
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// The log title/summary
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The log message
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The severity level of the log entry
        /// </summary>
        public LogLevel Severity { get; set; }
    }
}