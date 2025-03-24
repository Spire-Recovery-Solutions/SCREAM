using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using SCREAM.Business;
using SCREAM.Data;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Entities.Restore;
using SCREAM.Data.Entities.StorageTargets;
using SCREAM.Data.Enums;
using SCREAM.Service.Api.Validators;
using System.Text.Json;
using System.Text.Json.Serialization;
using SCREAM.Data.Entities.Backup.BackupItems;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddPooledDbContextFactory<ScreamDbContext>(o =>
{
    var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "ScreamDb.db");
    o.UseSqlite($"Data Source={dbPath}");
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();
app.UseCors("AllowBlazorClient");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


#region Targets

#region Storage

// Get a list of all storage targets
app.MapGet("/targets/storage", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var storageTargets = await dbContext.StorageTargets.ToListAsync();
    return Results.Ok(storageTargets);
});

// Get a storage target by id
app.MapGet("/targets/storage/{storageTargetId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
    long storageTargetId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var storageTarget = await dbContext.StorageTargets
        .FirstOrDefaultAsync(x => x.Id == storageTargetId);
    return storageTarget == null ? Results.NotFound() : Results.Ok(storageTarget);
});

// Combined endpoint for creating/editing and testing storage targets
app.MapPost("/targets/storage", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
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
app.MapDelete("/targets/storage/{storageTargetId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
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

bool ValidateStorageTarget(StorageTarget storageTarget)
{
    return StorageTargetValidator.Validate(storageTarget);
}

async Task<bool> TestStorageTarget(StorageTarget storageTarget)
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

#endregion

#region Connections

// Get a list of all connections
app.MapGet("/targets/connections", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var databaseConnections = await dbContext.DatabaseConnections.ToListAsync();
    return Results.Ok(databaseConnections);
});

// Get a connection by id
app.MapGet("/targets/connections/{databaseConnectionId:long}", async (HttpContext _,
    IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseConnectionId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var databaseConnection = await dbContext.DatabaseConnections
        .FirstOrDefaultAsync(x => x.Id == databaseConnectionId);
    return databaseConnection == null ? Results.NotFound() : Results.Ok(databaseConnection);
});

// Combined endpoint for creating/editing and testing database connections
app.MapPost("/targets/connections", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
    DatabaseConnection databaseConnection) =>
{
    // First test the database connection
    var isValid = ValidateDatabaseConnection(databaseConnection);
    if (!isValid)
    {
        return Results.BadRequest("Invalid database connection configuration.");
    }

    var testResult = await TestDatabaseConnection(databaseConnection);
    if (!testResult)
    {
        return Results.BadRequest("Database connection test failed.");
    }

    // If test succeeds, save the database connection
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();

    if (databaseConnection.Id == 0)
    {
        // Create new database connection
        dbContext.DatabaseConnections.Add(databaseConnection);
        await dbContext.SaveChangesAsync();
        return Results.Created($"/targets/connections/{databaseConnection.Id}", databaseConnection);
    }
    else
    {
        // Update existing database connection
        var existingConnection = await dbContext.DatabaseConnections
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
app.MapDelete("/targets/connections/{databaseConnectionId:long}", async (HttpContext _,
    IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseConnectionId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var databaseConnection = await dbContext.DatabaseConnections
        .FirstOrDefaultAsync(x => x.Id == databaseConnectionId);
    if (databaseConnection == null)
    {
        return Results.NotFound();
    }

    dbContext.DatabaseConnections.Remove(databaseConnection);
    await dbContext.SaveChangesAsync();

    return Results.NoContent();
});

// Scan a connections database
app.MapPost("/targets/connections/{databaseConnectionId:long}/scan", async (HttpContext _,
    IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseConnectionId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var databaseConnection = dbContext.DatabaseConnections.FirstOrDefault(c => c.Id == databaseConnectionId);
    if (databaseConnection == null)
    {
        return Results.NotFound();
    }

    var big = new BackupItemGenerator();
    var backupItems = await big.GetBackupItems(databaseConnection);

    return Results.Ok(backupItems);
});

bool ValidateDatabaseConnection(DatabaseConnection databaseConnection)
{
    return DatabaseConnectionValidator.Validate(databaseConnection);
}

async Task<bool> TestDatabaseConnection(DatabaseConnection databaseConnection)
{
    try
    {
        using var connection = new MySqlConnection(databaseConnection.ConnectionString);
        await connection.OpenAsync();
        return true;
    }
    catch (Exception)
    {
        return false;
    }
}

#endregion

#endregion

#region Backup Plans

// Get a list of all backup plans
app.MapGet("/plans/backup", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var backupPlans = await dbContext.BackupPlans
        .Include(i => i.DatabaseConnection)
        .Include(i => i.StorageTarget)
        .ToListAsync();
    return Results.Ok(backupPlans);
});
// Get a backup plan by id
app.MapGet("/plans/backup/{backupPlanId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
    long backupPlanId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var backupPlan = await dbContext.BackupPlans
        .Include(i => i.DatabaseConnection)
        .Include(i => i.StorageTarget)
        .Include(i => i.Items)
        .ThenInclude(item => item.DatabaseItem)
        .FirstOrDefaultAsync(x => x.Id == backupPlanId);
    return backupPlan == null ? Results.NotFound() : Results.Ok(backupPlan);
});
// Create or update a backup plan
app.MapPost("/plans/backup", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
    BackupPlan backupPlan) =>
{
    // // First test the database connection
    // var isValid = ValidateDatabaseConnection(backupPlan.DatabaseConnection);
    // if (!isValid)
    // {
    //     return Results.BadRequest("Invalid database connection configuration.");
    // }
    //
    // var testResult = await TestDatabaseConnection(backupPlan.DatabaseConnection);
    // if (!testResult)
    // {
    //     return Results.BadRequest("Database connection test failed.");
    // }

    // If test succeeds, save the backup plan
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
app.MapDelete("/plans/backup/{backupPlanId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
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

#endregion

#region Restore Plans

// Get a list of all restore plans
app.MapGet("/plans/restore", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var restorePlans = await dbContext.RestorePlans
        .Include(i => i.DatabaseConnection)
        .Include(i => i.SourceBackupPlan)
        .ToListAsync();
    return Results.Ok(restorePlans);
});

// Get a restore plan by id
app.MapGet("/plans/restore/{restorePlanId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
    long restorePlanId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var restorePlan = await dbContext.RestorePlans
        .Include(i => i.DatabaseConnection)
        .Include(i => i.SourceBackupPlan)
        .Include(i => i.Items)
        .ThenInclude(item => item.DatabaseItem)
        .FirstOrDefaultAsync(x => x.Id == restorePlanId);
    return restorePlan == null ? Results.NotFound() : Results.Ok(restorePlan);
});

// Create or update a restore plan
app.MapPost("/plans/restore", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
    RestorePlan restorePlan) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();

    if (restorePlan.Id == 0)
    {
        // Create new restore plan
        // Handle the many-to-many relationship with BackupItems
        var itemsToSave = restorePlan.Items.ToList();
        restorePlan.Items = new List<BackupItem>(); // Clear items before adding

        dbContext.RestorePlans.Add(restorePlan);
        await dbContext.SaveChangesAsync();

        // After saving, set up the relationships in the joining table
        foreach (var item in itemsToSave)
        {
            dbContext.Set<RestorePlanBackupItem>().Add(new RestorePlanBackupItem
            {
                RestorePlanId = restorePlan.Id,
                BackupItemId = item.Id
            });
        }

        await dbContext.SaveChangesAsync();
        return Results.Created($"/plans/restore/{restorePlan.Id}", restorePlan);
    }
    else
    {
        var existingPlan = await dbContext.RestorePlans
            .Include(rp => rp.Items)
            .FirstOrDefaultAsync(rp => rp.Id == restorePlan.Id);

        if (existingPlan == null)
            return Results.NotFound();

        // Update basic properties
        dbContext.Entry(existingPlan).CurrentValues.SetValues(restorePlan);

        // Handle the many-to-many relationship with BackupItems
        // First, get all current RestorePlanBackupItem entries
        var currentRelationships = await dbContext.Set<RestorePlanBackupItem>()
            .Where(rbi => rbi.RestorePlanId == restorePlan.Id)
            .ToListAsync();

        // Remove relationships not in the updated plan
        var updatedItemIds = restorePlan.Items.Select(i => i.Id).ToList();
        var relationshipsToRemove = currentRelationships
            .Where(r => !updatedItemIds.Contains(r.BackupItemId))
            .ToList();

        foreach (var relation in relationshipsToRemove)
        {
            dbContext.Set<RestorePlanBackupItem>().Remove(relation);
        }

        // Add new relationships
        var currentItemIds = currentRelationships.Select(r => r.BackupItemId).ToList();
        foreach (var item in restorePlan.Items)
        {
            if (!currentItemIds.Contains(item.Id))
            {
                dbContext.Set<RestorePlanBackupItem>().Add(new RestorePlanBackupItem
                {
                    RestorePlanId = restorePlan.Id,
                    BackupItemId = item.Id
                });
            }
        }

        await dbContext.SaveChangesAsync();

        // Reload the plan with updated relationships
        var updatedPlan = await dbContext.RestorePlans
            .Include(i => i.DatabaseConnection)
            .Include(i => i.SourceBackupPlan)
            .Include(i => i.Items)
            .ThenInclude(item => item.DatabaseItem)
            .FirstOrDefaultAsync(x => x.Id == restorePlan.Id);

        return Results.Ok(updatedPlan);
    }
});

