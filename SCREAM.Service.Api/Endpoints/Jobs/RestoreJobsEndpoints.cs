using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities.Restore;

namespace SCREAM.Service.Api.Endpoints.Jobs;

public static class RestoreJobEndpoints
{
    public static void MapRestoreJobEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/jobs/restore")
            .WithTags("Restore Jobs");

        // Get all restore jobs
        group.MapGet("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var restoreJobs = await dbContext.RestoreJobs
                .Include(job => job.RestorePlan)
                .OrderByDescending(job => job.StartedAt)
                .ToListAsync();
            return Results.Ok(restoreJobs);
        });

        // Get a restore job by id
        group.MapGet("/{jobId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory, long jobId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var restoreJob = await dbContext.RestoreJobs
                .Include(job => job.RestorePlan)
                .ThenInclude(plan => plan.DatabaseTarget)
                .Include(job => job.RestorePlan)
                .ThenInclude(plan => plan.SourceBackupPlan)
                .Include(job => job.RestoreItems)
                .ThenInclude(item => item.DatabaseItem)
                .FirstOrDefaultAsync(job => job.Id == jobId);

            return restoreJob == null ? Results.NotFound() : Results.Ok(restoreJob);
        });

        // Get logs for a restore job
        group.MapGet("/{jobId:long}/logs", async (IDbContextFactory<ScreamDbContext> dbContextFactory, long jobId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var logs = await dbContext.RestoreJobLogs
                .Where(log => log.RestoreJobId == jobId)
                .OrderByDescending(log => log.Timestamp)
                .ToListAsync();

            return Results.Ok(logs);
        });

        // Run a restore plan
        group.MapPost("/{restorePlanId:long}/run", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            long restorePlanId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var restorePlan = await dbContext.RestorePlans
                .Include(plan => plan.DatabaseTarget)
                .Include(plan => plan.SourceBackupPlan)
                .Include(plan => plan.Items)
                .ThenInclude(item => item.DatabaseItem)
                .FirstOrDefaultAsync(plan => plan.Id == restorePlanId);

            if (restorePlan == null)
                return Results.NotFound();

            // Create a new restore job
            var restoreJob = new RestoreJob
            {
                RestorePlanId = restorePlanId,
                RestorePlan = restorePlan,
                Status = TaskStatus.Created,
                StartedAt = DateTime.UtcNow,
                CompletedAt = null,
                IsCompressed = false,
                IsEncrypted = false,
                RestoreItems = new List<RestoreItem>()
            };

            // Add restore items from the plan - only the selected items
            foreach (var planItem in restorePlan.Items.Where(i => i.IsSelected))
            {
                restoreJob.RestoreItems.Add(new RestoreItem
                {
                    RestoreJobId = restoreJob.Id,
                    RestoreJob = restoreJob,
                    DatabaseItemId = planItem.DatabaseItemId,
                    DatabaseItem = planItem.DatabaseItem,
                    Status = TaskStatus.WaitingToRun,
                    RetryCount = 0,
                    StartedAt = null,
                    CompletedAt = null
                });
            }

            dbContext.RestoreJobs.Add(restoreJob);
            await dbContext.SaveChangesAsync();

            // Add initial log entry
            var logEntry = new RestoreJobLog
            {
                RestoreJobId = restoreJob.Id,
                Timestamp = DateTime.UtcNow,
                Title = "Job Created",
                Message = $"Restore job created for plan: {restorePlan.Name}",
                Severity = LogLevel.Information
            };

            dbContext.RestoreJobLogs.Add(logEntry);
            await dbContext.SaveChangesAsync();

            return Results.Created($"/jobs/restore/{restoreJob.Id}", restoreJob);
        });

        // Retry a failed restore job
        group.MapPost("/{jobId:long}/retry",
            async (IDbContextFactory<ScreamDbContext> dbContextFactory, long jobId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var restoreJob = await dbContext.RestoreJobs
                    .Include(job => job.RestoreItems)
                    .FirstOrDefaultAsync(job => job.Id == jobId);

                if (restoreJob == null)
                    return Results.NotFound();

                if (restoreJob.Status != TaskStatus.Faulted && restoreJob.Status != TaskStatus.Canceled)
                    return Results.BadRequest("Only failed or canceled jobs can be retried");

                // Reset job status
                restoreJob.Status = TaskStatus.WaitingToRun;
                restoreJob.CompletedAt = null;

                // Reset failed items
                foreach (var item in restoreJob.RestoreItems.Where(i => i.Status == TaskStatus.Faulted))
                {
                    item.Status = TaskStatus.WaitingToRun;
                    item.CompletedAt = null;
                    item.RetryCount += 1;
                }

                await dbContext.SaveChangesAsync();

                // Add log entry
                var logEntry = new RestoreJobLog
                {
                    RestoreJobId = restoreJob.Id,
                    Timestamp = DateTime.UtcNow,
                    Title = "Job Retry",
                    Message = "Restore job retry initiated",
                    Severity = LogLevel.Information
                };

                dbContext.RestoreJobLogs.Add(logEntry);
                await dbContext.SaveChangesAsync();

                return Results.Ok(restoreJob);
            });

        // Retry a failed restore item
        group.MapPost("/{jobId:long}/items/{itemId:long}/retry", async (
            IDbContextFactory<ScreamDbContext> dbContextFactory,
            long jobId, long itemId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var restoreJob = await dbContext.RestoreJobs
                .Include(j => j.RestoreItems)
                .ThenInclude(restoreItem => restoreItem.DatabaseItem)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (restoreJob == null)
                return Results.NotFound();

            var restoreItem = restoreJob.RestoreItems.FirstOrDefault(i => i.Id == itemId);

            if (restoreItem == null)
                return Results.NotFound();

            if (restoreItem.Status != TaskStatus.Faulted)
                return Results.BadRequest("Only failed items can be retried");

            // Reset item status
            restoreItem.Status = TaskStatus.WaitingToRun;
            restoreItem.CompletedAt = null;
            restoreItem.RetryCount += 1;
            restoreItem.ErrorMessage = null;

            // If job was completed with failure, reset it to running
            if (restoreItem.RestoreJob.Status == TaskStatus.Faulted)
            {
                restoreItem.RestoreJob.Status = TaskStatus.Running;
                restoreItem.RestoreJob.CompletedAt = null;
            }

            await dbContext.SaveChangesAsync();

            // Add log entry
            var logEntry = new RestoreJobLog
            {
                RestoreJobId = restoreItem.RestoreJobId,
                RestoreItemId = restoreItem.Id,
                Timestamp = DateTime.UtcNow,
                Title = "Item Retry",
                Message = $"Restore item retry initiated for {restoreItem.DatabaseItem.Name}",
                Severity = LogLevel.Information
            };

            dbContext.RestoreJobLogs.Add(logEntry);
            await dbContext.SaveChangesAsync();

            return Results.Ok(restoreItem);
        });
    }
}