using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities.Backup;

namespace SCREAM.Service.Api.Endpoints.Settings;

public static class BackupSettingsEndpoints
{
    public static void MapBackupSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/settings/backup")
            .WithTags("Backup Settings");

        // Get backup settings
        group.MapGet("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var settings = await dbContext.BackupSettings.FirstOrDefaultAsync();

            if (settings == null)
            {
                // Create default settings if none exist
                settings = new BackupSettings
                {
                    MaxAutoRetries = 3,
                    BackupHistoryRetentionDays = 30,
                    DefaultMaxAllowedPacket = "64M",
                    SendEmailNotifications = false
                };

                dbContext.BackupSettings.Add(settings);
                await dbContext.SaveChangesAsync();
            }

            return Results.Ok(settings);
        });

        // Update backup settings
        group.MapPost("/", async (IDbContextFactory<ScreamDbContext> dbContextFactory, BackupSettings settings) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var existingSettings = await dbContext.BackupSettings.FirstOrDefaultAsync();

            if (existingSettings == null)
            {
                dbContext.BackupSettings.Add(settings);
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