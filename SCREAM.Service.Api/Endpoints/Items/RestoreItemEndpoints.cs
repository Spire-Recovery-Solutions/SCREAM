using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities.Restore;

namespace SCREAM.Service.Api.Endpoints.Items
{
    public static class RestoreItemEndpoints
    {
        public static void MapRestoreItemEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/jobs/restore/items")
                .WithTags("Jobs/RestoreItems");

            // GET: List all restore items for a specific restore job
            group.MapGet("/{jobId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long jobId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var restoreJob = await dbContext.RestoreJobs
                    .Include(rj => rj.RestoreItems)
                        .ThenInclude(ri => ri.DatabaseItem)
                    .FirstOrDefaultAsync(rj => rj.Id == jobId);

                if (restoreJob == null)
                    return Results.NotFound("Restore job not found");

                return Results.Ok(restoreJob.RestoreItems);
            });

            // GET: Get a specific restore item for a specific restore job
            group.MapGet("/{jobId:long}/{itemId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long jobId,
                long itemId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var restoreJob = await dbContext.RestoreJobs
                    .Include(rj => rj.RestoreItems)
                        .ThenInclude(ri => ri.DatabaseItem)
                    .FirstOrDefaultAsync(rj => rj.Id == jobId);
                if (restoreJob == null)
                    return Results.NotFound("Restore job not found");

                var item = restoreJob.RestoreItems.FirstOrDefault(ri => ri.Id == itemId);
                return item == null ? Results.NotFound("Restore item not found") : Results.Ok(item);
            });

            // POST: Upsert (create or update) a restore item for a specific restore job
            group.MapPost("/{jobId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long jobId,
                RestoreItem item) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                // Retrieve the restore job along with its items
                var restoreJob = await dbContext.RestoreJobs
                    .Include(rj => rj.RestoreItems)
                    .FirstOrDefaultAsync(rj => rj.Id == jobId);
                if (restoreJob == null)
                    return Results.NotFound("Restore job not found");

                // Validate that the referenced DatabaseItem exists
                var databaseItemExists = await dbContext.DatabaseItems.AnyAsync(d => d.Id == item.DatabaseItemId);
                if (!databaseItemExists)
                    return Results.BadRequest("Invalid DatabaseItemId");

                if (item.Id == 0)
                {
                    // Create new restore item
                    item.RestoreJobId = jobId;
                    restoreJob.RestoreItems.Add(item);
                    await dbContext.SaveChangesAsync();
                    return Results.Created($"/jobs/restore/items/{jobId}/{item.Id}", item);
                }
                else
                {
                    // Update existing restore item
                    var existingItem = restoreJob.RestoreItems.FirstOrDefault(ri => ri.Id == item.Id);
                    if (existingItem == null)
                        return Results.NotFound("Restore item not found");

                    // If DatabaseItemId changes, validate the new one exists
                    if (existingItem.DatabaseItemId != item.DatabaseItemId)
                    {
                        var newDatabaseItemExists = await dbContext.DatabaseItems.AnyAsync(d => d.Id == item.DatabaseItemId);
                        if (!newDatabaseItemExists)
                            return Results.BadRequest("Invalid DatabaseItemId");
                    }

                    // Update properties
                    existingItem.DatabaseItemId = item.DatabaseItemId;
                    existingItem.Status = item.Status;
                    existingItem.RetryCount = item.RetryCount;
                    existingItem.ErrorMessage = item.ErrorMessage;
                    existingItem.StartedAt = item.StartedAt;
                    existingItem.CompletedAt = item.CompletedAt;

                    await dbContext.SaveChangesAsync();
                    return Results.Ok(existingItem);
                }
            });

            // DELETE: Delete a restore item for a specific restore job
            group.MapDelete("/{jobId:long}/{itemId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long jobId,
                long itemId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var restoreJob = await dbContext.RestoreJobs
                    .Include(rj => rj.RestoreItems)
                    .FirstOrDefaultAsync(rj => rj.Id == jobId);
                if (restoreJob == null)
                    return Results.NotFound("Restore job not found");

                var item = restoreJob.RestoreItems.FirstOrDefault(ri => ri.Id == itemId);
                if (item == null)
                    return Results.NotFound("Restore item not found");

                restoreJob.RestoreItems.Remove(item);
                await dbContext.SaveChangesAsync();
                return Results.NoContent();
            });
        }
    }
}
