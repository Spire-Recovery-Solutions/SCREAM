using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities.Restore;

namespace SCREAM.Service.Api.Endpoints.Jobs;

public static class RestoreJobEndpoints
{
    public static void MapRestoreJobEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/jobs/restore")
            .WithTags("Jobs/Restore");

        // Get all restore jobs
        group.MapGet("/", async (
            IDbContextFactory<ScreamDbContext> dbContextFactory,
            [FromQuery] TaskStatus[]? statuses,
            bool? isCompressed,
            bool? isEncrypted
        ) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            IQueryable<RestoreJob> query = dbContext.RestoreJobs;
            if (statuses != null && statuses.Any())
            {
                query = query.Where(job => statuses.Contains(job.Status));
            }
            if (isCompressed.HasValue)
            {
                query = query.Where(job => job.IsCompressed == isCompressed.Value);
            }
            if (isEncrypted.HasValue)
            {
                query = query.Where(job => job.IsEncrypted == isEncrypted.Value);
            }
            var restoreJobs = await query.OrderByDescending(job => job.StartedAt).ToListAsync();
            return Results.Ok(restoreJobs);
        });

        // Get a restore job by id
        group.MapGet("/{jobId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory, long jobId) =>
       {
           await using var dbContext = await dbContextFactory.CreateDbContextAsync();
           var restoreJob = await dbContext.RestoreJobs
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

            var job = await dbContext.RestoreJobs
                .FirstOrDefaultAsync(j => j.Id == restoreItem.RestoreJobId);

            if (job != null && job.Status == TaskStatus.Faulted)
            {
                job.Status = TaskStatus.Running;
                job.CompletedAt = null;
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

        group.MapPut("/{jobId:long}", async (
            IDbContextFactory<ScreamDbContext> dbContextFactory,
            long jobId,
            RestoreJob updateJob) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var existingJob = await dbContext.RestoreJobs
                .FirstOrDefaultAsync(job => job.Id == jobId);

            if (existingJob == null)
                return Results.NotFound();

            var previousStatus = existingJob.Status;

            existingJob.Status = updateJob.Status;
            existingJob.IsCompressed = updateJob.IsCompressed;
            existingJob.IsEncrypted = updateJob.IsEncrypted;
            existingJob.RestorePlanId = updateJob.RestorePlanId;

            if (updateJob.Status == TaskStatus.RanToCompletion ||
                updateJob.Status == TaskStatus.Faulted ||
                updateJob.Status == TaskStatus.Canceled)
            {
                existingJob.CompletedAt = DateTime.UtcNow;
            }
            else if (previousStatus is TaskStatus.RanToCompletion or TaskStatus.Faulted or TaskStatus.Canceled &&
                     updateJob.Status is not TaskStatus.RanToCompletion and not TaskStatus.Faulted and not TaskStatus.Canceled)
            {
                existingJob.CompletedAt = null;
            }

            await dbContext.SaveChangesAsync();

            if (previousStatus != updateJob.Status)
            {
                var logEntry = new RestoreJobLog
                {
                    RestoreJobId = existingJob.Id,
                    Timestamp = DateTime.UtcNow,
                    Title = "Status Change",
                    Message = $"Job status changed from {previousStatus} to {updateJob.Status}",
                    Severity = LogLevel.Information
                };

                dbContext.RestoreJobLogs.Add(logEntry);
                await dbContext.SaveChangesAsync();
            }

            return Results.Ok(existingJob);
        });

        group.MapGet("/logs", async (
           IDbContextFactory<ScreamDbContext> dbContextFactory,
           long? restoreJobId = null,
           DateTime? dateFrom = null,
           DateTime? dateTo = null,
           LogLevel? severity = null,
           string? title = null) =>
       {
           await using var dbContext = await dbContextFactory.CreateDbContextAsync();

           IQueryable<RestoreJobLog> query = dbContext.RestoreJobLogs;

           if (restoreJobId.HasValue)
           {
               query = query.Where(log => log.RestoreJobId == restoreJobId.Value);
           }
           if (dateFrom.HasValue)
           {
               query = query.Where(log => log.Timestamp >= dateFrom.Value);
           }
           if (dateTo.HasValue)
           {
               query = query.Where(log => log.Timestamp <= dateTo.Value);
           }
           if (severity.HasValue)
           {
               query = query.Where(log => log.Severity == severity.Value);
           }
           if (!string.IsNullOrEmpty(title))
           {
               query = query.Where(log => log.Title.Contains(title));
           }

           var restoreLogs = await query
               .OrderByDescending(log => log.Timestamp)
               .ToListAsync();

           return Results.Ok(restoreLogs);
       });
    }
}