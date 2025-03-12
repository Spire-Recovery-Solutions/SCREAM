using System.Text.Json.Serialization;
using CliWrap.Builders;

namespace SCREAM.Data.Entities.BackupItems;

/// <summary>
/// Base class for all database objects that can be backed up
/// </summary>
[JsonDerivedType(typeof(TableStructureItem), typeDiscriminator: (int)BackupItemType.TableStructure)]
[JsonDerivedType(typeof(TableDataItem), typeDiscriminator: (int)BackupItemType.TableData)]
[JsonDerivedType(typeof(ViewItem), typeDiscriminator: (int)BackupItemType.View)]
[JsonDerivedType(typeof(TriggerItem), typeDiscriminator: (int)BackupItemType.Trigger)]
[JsonDerivedType(typeof(EventItem), typeDiscriminator: (int)BackupItemType.Event)]
[JsonDerivedType(typeof(FunctionProcedureItem), typeDiscriminator: (int)BackupItemType.FunctionProcedure)]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "objType")]
public abstract class BackupItem : ScreamDbBaseEntity
{

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
    public abstract BackupItemType Type { get; set; }


    /// <summary>
    /// Configures the CliWrap ArgumentsBuilder with arguments specific to this item type
    /// </summary>
    public abstract void ConfigureArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize);

    /// <summary>
    /// Gets the output filename for this backup item
    /// </summary>
    public abstract string GetOutputFileName();
}