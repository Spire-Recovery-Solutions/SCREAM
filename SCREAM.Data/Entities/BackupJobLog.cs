using SCREAM.Data.Entities.BackupItems;

namespace SCREAM.Data.Entities
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
        public LogSeverity Severity { get; set; }
    }

    /// <summary>
    /// Severity levels for log entries
    /// </summary>
    public enum LogSeverity
    {
        Debug,
        Info,
        Warning,
        Error,
        Success
    }
}