// Delete a restore plan
app.MapDelete("/plans/restore/{restorePlanId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
    long restorePlanId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var restorePlan = await dbContext.RestorePlans
        .FirstOrDefaultAsync(x => x.Id == restorePlanId);

    if (restorePlan == null)
    {
        return Results.NotFound();
    }

    // The many-to-many relationships will be automatically removed due to 
    // the cascade delete configuration in the DbContext

    dbContext.RestorePlans.Remove(restorePlan);
    await dbContext.SaveChangesAsync();

    return Results.NoContent();
});

// Endpoints for restore jobs
app.MapGet("/jobs/restore", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var restoreJobs = await dbContext.RestoreJobs
        .Include(job => job.RestorePlan)
        .OrderByDescending(job => job.StartedAt)
        .ToListAsync();
    return Results.Ok(restoreJobs);
});

app.MapGet("/jobs/restore/{jobId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory, long jobId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var restoreJob = await dbContext.RestoreJobs
        .Include(job => job.RestorePlan)
        .ThenInclude(plan => plan.DatabaseConnection)
        .Include(job => job.RestorePlan)
        .ThenInclude(plan => plan.SourceBackupPlan)
        .Include(job => job.RestoreItems)
        .ThenInclude(item => item.DatabaseItem)
        .FirstOrDefaultAsync(job => job.Id == jobId);

    return restoreJob == null ? Results.NotFound() : Results.Ok(restoreJob);
});

