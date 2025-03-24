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
            .WithTags("Database Targets");

        // Get a list of all connections
        group.MapGet("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var databaseConnections = await dbContext.DatabaseTargets.ToListAsync();
            return Results.Ok(databaseConnections);
        });

        // Get a connection by id
        group.MapGet("/{databaseConnectionId:long}", async (HttpContext _,
            IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseConnectionId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var databaseConnection = await dbContext.DatabaseTargets
                .FirstOrDefaultAsync(x => x.Id == databaseConnectionId);
            return databaseConnection == null ? Results.NotFound() : Results.Ok(databaseConnection);
        });

        // Combined endpoint for creating/editing and testing database connections
        group.MapPost("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
            DatabaseTarget databaseConnection) =>
        {
            // First test the database connection
            var isValid = DatabaseConnectionValidator.Validate(databaseConnection);
            if (!isValid)
            {
                return Results.BadRequest("Invalid database connection configuration.");
            }

            var testResult = await DatabaseConnectionValidator.TestDatabaseConnection(databaseConnection);
            if (!testResult)
            {
                return Results.BadRequest("Database connection test failed.");
            }

            // If test succeeds, save the database connection
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();

            if (databaseConnection.Id == 0)
            {
                // Create new database connection
                dbContext.DatabaseTargets.Add(databaseConnection);
                await dbContext.SaveChangesAsync();
                return Results.Created($"/targets/database/{databaseConnection.Id}", databaseConnection);
            }
            else
            {
                // Update existing database connection
                var existingConnection = await dbContext.DatabaseTargets
                    .FirstOrDefaultAsync(x => x.Id == databaseConnection.Id);

                if (existingConnection == null)
                {
                    return Results.NotFound();
                }

                // Update properties
                existingConnection.HostName = databaseConnection.HostName;
                existingConnection.Port = databaseConnection.Port;
                existingConnection.UserName = databaseConnection.UserName;
                existingConnection.Password = databaseConnection.Password;
                existingConnection.Type = databaseConnection.Type;

                await dbContext.SaveChangesAsync();

                return Results.Ok(databaseConnection);
            }
        });

        // Delete a connection
        group.MapDelete("/{databaseConnectionId:long}", async (HttpContext _,
            IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseConnectionId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var databaseConnection = await dbContext.DatabaseTargets
                .FirstOrDefaultAsync(x => x.Id == databaseConnectionId);
            if (databaseConnection == null)
            {
                return Results.NotFound();
            }

            dbContext.DatabaseTargets.Remove(databaseConnection);
            await dbContext.SaveChangesAsync();

            return Results.NoContent();
        });

        // Scan a connections database
        group.MapPost("/{databaseConnectionId:long}/scan", async (HttpContext _,
            IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseConnectionId) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var databaseConnection = dbContext.DatabaseTargets.FirstOrDefault(c => c.Id == databaseConnectionId);
            if (databaseConnection == null)
            {
                return Results.NotFound();
            }

            var big = new BackupItemGenerator();
            var backupItems = await big.GetBackupItems(databaseConnection);

            return Results.Ok(backupItems);
        });
    }
}