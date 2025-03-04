using SCREAM.Data.Entities.BackupItems;
using SCREAM.Data.Enums;

namespace SCREAM.Data.Entities
{
    public class BackupPlan : ScreamDbBaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public DatabaseConnection DatabaseConnection { get; set; }
        public List<BackupItem> BackupItems { get; set; } = new();
        public BackupStorageType StorageType { get; set; }
        public string StoragePath { get; set; }
        public bool UseEncryption { get; set; } = false;
        public string EncryptionKey { get; set; }
    }
}
