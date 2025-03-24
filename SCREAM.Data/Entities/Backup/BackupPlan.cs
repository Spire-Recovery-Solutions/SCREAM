using SCREAM.Data.Entities.Backup.BackupItems;
using SCREAM.Data.Entities.StorageTargets;
using SCREAM.Data.Enums;

namespace SCREAM.Data.Entities.Backup;

public class BackupPlan : ScreamDbBaseEntity
{
    public required string Name { get; set; }
    public required string Description { get; set; }

    // Navigation properties and foreign keys for related entities
    public long DatabaseTargetId { get; set; }
    public DatabaseTarget DatabaseTarget { get; set; } = null!;

    public long StorageTargetId { get; set; }
    public StorageTarget StorageTarget { get; set; } = null!;

    public bool IsActive { get; set; }

    // Related collections
    public ICollection<BackupJob> Jobs { get; set; }
    public ICollection<BackupItem> Items { get; set; }

    // Schedule properties
    public string ScheduleCron { get; set; } = string.Empty;
    public ScheduleType ScheduleType { get; set; }
    public DateTime? LastRun { get; set; }
    public DateTime? NextRun { get; set; }


    public DateTime? GetNextRun(DateTime utcNow)
    {
        switch (ScheduleType)
        {
            case ScheduleType.Repeating:
                {
                    var expression = Cronos.CronExpression.Parse(ScheduleCron);
                    var nextUtc = expression.GetNextOccurrence(DateTime.UtcNow);
                    return nextUtc;
                }
            case ScheduleType.OneTime when LastRun == null:
                return CreatedAt.AddMinutes(5);
            default:
                return null;
        }
    }
}