using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities.StorageTargets;
using SCREAM.Service.Api.Validators;

namespace SCREAM.Service.Api.Endpoints.Targets;

public static class StorageTargetEndpoints
{
    public static void MapStorageTargetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/targets/storage")
            .WithTags("Storage Targets");

        // Get a list of all storage targets
        group.MapGet("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var storageTargets = await dbContext.StorageTargets.ToListAsync();
            return Results.Ok(storageTargets);
        });

        // Get a storage target by id
        group.MapGet("/{storageTargetId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            long storageTargetId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var storageTarget = await dbContext.StorageTargets
                .FirstOrDefaultAsync(x => x.Id == storageTargetId);
            return storageTarget == null ? Results.NotFound() : Results.Ok(storageTarget);
        });

        // Combined endpoint for creating/editing and testing storage targets
        group.MapPost("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            StorageTarget storageTarget) =>
        {
            // First test the storage target
            var isValid = ValidateStorageTarget(storageTarget);
            if (!isValid)
            {
                return Results.BadRequest("Invalid storage target configuration.");
            }

            var testResult = await TestStorageTarget(storageTarget);
            if (!testResult)
            {
                return Results.BadRequest("Storage target test failed.");
            }

            // If test succeeds, save the storage target
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();

            if (storageTarget.Id == 0)
            {
                // Create new storage target
                dbContext.StorageTargets.Add(storageTarget);
                await dbContext.SaveChangesAsync();
                return Results.Created($"/targets/storage/{storageTarget.Id}", storageTarget);
            }
            else
            {
                // Update existing storage target
                var existingStorageTarget = await dbContext.StorageTargets
                    .FirstOrDefaultAsync(x => x.Id == storageTarget.Id);

                if (existingStorageTarget == null)
                {
                    return Results.NotFound();
                }

                // Use EF Update function 
                dbContext.Entry(existingStorageTarget).CurrentValues.SetValues(storageTarget);
                await dbContext.SaveChangesAsync();

                return Results.Ok(storageTarget);
            }
        });

        // Delete a storage target
        group.MapDelete("/{storageTargetId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            long storageTargetId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var storageTarget = await dbContext.StorageTargets
                .FirstOrDefaultAsync(x => x.Id == storageTargetId);
            if (storageTarget == null)
            {
                return Results.NotFound();
            }

            dbContext.StorageTargets.Remove(storageTarget);
            await dbContext.SaveChangesAsync();

            return Results.NoContent();
        });
    }

    private static bool ValidateStorageTarget(StorageTarget storageTarget)
    {
        return StorageTargetValidator.Validate(storageTarget);
    }

    private static async Task<bool> TestStorageTarget(StorageTarget storageTarget)
    {
        // Add testing logic for the storage target
        switch (storageTarget)
        {
            case LocalStorageTarget localStorageTarget:
                return Directory.Exists(localStorageTarget.Path);
            case S3StorageTarget s3StorageTarget:
                // Simulate S3 storage target testing
                await Task.Delay(1000);
                return true;
            default:
                return false;
        }
    }
}