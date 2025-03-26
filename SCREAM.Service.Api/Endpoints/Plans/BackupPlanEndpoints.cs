using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Enums;

namespace SCREAM.Service.Api.Endpoints.Plans;

public static class BackupPlanEndpoints
{
    public static void MapBackupPlanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/plans/backup")
            .WithTags("Plans/Backup");

        // Get a list of all backup plans
        group.MapGet("/", async (
            IDbContextFactory<ScreamDbContext> dbContextFactory,
            bool? isActive = null,
            ScheduleType? scheduleType = null,
            string? name = null,
            long? databaseTargetId = null,
            long? storageTargetId = null,
            bool? nextRunIsNull = null) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();

            IQueryable<BackupPlan> query = dbContext.BackupPlans
                .Include(bp => bp.Items)
                .Include(bp => bp.DatabaseTarget)
                .Include(bp => bp.StorageTarget);

            if (isActive.HasValue)
                query = query.Where(bp => bp.IsActive == isActive.Value);

            if (scheduleType.HasValue)
                query = query.Where(bp => bp.ScheduleType == scheduleType.Value);

            if (!string.IsNullOrEmpty(name))
                query = query.Where(bp => bp.Name.Contains(name));

            if (databaseTargetId.HasValue)
                query = query.Where(bp => bp.DatabaseTargetId == databaseTargetId.Value);

            if (storageTargetId.HasValue)
                query = query.Where(bp => bp.StorageTargetId == storageTargetId.Value);

            if (nextRunIsNull.HasValue && nextRunIsNull.Value)
                query = query.Where(bp => bp.NextRun == null);

            var backupPlans = await query.ToListAsync();
            return Results.Ok(backupPlans);
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

        // Get a backup plan by id
        group.MapGet("/{backupPlanId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            long backupPlanId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var backupPlan = await dbContext.BackupPlans
                .Include(i => i.DatabaseTarget)
                .Include(i => i.StorageTarget)
                .Include(i => i.Items)
                .ThenInclude(item => item.DatabaseItem)
                .FirstOrDefaultAsync(x => x.Id == backupPlanId);
            return backupPlan == null ? Results.NotFound() : Results.Ok(backupPlan);
        });
        
        // Create or update a backup plan
        group.MapPost("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            BackupPlan backupPlan) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();

            if (backupPlan.Id == 0)
            {
                // Create new backup plan
                dbContext.BackupPlans.Add(backupPlan);
                await dbContext.SaveChangesAsync();
                return Results.Created($"/plans/backup/{backupPlan.Id}", backupPlan);
            }
            else
            {
                var existingPlan = await dbContext.BackupPlans
                    .Include(bp => bp.Items)
                    .FirstOrDefaultAsync(bp => bp.Id == backupPlan.Id);

                if (existingPlan == null)
                    return Results.NotFound();

                dbContext.Entry(existingPlan).CurrentValues.SetValues(backupPlan);

                var existingItemsDict = existingPlan.Items.Where(i => i.Id != 0)
                    .ToDictionary(i => i.Id);
                var newItemsDict = backupPlan.Items.Where(i => i.Id != 0)
                    .ToDictionary(i => i.Id);

                foreach (var itemId in existingItemsDict.Keys.Except(newItemsDict.Keys))
                {
                    var backupItem = existingItemsDict[itemId];

                    var dependentStatuses = await dbContext.BackupItemStatuses
                        .Where(s => s.BackupItemId == backupItem.Id)
                        .ToListAsync();

                    foreach (var status in dependentStatuses)
                    {
                        dbContext.BackupItemStatuses.Remove(status);
                    }

                    dbContext.Remove(backupItem);
                }

                foreach (var newItem in backupPlan.Items)
                {
                    if (newItem.Id != 0 && existingItemsDict.TryGetValue(newItem.Id, out var existingItem))
                        dbContext.Entry(existingItem).CurrentValues.SetValues(newItem);
                    else
                        existingPlan.Items.Add(newItem);
                }

                await dbContext.SaveChangesAsync();
                return Results.Ok(existingPlan);
            }
        });

        // Delete a backup plan
        group.MapDelete("/{backupPlanId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            long backupPlanId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var backupPlan = await dbContext.BackupPlans
                .Include(p => p.Items)
                .FirstOrDefaultAsync(x => x.Id == backupPlanId);

            if (backupPlan == null)
            {
                return Results.NotFound();
            }

            dbContext.BackupPlans.Remove(backupPlan);
            await dbContext.SaveChangesAsync();

            return Results.NoContent();
        });
    }
}