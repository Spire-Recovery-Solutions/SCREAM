using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.Restore;

namespace SCREAM.Service.Api.Endpoints.Items
{
    public static class RestoreItemEndpoints
    {
        public static void MapRestoreItemEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/jobs/restore/items")
                .WithTags("Jobs/RestoreItems");

            // GET: List all restore items for a specific restore job based on filters.
            group.MapGet("/{jobId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long jobId,
                [FromQuery] TaskStatus[]? statuses = null,
                [FromQuery] string? schema = null,
                [FromQuery] string? name = null,
                [FromQuery] DatabaseItemType? type = null,
                [FromQuery] int? maxRetryCount = null) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var restoreJob = await dbContext.RestoreJobs
                    .Include(rj => rj.RestoreItems)
                        .ThenInclude(ri => ri.DatabaseItem)
                    .FirstOrDefaultAsync(rj => rj.Id == jobId);

                if (restoreJob == null)
                    return Results.NotFound("Restore job not found");

                var filteredItems = restoreJob.RestoreItems.AsQueryable();

                if (statuses != null && statuses.Any())
                    filteredItems = filteredItems.Where(ri => statuses.Contains(ri.Status));

                if (!string.IsNullOrEmpty(schema))
                    filteredItems = filteredItems.Where(ri => ri.DatabaseItem.Schema.Contains(schema));

                if (!string.IsNullOrEmpty(name))
                    filteredItems = filteredItems.Where(ri => ri.DatabaseItem.Name.Contains(name));

                if (type.HasValue)
                    filteredItems = filteredItems.Where(ri => ri.DatabaseItem.Type == type.Value);

                if (maxRetryCount.HasValue)
                    filteredItems = filteredItems.Where(ri => ri.RetryCount <= maxRetryCount.Value);

                return Results.Ok(filteredItems.ToList());
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

         group.MapPost("/{jobId:long}/{itemId:long}/retry", async (
           IDbContextFactory<ScreamDbContext> dbContextFactory,
           long jobId,
           long itemId) =>
       {
           await using var dbContext = await dbContextFactory.CreateDbContextAsync();
           var restoreJob = await dbContext.RestoreJobs
               .Include(j => j.RestoreItems)
               .ThenInclude(restoreItem => restoreItem.DatabaseItem)
               .FirstOrDefaultAsync(j => j.Id == jobId);

           if (restoreJob == null)
               return Results.NotFound("Restore job not found");

           var restoreItem = restoreJob.RestoreItems.FirstOrDefault(i => i.Id == itemId);

           if (restoreItem == null)
               return Results.NotFound("Restore item not found");

           if (restoreItem.Status != TaskStatus.Faulted)
               return Results.BadRequest("Only failed items can be retried");

           // Reset item status.
           restoreItem.Status = TaskStatus.WaitingToRun;
           restoreItem.CompletedAt = null;
           restoreItem.RetryCount += 1;
           restoreItem.ErrorMessage = null;

           // If the parent job is faulted, set its status to Running.
           var job = await dbContext.RestoreJobs
               .FirstOrDefaultAsync(j => j.Id == restoreItem.RestoreJobId);
           if (job != null && job.Status == TaskStatus.Faulted)
           {
               job.Status = TaskStatus.Running;
               job.CompletedAt = null;
           }
           await dbContext.SaveChangesAsync();

           // Log the retry.
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
}
