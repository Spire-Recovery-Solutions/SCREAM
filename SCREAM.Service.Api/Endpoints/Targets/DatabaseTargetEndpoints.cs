using Microsoft.EntityFrameworkCore;
using SCREAM.Business;
using SCREAM.Data;
using SCREAM.Data.Entities;
using SCREAM.Service.Api.Validators;

namespace SCREAM.Service.Api.Endpoints.Targets;

public static class DatabaseTargetEndpoints
{
    public static void MapDatabaseTargetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/targets/database")
            .WithTags("Targets/Database");

        // Get a list of all connections
        group.MapGet("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var databaseTargets = await dbContext.DatabaseTargets.ToListAsync();
            return Results.Ok(databaseTargets);
        });

        // Get a connection by id
        group.MapGet("/{databaseTargetId:long}", async (HttpContext _,
            IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseTargetId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var databaseTarget = await dbContext.DatabaseTargets
                .FirstOrDefaultAsync(x => x.Id == databaseTargetId);
            return databaseTarget == null ? Results.NotFound() : Results.Ok(databaseTarget);
        });

        // Combined endpoint for creating/editing and testing database connections
        group.MapPost("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            DatabaseTarget databaseTarget) =>
        {
            // First test the database connection
            var isValid = DatabaseTargetValidator.Validate(databaseTarget);
            if (!isValid)
            {
                return Results.BadRequest("Invalid database connection configuration.");
            }

            var testResult = await DatabaseTargetValidator.TestDatabaseTarget(databaseTarget);
            if (!testResult)
            {
                return Results.BadRequest("Database connection test failed.");
            }

            // If test succeeds, save the database connection
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();

            if (databaseTarget.Id == 0)
            {
                // Create new database connection
                dbContext.DatabaseTargets.Add(databaseTarget);
                await dbContext.SaveChangesAsync();
                return Results.Created($"/targets/database/{databaseTarget.Id}", databaseTarget);
            }
            else
            {
                // Update existing database connection
                var existingConnection = await dbContext.DatabaseTargets
                    .FirstOrDefaultAsync(x => x.Id == databaseTarget.Id);

                if (existingConnection == null)
                {
                    return Results.NotFound();
                }

                // Update properties
                existingConnection.HostName = databaseTarget.HostName;
                existingConnection.Port = databaseTarget.Port;
                existingConnection.UserName = databaseTarget.UserName;
                existingConnection.Password = databaseTarget.Password;
                existingConnection.Type = databaseTarget.Type;

                await dbContext.SaveChangesAsync();

                return Results.Ok(databaseTarget);
            }
        });

        // Delete a connection
        group.MapDelete("/{databaseTargetId:long}", async (HttpContext _,
            IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseTargetId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var databaseTarget = await dbContext.DatabaseTargets
                .FirstOrDefaultAsync(x => x.Id == databaseTargetId);
            if (databaseTarget == null)
            {
                return Results.NotFound();
            }

            dbContext.DatabaseTargets.Remove(databaseTarget);
            await dbContext.SaveChangesAsync();

            return Results.NoContent();
        });

        // Scan a connections database
        group.MapPost("/{databaseTargetId:long}/scan", async (HttpContext _,
            IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseTargetId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var databaseTarget = dbContext.DatabaseTargets.FirstOrDefault(c => c.Id == databaseTargetId);
            if (databaseTarget == null)
            {
                return Results.NotFound();
            }

            var big = new BackupItemGenerator();
            var backupItems = await big.GetBackupItems(databaseTarget);

            return Results.Ok(backupItems);
        });
    }
}