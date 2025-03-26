using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities.Backup.BackupItems;
using SCREAM.Data.Entities.Restore;
using SCREAM.Data.Enums;

namespace SCREAM.Service.Api.Endpoints.Plans;

public static class RestorePlanEndpoints
{
    public static void MapRestorePlanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/plans/restore")
            .WithTags("Plans/Restore");

        // Get a list of all restore plans
        group.MapGet("/", async (
            IDbContextFactory<ScreamDbContext> dbContextFactory,
            bool? isActive = null,
            ScheduleType? scheduleType = null,
            string? name = null,
            long? databaseTargetId = null,
            long? sourceBackupPlanId = null,
            bool? nextRunIsNull = null) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();

            IQueryable<RestorePlan> query = dbContext.RestorePlans
                .Include(i => i.Items)
                .Include(i => i.DatabaseTarget)
                .Include(i => i.SourceBackupPlan);

            if (isActive.HasValue)
                query = query.Where(rp => rp.IsActive == isActive.Value);

            if (scheduleType.HasValue)
                query = query.Where(rp => rp.ScheduleType == scheduleType.Value);

            if (!string.IsNullOrEmpty(name))
                query = query.Where(rp => rp.Name.Contains(name));

            if (databaseTargetId.HasValue)
                query = query.Where(rp => rp.DatabaseTargetId == databaseTargetId.Value);

            if (sourceBackupPlanId.HasValue)
                query = query.Where(rp => rp.SourceBackupPlanId == sourceBackupPlanId.Value);

            if (nextRunIsNull.HasValue && nextRunIsNull.Value)
                query = query.Where(rp => rp.NextRun == null);

            var restorePlans = await query.ToListAsync();
            return Results.Ok(restorePlans);
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

        // Get a restore plan by id
        group.MapGet("/{restorePlanId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            long restorePlanId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var restorePlan = await dbContext.RestorePlans
                .Include(i => i.DatabaseTarget)
                .Include(i => i.SourceBackupPlan)
                .Include(i => i.Items)
                .ThenInclude(item => item.DatabaseItem)
                .FirstOrDefaultAsync(x => x.Id == restorePlanId);
            return restorePlan == null ? Results.NotFound() : Results.Ok(restorePlan);
        });

        // Create or update a restore plan
        group.MapPost("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            RestorePlan restorePlan) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();

            if (restorePlan.Id == 0)
            {
                // Create new restore plan
                // Handle the many-to-many relationship with BackupItems
                var itemsToSave = restorePlan.Items.ToList();
                restorePlan.Items = new List<BackupItem>(); // Clear items before adding

                dbContext.RestorePlans.Add(restorePlan);
                await dbContext.SaveChangesAsync();

                // After saving, set up the relationships in the joining table
                foreach (var item in itemsToSave)
                {
                    dbContext.Set<RestorePlanBackupItem>().Add(new RestorePlanBackupItem
                    {
                        RestorePlanId = restorePlan.Id,
                        BackupItemId = item.Id
                    });
                }

                await dbContext.SaveChangesAsync();
                return Results.Created($"/plans/restore/{restorePlan.Id}", restorePlan);
            }
            else
            {
                var existingPlan = await dbContext.RestorePlans
                    .Include(rp => rp.Items)
                    .FirstOrDefaultAsync(rp => rp.Id == restorePlan.Id);

                if (existingPlan == null)
                    return Results.NotFound();

                // Update basic properties
                dbContext.Entry(existingPlan).CurrentValues.SetValues(restorePlan);

                // Handle the many-to-many relationship with BackupItems
                // First, get all current RestorePlanBackupItem entries
                var currentRelationships = await dbContext.Set<RestorePlanBackupItem>()
                    .Where(rbi => rbi.RestorePlanId == restorePlan.Id)
                    .ToListAsync();

                // Remove relationships not in the updated plan
                var updatedItemIds = restorePlan.Items.Select(i => i.Id).ToList();
                var relationshipsToRemove = currentRelationships
                    .Where(r => !updatedItemIds.Contains(r.BackupItemId))
                    .ToList();

                foreach (var relation in relationshipsToRemove)
                {
                    dbContext.Set<RestorePlanBackupItem>().Remove(relation);
                }

                // Add new relationships
                var currentItemIds = currentRelationships.Select(r => r.BackupItemId).ToList();
                foreach (var item in restorePlan.Items)
                {
                    if (!currentItemIds.Contains(item.Id))
                    {
                        dbContext.Set<RestorePlanBackupItem>().Add(new RestorePlanBackupItem
                        {
                            RestorePlanId = restorePlan.Id,
                            BackupItemId = item.Id
                        });
                    }
                }

                await dbContext.SaveChangesAsync();

                // Reload the plan with updated relationships
                var updatedPlan = await dbContext.RestorePlans
                    .Include(i => i.DatabaseTarget)
                    .Include(i => i.SourceBackupPlan)
                    .Include(i => i.Items)
                    .ThenInclude(item => item.DatabaseItem)
                    .FirstOrDefaultAsync(x => x.Id == restorePlan.Id);

                return Results.Ok(updatedPlan);
            }
        });

        // Delete a restore plan
        group.MapDelete("/{restorePlanId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            long restorePlanId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var restorePlan = await dbContext.RestorePlans
                .FirstOrDefaultAsync(x => x.Id == restorePlanId);

            if (restorePlan == null)
            {
                return Results.NotFound();
            }

            // The many-to-many relationships will be automatically removed due to 
            // the cascade delete configuration in the DbContext

            dbContext.RestorePlans.Remove(restorePlan);
            await dbContext.SaveChangesAsync();

            return Results.NoContent();
        });
    }
}