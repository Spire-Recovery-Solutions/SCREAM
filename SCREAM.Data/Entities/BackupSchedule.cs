using Cronos;
using SCREAM.Data.Enums;

namespace SCREAM.Data.Entities
{
    public class BackupSchedule : ScreamDbBaseEntity
    {   
        public long BackupPlanId { get; set; }
        public BackupPlan BackupPlan { get; set; } = null!;
        
        public string CronExpression { get; set; } = string.Empty;

        public ScheduleType ScheduledType { get; set; }

        public DateTime? LastRun { get; set; }

        public DateTime? NextRun { get; set; }

        public DateTime? GetNextRun(DateTime utcNow)
        {
            switch (ScheduledType)
            {
                case ScheduleType.Repeating:
                {
                    var expression = Cronos.CronExpression.Parse(CronExpression);
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
}