using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities;

namespace SCREAM.Service.Backup;

public class Worker(ILogger<Worker> logger, IDbContextFactory<ScreamDbContext> dbContextFactory) : BackgroundService
{
    //TODO: This needs to communicate with the API locally to handle all database operations

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            //TODO: 
            // 1. Check for BackupPlans that need BackupJobs created
            await GenerateBackupJobs();
            // 2. Check for BackupJobs that need to be executed
            // 3. Check for BackupJobs that need to be retried
            // 4. Check for BackupJobs that need to be cancelled

            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task GenerateBackupJobs()
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        // Get all active BackupPlans that have a schedule with NextRun == null
        var backupPlans = await dbContext.BackupPlans
            .Include(i => i.Jobs)
            .Where(w => w.IsActive && w.NextRun == null)
            .ToListAsync();

        foreach (var backupPlan in backupPlans)
        {
            if (backupPlan.Jobs.Any(a => a.Status is >= TaskStatus.Created and < TaskStatus.RanToCompletion))
            {
                // If there are any jobs that are already created or running, skip this backup plan
                continue;
            }
            //TODO: Check for retries on the Faulted jobs
            // var faultedJobs = backupPlan.Jobs.Where(w => w.Status == TaskStatus.Faulted).ToList();
            // if (faultedJobs.Any())
            // {
            //     foreach (var faultedJob in faultedJobs)
            //     {
            //         // Check if the job can be retried
            //         if (faultedJob.RetryCount < 3)
            //         {
            //             faultedJob.RetryCount++;
            //             dbContext.BackupJobs.Update(faultedJob);
            //         }
            //         else
            //         {
            //             // If the job has reached the max retry count, mark it as cancelled
            //             faultedJob.Status = TaskStatus.Canceled;
            //             dbContext.BackupJobs.Update(faultedJob);
            //         }
            //     }
            // }

            // Calculate NextRun 
            var nextRun = backupPlan.GetNextRun(DateTime.UtcNow);
            if (nextRun != null)
            {
                backupPlan.NextRun = nextRun;
                dbContext.BackupPlans.Update(backupPlan);
                await dbContext.SaveChangesAsync();
            }

            // Create BackupJob
            var backupJob = new BackupJob
            {
                BackupPlan = backupPlan,
                Status = TaskStatus.Created,
                StartedAt = default,
                CompletedAt = null
            };
            dbContext.BackupJobs.Add(backupJob);
            await dbContext.SaveChangesAsync();
        }
    }
}