app.MapGet("/jobs/restore/{jobId:long}/logs", async (IDbContextFactory<ScreamDbContext> dbContextFactory, long jobId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var logs = await dbContext.RestoreJobLogs
        .Where(log => log.RestoreJobId == jobId)
        .OrderByDescending(log => log.Timestamp)
        .ToListAsync();

    return Results.Ok(logs);
});

app.MapPost("/jobs/restore/{restorePlanId:long}/run", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
    long restorePlanId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var restorePlan = await dbContext.RestorePlans
        .Include(plan => plan.DatabaseConnection)
        .Include(plan => plan.SourceBackupPlan)
        .Include(plan => plan.Items)
        .ThenInclude(item => item.DatabaseItem)
        .FirstOrDefaultAsync(plan => plan.Id == restorePlanId);

    if (restorePlan == null)
        return Results.NotFound();

    // Create a new restore job
    var restoreJob = new RestoreJob
    {
        RestorePlanId = restorePlanId,
        RestorePlan = restorePlan,
        Status = TaskStatus.Created,
        StartedAt = DateTime.UtcNow,
        CompletedAt = null,
        IsCompressed = false,
        IsEncrypted = false,
        RestoreItems = new List<RestoreItem>()
    };

    // Add restore items from the plan - only the selected items
    foreach (var planItem in restorePlan.Items.Where(i => i.IsSelected))
    {
        restoreJob.RestoreItems.Add(new RestoreItem
        {
            RestoreJobId = restoreJob.Id,
            RestoreJob = restoreJob,
            DatabaseItemId = planItem.DatabaseItemId,
            DatabaseItem = planItem.DatabaseItem,
            Status = TaskStatus.WaitingToRun,
            RetryCount = 0,
            StartedAt = null,
            CompletedAt = null
        });
    }

    dbContext.RestoreJobs.Add(restoreJob);
    await dbContext.SaveChangesAsync();

    // Add initial log entry
    var logEntry = new RestoreJobLog
    {
        RestoreJobId = restoreJob.Id,
        Timestamp = DateTime.UtcNow,
        Title = "Job Created",
        Message = $"Restore job created for plan: {restorePlan.Name}",
        Severity = LogSeverity.Info
    };

    dbContext.RestoreJobLogs.Add(logEntry);
    await dbContext.SaveChangesAsync();

    // Here you would trigger the actual restore process, perhaps via a background service
    // Since that's outside the scope of this API endpoint implementation, we'll assume
    // some other part of the system will pick up the job and process it

    return Results.Created($"/jobs/restore/{restoreJob.Id}", restoreJob);
});

