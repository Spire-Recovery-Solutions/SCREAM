using SCREAM.Data.Enums;

namespace SCREAM.Data.Entities
{
    public class BackupSchedule : ScreamDbBaseEntity
    {
        public string CronExpression { get; set; } = string.Empty;

        public ScheduleType ScheduledType { get; set; }

        public DateTime? LastRun { get; set; }

        public DateTime? NextRun { get; set; }
    }
}
