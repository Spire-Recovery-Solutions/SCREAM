using SCREAM.Data.Entities.BackupItems;
using SCREAM.Data.Entities.StorageTargets;
using SCREAM.Data.Enums;

namespace SCREAM.Data.Entities;

public class BackupPlan : ScreamDbBaseEntity
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    
    public required long DatabaseConnectionId { get; set; }
    public required DatabaseConnection DatabaseConnection { get; set; }
    
    public required long StorageTargetId { get; set; }
    public required StorageTarget StorageTarget { get; set; }
    public bool IsActive { get; set; }

    public ICollection<BackupJob> Jobs { get; set; } = new List<BackupJob>();
    public string ScheduleCron { get; set; } = string.Empty;
    public ScheduleType ScheduleType { get; set; }

    public DateTime? LastRun { get; set; }

    public DateTime? NextRun { get; set; }
    
    
    public required List<BackupItem> Items { get; set; }


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