using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities.Backup.BackupItems;
using SCREAM.Data.Entities.Restore;

namespace SCREAM.Service.Api.Endpoints.Plans;

public static class RestorePlanEndpoints
{
    public static void MapRestorePlanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/plans/restore")
            .WithTags("Plans/Restore");

        // Get a list of all restore plans
        group.MapGet("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var restorePlans = await dbContext.RestorePlans
                .Include(i => i.DatabaseTarget)
                .Include(i => i.SourceBackupPlan)
                .ToListAsync();
            return Results.Ok(restorePlans);
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