using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities.Backup;

namespace SCREAM.Service.Api.Endpoints.Jobs;

public static class BackupJobEndpoints
{
    public static void MapBackupJobEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/jobs/backup")
            .WithTags("Jobs/Backup");

        // Get all backup jobs
        group.MapGet("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var backupJobs = await dbContext.BackupJobs
                .OrderByDescending(job => job.StartedAt)
                .ToListAsync();
            return Results.Ok(backupJobs);
        });

        // Get a backup job by id
        group.MapGet("/{jobId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory, long jobId) =>
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
         var backupJob = await dbContext.BackupJobs
        .Include(job => job.BackupItemStatuses)
        .ThenInclude(status => status.BackupItem)
        .ThenInclude(item => item.DatabaseItem)
        .FirstOrDefaultAsync(job => job.Id == jobId);

    return backupJob == null ? Results.NotFound() : Results.Ok(backupJob);
    });

        // Get logs for a backup job
        group.MapGet("/{jobId:long}/logs", async (IDbContextFactory<ScreamDbContext> dbContextFactory, long jobId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var logs = await dbContext.BackupJobLogs
                .Where(log => log.BackupJobId == jobId)
                .OrderByDescending(log => log.Timestamp)
                .ToListAsync();

            return Results.Ok(logs);
        });

        // Run a backup plan
        group.MapPost("/{backupPlanId:long}/run", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            long backupPlanId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var backupPlan = await dbContext.BackupPlans
                .Include(plan => plan.DatabaseTarget)
                .Include(plan => plan.StorageTarget)
                .Include(plan => plan.Items)
                .ThenInclude(item => item.DatabaseItem)
                .FirstOrDefaultAsync(plan => plan.Id == backupPlanId);

            if (backupPlan == null)
                return Results.NotFound();

            // Create a new backup job
            var backupJob = new BackupJob
            {
                BackupPlanId = backupPlanId,
                Status = TaskStatus.Created,
                StartedAt = DateTime.UtcNow,
                CompletedAt = null,
                BackupItemStatuses = new List<BackupItemStatus>()
            };

            // Add backup item statuses from the plan - only the selected items
            foreach (var planItem in backupPlan.Items.Where(i => i.IsSelected))
            {
                backupJob.BackupItemStatuses.Add(new BackupItemStatus
                {
                    BackupJobId = backupJob.Id,
                    BackupItemId = planItem.Id,
                    BackupItem = planItem,
                    Status = TaskStatus.WaitingToRun,
                    RetryCount = 0,
                    StartedAt = null,
                    CompletedAt = null
                });
            }

            dbContext.BackupJobs.Add(backupJob);
            await dbContext.SaveChangesAsync();

            // Add initial log entry
            var logEntry = new BackupJobLog
            {
                BackupJobId = backupJob.Id,
                Timestamp = DateTime.UtcNow,
                Title = "Job Created",
                Message = $"Backup job created for plan: {backupPlan.Name}",
                Severity = LogLevel.Information
            };

            dbContext.BackupJobLogs.Add(logEntry);
            await dbContext.SaveChangesAsync();

            return Results.Created($"/jobs/backup/{backupJob.Id}", backupJob);
        });

        // Retry a failed backup job
        group.MapPost("/{jobId:long}/retry",
            async (IDbContextFactory<ScreamDbContext> dbContextFactory, long jobId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var backupJob = await dbContext.BackupJobs
                    .Include(job => job.BackupItemStatuses)
                    .FirstOrDefaultAsync(job => job.Id == jobId);

                if (backupJob == null)
                    return Results.NotFound();

                if (backupJob.Status != TaskStatus.Faulted && backupJob.Status != TaskStatus.Canceled)
                    return Results.BadRequest("Only failed or canceled jobs can be retried");

                // Reset job status
                backupJob.Status = TaskStatus.WaitingToRun;
                backupJob.CompletedAt = null;

                // Reset failed items
                foreach (var item in backupJob.BackupItemStatuses.Where(i => i.Status == TaskStatus.Faulted))
                {
                    item.Status = TaskStatus.WaitingToRun;
                    item.CompletedAt = null;
                    item.RetryCount += 1;
                }

                await dbContext.SaveChangesAsync();

                // Add log entry
                var logEntry = new BackupJobLog
                {
                    BackupJobId = backupJob.Id,
                    Timestamp = DateTime.UtcNow,
                    Title = "Job Retry",
                    Message = "Backup job retry initiated",
                    Severity = LogLevel.Information
                };

                dbContext.BackupJobLogs.Add(logEntry);
                await dbContext.SaveChangesAsync();

                return Results.Ok(backupJob);
            });

        // Retry a failed backup item
        group.MapPost("/{jobId:long}/items/{itemId:long}/retry", async (
            IDbContextFactory<ScreamDbContext> dbContextFactory,
            long jobId, long itemId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var backupJob = await dbContext.BackupJobs
                .Include(j => j.BackupItemStatuses)
                .ThenInclude(status => status.BackupItem)
                .ThenInclude(item => item.DatabaseItem)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (backupJob == null)
                return Results.NotFound();

            var backupItemStatus = backupJob.BackupItemStatuses.FirstOrDefault(i => i.Id == itemId);

            if (backupItemStatus == null)
                return Results.NotFound();

            if (backupItemStatus.Status != TaskStatus.Faulted)
                return Results.BadRequest("Only failed items can be retried");

            // Reset item status
            backupItemStatus.Status = TaskStatus.WaitingToRun;
            backupItemStatus.CompletedAt = null;
            backupItemStatus.RetryCount += 1;
            backupItemStatus.ErrorMessage = null;

            // If job was completed with failure, reset it to running
            if (backupJob.Status == TaskStatus.Faulted)
            {
                backupJob.Status = TaskStatus.Running;
                backupJob.CompletedAt = null;
            }

            await dbContext.SaveChangesAsync();

            // Add log entry
            var logEntry = new BackupJobLog
            {
                BackupJobId = backupItemStatus.BackupJobId,
                BackupItemStatusId = backupItemStatus.Id,
                BackupItemStatus = backupItemStatus,
                Timestamp = DateTime.UtcNow,
                Title = "Item Retry",
                Message = $"Backup item retry initiated for {backupItemStatus.BackupItem.DatabaseItem.Name}",
                Severity = LogLevel.Information
            };

            dbContext.BackupJobLogs.Add(logEntry);
            await dbContext.SaveChangesAsync();

            return Results.Ok(backupItemStatus);
        });
    }
}