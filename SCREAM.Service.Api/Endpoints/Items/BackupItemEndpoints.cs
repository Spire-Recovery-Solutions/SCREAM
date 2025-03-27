using Microsoft.EntityFrameworkCore;
using SCREAM.Data.Entities.Backup.BackupItems;
using SCREAM.Data;

namespace SCREAM.Service.Api.Endpoints.Items
{
    public static class BackupItemEndpoints
    {
        public static void MapBackupItemEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/plans/backup/items")
                .WithTags("Plans/BackupItems");

            // GET: Get all backup items for a specific backup plan
            group.MapGet("/{planId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long planId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var items = await dbContext.BackupItems
                    .Include(i => i.DatabaseItem)
                    .Where(i => i.BackupPlanId == planId)
                    .ToListAsync();
                return Results.Ok(items);
            });

            // GET: Get a specific backup item by id for a specific backup plan
            group.MapGet("/{planId:long}/{itemId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long planId,
                long itemId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var item = await dbContext.BackupItems
                    .Include(i => i.DatabaseItem)
                    .FirstOrDefaultAsync(i => i.Id == itemId && i.BackupPlanId == planId);
                return item == null ? Results.NotFound() : Results.Ok(item);
            });

            // POST: Create a new backup item for a specific backup plan
            group.MapPost("/{planId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long planId,
                BackupItem item) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                // Ensure the backup plan exists
                var planExists = await dbContext.BackupPlans.AnyAsync(p => p.Id == planId);
                if (!planExists)
                    return Results.NotFound("Backup plan not found");

                // Validate that the referenced DatabaseItem exists
                var databaseItemExists = await dbContext.DatabaseItems.AnyAsync(d => d.Id == item.DatabaseItemId);
                if (!databaseItemExists)
                    return Results.BadRequest("Invalid DatabaseItemId");

                item.BackupPlanId = planId;
                dbContext.BackupItems.Add(item);
                await dbContext.SaveChangesAsync();
                return Results.Created($"/plans/backup/items/{planId}/{item.Id}", item);
            });

            // PUT: Update an existing backup item for a specific backup plan
            group.MapPut("/{planId:long}/{itemId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long planId,
                long itemId,
                BackupItem updatedItem) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var existingItem = await dbContext.BackupItems
                    .FirstOrDefaultAsync(i => i.Id == itemId && i.BackupPlanId == planId);

                if (existingItem == null)
                    return Results.NotFound();

                if (existingItem.DatabaseItemId != updatedItem.DatabaseItemId)
                {
                    var databaseItemExists =
                        await dbContext.DatabaseItems.AnyAsync(d => d.Id == updatedItem.DatabaseItemId);
                    if (!databaseItemExists)
                        return Results.BadRequest("Invalid DatabaseItemId");
                }

                // Update properties
                existingItem.DatabaseItemId = updatedItem.DatabaseItemId;
                existingItem.IsSelected = updatedItem.IsSelected;

                await dbContext.SaveChangesAsync();
                return Results.Ok(existingItem);
            });

            // DELETE: Delete a backup item for a specific backup plan
            group.MapDelete("/{planId:long}/{itemId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long planId,
                long itemId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var item = await dbContext.BackupItems
                    .FirstOrDefaultAsync(i => i.Id == itemId && i.BackupPlanId == planId);
                if (item == null)
                    return Results.NotFound();

                dbContext.BackupItems.Remove(item);
                await dbContext.SaveChangesAsync();
                return Results.NoContent();
            });
        }
    }
}
