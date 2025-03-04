using SCREAM.Data.Entities.BackupItems;
using SCREAM.Data.Entities.StorageTargets;

namespace SCREAM.Data.Entities;

public class BackupPlan : ScreamDbBaseEntity
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required DatabaseConnection DatabaseConnection { get; set; }
    public required StorageTarget StorageTarget { get; set; }
    public required List<BackupItem> Items { get; set; }
    //public required BackupSchedule Schedule { get; set; }
}