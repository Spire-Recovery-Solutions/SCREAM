using CliWrap.Builders;
using SCREAM.Data.Entities.Backup.BackupItems;
using System.Text.Json.Serialization;

namespace SCREAM.Data.Entities.Database.DatabaseItems
{
    [JsonDerivedType(typeof(DatabaseTableStructureItems), typeDiscriminator: (int)DatabaseItemType.TableStructure)]
    [JsonDerivedType(typeof(DatabaseTableDataItems), typeDiscriminator: (int)DatabaseItemType.TableData)]
    [JsonDerivedType(typeof(DatabaseViewItems), typeDiscriminator: (int)DatabaseItemType.View)]
    [JsonDerivedType(typeof(DatabaseTriggerItems), typeDiscriminator: (int)DatabaseItemType.Trigger)]
    [JsonDerivedType(typeof(DatabaseEventItems), typeDiscriminator: (int)DatabaseItemType.Event)]
    [JsonDerivedType(typeof(DatabaseFunctionProcedureItems), typeDiscriminator: (int)DatabaseItemType.FunctionProcedure)]
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "objType")]
    public abstract class DatabaseItem : ScreamDbBaseEntity
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public abstract DatabaseItemType Type { get; set; }

        public abstract void ConfigureBackupArguments(ArgumentsBuilder args,
            string host, string user, string password, string maxPacketSize);

        public abstract void ConfigureRestoreArguments(ArgumentsBuilder args,
            string host, string user, string password);

        public abstract string GetBackupFileName();
    }
}
