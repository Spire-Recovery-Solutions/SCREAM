using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities.Restore;

namespace SCREAM.Service.Api.Endpoints.Settings;

public static class RestoreSettingsEndpoints
{
    public static void MapRestoreSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/settings/restore")
            .WithTags("Restore Settings");

        // Get restore settings
        group.MapGet("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var settings = await dbContext.RestoreSettings.FirstOrDefaultAsync();

            if (settings == null)
            {
                // Create default settings if none exist
                settings = new RestoreSettings
                {
                    MaxAutoRetries = 3,
                    OverwriteExistingByDefault = false,
                    UseParallelExecution = true,
                    ImportTimeout = 3600,
                    SendEmailNotifications = false
                };

                dbContext.RestoreSettings.Add(settings);
                await dbContext.SaveChangesAsync();
            }

            return Results.Ok(settings);
        });

        // Update restore settings
        group.MapPost("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory, RestoreSettings settings) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var existingSettings = await dbContext.RestoreSettings.FirstOrDefaultAsync();

            if (existingSettings == null)
            {
                dbContext.RestoreSettings.Add(settings);
            }
            else
            {
                dbContext.Entry(existingSettings).CurrentValues.SetValues(settings);
            }

            await dbContext.SaveChangesAsync();
            return Results.Ok(settings);
        });
    }
}