app.MapPost("/jobs/restore/{jobId:long}/retry",
    async (IDbContextFactory<ScreamDbContext> dbContextFactory, long jobId) =>
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var restoreJob = await dbContext.RestoreJobs
            .Include(job => job.RestoreItems)
            .FirstOrDefaultAsync(job => job.Id == jobId);

        if (restoreJob == null)
            return Results.NotFound();

        if (restoreJob.Status != TaskStatus.Faulted && restoreJob.Status != TaskStatus.Canceled)
            return Results.BadRequest("Only failed or canceled jobs can be retried");

        // Reset job status
        restoreJob.Status = TaskStatus.WaitingToRun;
        restoreJob.CompletedAt = null;

        // Reset failed items
        foreach (var item in restoreJob.RestoreItems.Where(i => i.Status == TaskStatus.Faulted))
        {
            item.Status = TaskStatus.WaitingToRun;
            item.CompletedAt = null;
            item.RetryCount += 1;
        }

        await dbContext.SaveChangesAsync();

        // Add log entry
        var logEntry = new RestoreJobLog
        {
            RestoreJobId = restoreJob.Id,
            Timestamp = DateTime.UtcNow,
            Title = "Job Retry",
            Message = "Restore job retry initiated",
            Severity = LogSeverity.Info
        };

        dbContext.RestoreJobLogs.Add(logEntry);
        await dbContext.SaveChangesAsync();

        return Results.Ok(restoreJob);
    });

app.MapPost("/jobs/restore/{jobId:long}/items/{itemId:long}/retry", async (
    IDbContextFactory<ScreamDbContext> dbContextFactory,
    long jobId, long itemId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var restoreJob = await dbContext.RestoreJobs
        .Include(j => j.RestoreItems)
        .ThenInclude(restoreItem => restoreItem.DatabaseItem)
        .FirstOrDefaultAsync(j => j.Id == jobId);

    if (restoreJob == null)
        return Results.NotFound();

    var restoreItem = restoreJob.RestoreItems.FirstOrDefault(i => i.Id == itemId);

    if (restoreItem == null)
        return Results.NotFound();

    if (restoreItem.Status != TaskStatus.Faulted)
        return Results.BadRequest("Only failed items can be retried");

    // Reset item status
    restoreItem.Status = TaskStatus.WaitingToRun;
    restoreItem.CompletedAt = null;
    restoreItem.RetryCount += 1;
    restoreItem.ErrorMessage = null;

    // If job was completed with failure, reset it to running
    if (restoreItem.RestoreJob.Status == TaskStatus.Faulted)
    {
        restoreItem.RestoreJob.Status = TaskStatus.Running;
        restoreItem.RestoreJob.CompletedAt = null;
    }

    await dbContext.SaveChangesAsync();

    // Add log entry
    var logEntry = new RestoreJobLog
    {
        RestoreJobId = restoreItem.RestoreJobId,
        RestoreItemId = restoreItem.Id,
        Timestamp = DateTime.UtcNow,
        Title = "Item Retry",
        Message = $"Restore item retry initiated for {restoreItem.DatabaseItem.Name}",
        Severity = LogSeverity.Info
    };

    dbContext.RestoreJobLogs.Add(logEntry);
    await dbContext.SaveChangesAsync();

    return Results.Ok(restoreItem);
});

// Get restore settings
app.MapGet("/settings/restore", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
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
app.MapPost("/settings/restore",
    async (IDbContextFactory<ScreamDbContext> dbContextFactory, RestoreSettings settings) =>
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

#endregion

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ScreamDbContext>>();
    var dbContext = dbContextFactory.CreateDbContext();
    dbContext.Database.EnsureDeleted();
    dbContext.Database.Migrate();
    dbContext.DatabaseConnections.Add(new DatabaseConnection
    {
        HostName = "localhost",
        Port = 3306,
        UserName = "root",
        Password = "Here!Lives@A#Happy4Little%Password^",
        Type = DatabaseType.MySQL
    });
    dbContext.SaveChanges();
}

app.Run();