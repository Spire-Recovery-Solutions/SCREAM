using SCREAM.Data.Entities.Database.DatabaseItems;
using System.Text.Json.Serialization;

namespace SCREAM.Data.Entities.Backup.BackupItems;

/// <summary>
/// Base class for all database objects that can be backed up
/// </summary>
public class BackupItem : ScreamDbBaseEntity
{
    /// <summary>
    /// The ID of the backup plan this item belongs to.
    /// </summary>
    public long? BackupPlanId { get; set; }


    /// <summary>
    /// Indicates whether the item is selected for backup.
    /// </summary>
    public bool IsSelected { get; set; } = true;

    public long DatabaseItemId { get; set; }

    public DatabaseItem DatabaseItem { get; set; } = null!;
}