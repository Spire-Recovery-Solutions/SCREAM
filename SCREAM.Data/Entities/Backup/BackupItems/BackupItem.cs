using System.Text.Json.Serialization;
using CliWrap.Builders;

namespace SCREAM.Data.Entities.Backup.BackupItems;

/// <summary>
/// Base class for all database objects that can be backed up
/// </summary>
[JsonDerivedType(typeof(TableStructureItem), typeDiscriminator: (int)DatabaseItemType.TableStructure)]
[JsonDerivedType(typeof(TableDataItem), typeDiscriminator: (int)DatabaseItemType.TableData)]
[JsonDerivedType(typeof(ViewItem), typeDiscriminator: (int)DatabaseItemType.View)]
[JsonDerivedType(typeof(TriggerItem), typeDiscriminator: (int)DatabaseItemType.Trigger)]
[JsonDerivedType(typeof(EventItem), typeDiscriminator: (int)DatabaseItemType.Event)]
[JsonDerivedType(typeof(FunctionProcedureItem), typeDiscriminator: (int)DatabaseItemType.FunctionProcedure)]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "objType")]
public abstract class BackupItem : ScreamDbBaseEntity
{
    /// <summary>
    /// The ID of the backup plan this item belongs to.
    /// </summary>
    public long? BackupPlanId { get; set; }

    /// <summary>
    /// The schema (database) the item belongs to.
    /// </summary>
    public string Schema { get; set; } = string.Empty;

    /// <summary>
    /// The name of the item (e.g., table name, view name). May be empty for schema-level objects.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the item is selected for backup.
    /// </summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>
    /// The type of backup item.
    /// </summary>
    public abstract DatabaseItemType Type { get; set; }


    /// <summary>
    /// Configures the CliWrap ArgumentsBuilder with arguments specific to this item type
    /// </summary>
    public abstract void ConfigureArguments(ArgumentsBuilder args, string host, string user, string password,
        string maxPacketSize);

    /// <summary>
    /// Gets the output filename for this backup item
    /// </summary>
    public abstract string GetOutputFileName();
}