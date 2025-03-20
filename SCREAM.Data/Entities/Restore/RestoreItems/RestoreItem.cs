using SCREAM.Data.Entities.Backup.BackupItems;
using SCREAM.Data.Entities.Database.DatabaseItems;

namespace SCREAM.Data.Entities.Restore.RestoreItems
{
    /// <summary>
    /// Represents a database object that can be restored.
    /// This item encapsulates the reference to a backed-up database object, 
    /// along with additional metadata required during the restore process.
    /// </summary>
    public class RestoreItem : ScreamDbBaseEntity
    {
        /// <summary>
        /// Gets or sets the identifier of the restore plan that this item belongs to.
        /// </summary>
        public long? RestorePlanId { get; set; }

        /// <summary>
        /// Gets or sets the execution order of the restore item within the restore plan.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the restore item is selected for restoration.
        /// </summary>
        public bool IsSelected { get; set; } = true;

        /// <summary>
        /// Gets or sets the identifier of the corresponding backup item.
        /// </summary>
        public long BackupItemId { get; set; }

        public long DatabaseItemId { get; set; }

        /// <summary>
        /// Gets or sets the database object associated with this restore item.
        /// This object contains the details of the backup item that will be used during restoration.
        /// </summary>
        public DatabaseItem DatabaseItems { get; set; } = null!;

        public BackupItem BackupItem { get; set; } = null!;
    }
}