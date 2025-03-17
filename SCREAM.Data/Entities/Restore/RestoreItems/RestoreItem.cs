using CliWrap.Builders;
using SCREAM.Data.Entities.Backup.BackupItems;
using System.Text.Json.Serialization;

namespace SCREAM.Data.Entities.Restore.RestoreItems
{
    /// <summary>
    /// Represents a database object that can be restored
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "objType")]
    [JsonDerivedType(typeof(TableStructureItem), typeDiscriminator: (int)DatabaseItemType.TableStructure)]
    [JsonDerivedType(typeof(TableDataItem), typeDiscriminator: (int)DatabaseItemType.TableData)]
    [JsonDerivedType(typeof(ViewItem), typeDiscriminator: (int)DatabaseItemType.View)]
    [JsonDerivedType(typeof(TriggerItem), typeDiscriminator: (int)DatabaseItemType.Trigger)]
    [JsonDerivedType(typeof(EventItem), typeDiscriminator: (int)DatabaseItemType.Event)]
    [JsonDerivedType(typeof(FunctionProcedureItem), typeDiscriminator: (int)DatabaseItemType.FunctionProcedure)]
    public abstract class RestoreItem : ScreamDbBaseEntity
    {
        /// <summary>
        /// The ID of the restore plan this item belongs to.
        /// </summary>
        public long? RestorePlanId { get; set; }

        /// <summary>
        /// The corresponding backup item this restore item is based on
        /// </summary>
        public long BackupItemId { get; set; }
        public BackupItem BackupItem { get; set; } = null!;

        /// <summary>
        /// The schema (database) the item belongs to.
        /// </summary>
        public string Schema { get; set; } = string.Empty;

        /// <summary>
        /// The name of the item (e.g., table name, view name). May be empty for schema-level objects.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Indicates whether the item is selected for restore.
        /// </summary>
        public bool IsSelected { get; set; } = true;

        /// <summary>
        /// The type of restore item.
        /// </summary>
        public abstract DatabaseItemType Type { get; set; }

        /// <summary>
        /// Configures the CliWrap ArgumentsBuilder with arguments specific to this item type
        /// </summary>
        public abstract void ConfigureArguments(ArgumentsBuilder args, string host, string user, string password);
    }

}
