using Microsoft.EntityFrameworkCore;
using SCREAM.Data.Entities.Backup;
using SCREAM.Data;

namespace SCREAM.Service.Api.Endpoints.BackupItemStatuses
{
    public static class BackupItemStatusEndpoints
    {
        public static void MapBackupItemStatusEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/jobs/backup/items/status")
                .WithTags("Jobs/BackupItemStatus");

            group.MapGet("/{jobId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long jobId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var statuses = await dbContext.BackupItemStatuses
                    .Include(s => s.BackupItem)
                    .ThenInclude(i => i.DatabaseItem)
                    .Where(s => s.BackupJobId == jobId)
                    .ToListAsync();
                return Results.Ok(statuses);
            });

            // GET: Get a specific backup item status
            group.MapGet("/{jobId:long}/{itemStatusId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long jobId,
                long itemStatusId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var status = await dbContext.BackupItemStatuses
                    .Include(s => s.BackupItem)
                    .ThenInclude(i => i.DatabaseItem)
                    .FirstOrDefaultAsync(s => s.Id == itemStatusId && s.BackupJobId == jobId);
                return status == null ? Results.NotFound() : Results.Ok(status);
            });

            // POST: Create or update a backup item status
            group.MapPost("/{jobId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long jobId,
                BackupItemStatus itemStatus) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();

                // Ensure the backup job exists
                var jobExists = await dbContext.BackupJobs.AnyAsync(j => j.Id == jobId);
                if (!jobExists)
                    return Results.NotFound("Backup job not found");

                // Validate the referenced backup item
                var backupItemExists = await dbContext.BackupItems.AnyAsync(i => i.Id == itemStatus.BackupItemId);
                if (!backupItemExists)
                    return Results.BadRequest("Invalid BackupItemId");

                if (itemStatus.Id == 0)
                {
                    // Create new backup item status
                    itemStatus.BackupJobId = jobId;
                    dbContext.BackupItemStatuses.Add(itemStatus);
                    await dbContext.SaveChangesAsync();
                    return Results.Created($"/jobs/backup/items/status/{jobId}/{itemStatus.Id}", itemStatus);
                }
                else
                {
                    // Update existing backup item status
                    var existingStatus = await dbContext.BackupItemStatuses
                        .FirstOrDefaultAsync(s => s.Id == itemStatus.Id && s.BackupJobId == jobId);

                    if (existingStatus == null)
                        return Results.NotFound();

                    // Update mutable properties
                    existingStatus.Status = itemStatus.Status;
                    existingStatus.RetryCount = itemStatus.RetryCount;
                    existingStatus.ErrorMessage = itemStatus.ErrorMessage;
                    existingStatus.StartedAt = itemStatus.StartedAt;
                    existingStatus.CompletedAt = itemStatus.CompletedAt;

                    await dbContext.SaveChangesAsync();
                    return Results.Ok(existingStatus);
                }
            });

            // DELETE: Delete a backup item status
            group.MapDelete("/{jobId:long}/{itemStatusId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long jobId,
                long itemStatusId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var status = await dbContext.BackupItemStatuses
                    .FirstOrDefaultAsync(s => s.Id == itemStatusId && s.BackupJobId == jobId);

                if (status == null)
                    return Results.NotFound();

                dbContext.BackupItemStatuses.Remove(status);
                await dbContext.SaveChangesAsync();
                return Results.NoContent();
            });
        }
    }
}
