using Cronos;
using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Entities.Backup.BackupItems;
using SCREAM.Data.Enums;

namespace SCREAM.Data.Entities.Restore
{
    /// <summary>
    /// Represents a plan for restoring data from a completed backup job.
    /// </summary>
    public class RestorePlan : ScreamDbBaseEntity
    {
        public required string Name { get; set; }
        public required string Description { get; set; }

        // Navigation properties and foreign keys for related entities
        public long DatabaseTargetId { get; set; }
        public DatabaseTarget DatabaseTarget { get; set; } = null!;

        public long SourceBackupPlanId { get; set; }
        public BackupPlan SourceBackupPlan { get; set; } = null!;

        public string ScheduleCron { get; set; } = string.Empty;
        public ScheduleType ScheduleType { get; set; }
        public DateTime? LastRun { get; set; }
        public DateTime? NextRun { get; set; }

        public bool IsActive { get; set; }
        public bool OverwriteExisting { get; set; }

        public DateTime? GetNextRun(DateTime utcNow)
        {
            switch (ScheduleType)
            {
                case ScheduleType.Repeating:
                    {
                        var expression = CronExpression.Parse(ScheduleCron);
                        return expression.GetNextOccurrence(utcNow);
                    }
                case ScheduleType.OneTime when LastRun == null:
                    return CreatedAt.AddMinutes(5);
                case ScheduleType.Triggered:
                    return null;
                default:
                    return null;
            }
        }

        // Related collections
        public ICollection<RestoreJob> Jobs { get; set; } = new List<RestoreJob>();
        public ICollection<BackupItem> Items { get; set; } = new List<BackupItem>();
    }


    public class RestorePlanBackupItem
    {
        public long RestorePlanId { get; set; }
        public RestorePlan RestorePlan { get; set; } = null!;

        public long BackupItemId { get; set; }
        public BackupItem BackupItem { get; set; } = null!;
    }
}