using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities.Backup;

namespace SCREAM.Service.Api.Endpoints.Plans;

public static class BackupPlanEndpoints
{
    public static void MapBackupPlanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/plans/backup")
            .WithTags("Backup Plans");

        // Get a list of all backup plans
        group.MapGet("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var backupPlans = await dbContext.BackupPlans
                .Include(i => i.DatabaseTarget)
                .Include(i => i.StorageTarget)
                .ToListAsync();
            return Results.Ok(backupPlans);
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
                    dbContext.Remove(existingItemsDict[itemId]);

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