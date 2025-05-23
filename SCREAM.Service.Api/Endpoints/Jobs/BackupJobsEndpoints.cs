using Microsoft.AspNetCore.Mvc;
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

     group.MapGet("/",
    async (
        IDbContextFactory<ScreamDbContext> dbContextFactory,
        [FromQuery] long? planId = null,
        [FromQuery] TaskStatus[]? statuses = null,
        [FromQuery] bool? hasTriggeredRestore = null
    ) =>
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        IQueryable<BackupJob> query = db.BackupJobs;

        // Filter by planId if provided
        if (planId.HasValue)
            query = query.Where(job => job.BackupPlanId == planId.Value);

        // Filter by any statuses if provided
        if (statuses != null && statuses.Any())
            query = query.Where(job => statuses.Contains(job.Status));

        // Filter by HasTriggeredRestore if provided
        if (hasTriggeredRestore.HasValue)
            query = query.Where(job => job.HasTriggeredRestore == hasTriggeredRestore.Value);

        var backupJobs = await query
            .OrderByDescending(job => job.StartedAt)
            .ToListAsync();

        return Results.Ok(backupJobs);
    }
);


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

        group.MapPut("/{jobId:long}", async (
            IDbContextFactory<ScreamDbContext> dbContextFactory,
            long jobId,
            BackupJob updatedJob) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();

            var existingJob = await dbContext.BackupJobs
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (existingJob == null)
                return Results.NotFound();

            existingJob.Status = updatedJob.Status;
            existingJob.CompletedAt = updatedJob.CompletedAt;
            existingJob.HasTriggeredRestore = updatedJob.HasTriggeredRestore;

            await dbContext.SaveChangesAsync();

            return Results.Ok(existingJob);
        });

        group.MapGet("/logs", async (
            IDbContextFactory<ScreamDbContext> dbContextFactory,
            long? backupJobId = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            LogLevel? severity = null,
            string? title = null) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            IQueryable<BackupJobLog> query = dbContext.BackupJobLogs;

            if (backupJobId.HasValue)
            {
                query = query.Where(log => log.BackupJobId == backupJobId.Value);
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

            var backupLogs = await query.ToListAsync();
            return Results.Ok(backupLogs);
        });
    }
}