namespace SCREAM.Web.Models.Backup
{
    public class BackupConfiguration
    {
        public string DatabaseConnectionId { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public bool BackupSchema { get; set; } = true;
        public bool BackupData { get; set; } = true;
        public bool BackupFunctionSp { get; set; } = true;
        public bool BackupViews { get; set; } = true;
        public bool BackupTriggers { get; set; } = true;
        public bool BackupEvents { get; set; } = true;
        public int CompressionLevel { get; set; } = 3;
        public int ThreadCount { get; set; } = 4;
        public string StorageDestination { get; set; } = "s3";
        public string EncryptionKey { get; set; } = string.Empty;
        public string BackupFolder { get; set; } = string.Empty;
    }
}
