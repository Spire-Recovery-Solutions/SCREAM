using SCREAM.Data.Entities.Backup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCREAM.Data.Entities.Restore
{
    /// <summary>
    /// Represents a log entry for a restore job
    /// </summary>
    public class RestoreJobLog : ScreamDbBaseEntity
    {
        /// <summary>
        /// The restore job this log belongs to
        /// </summary>
        public long RestoreJobId { get; set; }

        /// <summary>
        /// The related restore item status if applicable
        /// </summary>
        public long? RestoreItemStatusId { get; set; }

        public RestoreItemStatus? RestoreItemStatus { get; set; }

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
}
