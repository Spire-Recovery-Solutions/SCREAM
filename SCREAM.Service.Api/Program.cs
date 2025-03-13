using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SCREAM.Business;
using SCREAM.Data;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.StorageTargets;
using SCREAM.Data.Enums;
using SCREAM.Service.Api.Validators;
using MySqlConnector;

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


#region Storage

// Get a list of all storage targets
app.MapGet("/storage", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var storageTargets = await dbContext.StorageTargets.ToListAsync();
    return Results.Ok(storageTargets);
});

// Get a storage target by id
app.MapGet("/storage/{storageTargetId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
    long storageTargetId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var storageTarget = await dbContext.StorageTargets
        .FirstOrDefaultAsync(x => x.Id == storageTargetId);
    return storageTarget == null ? Results.NotFound() : Results.Ok(storageTarget);
});

// Combined endpoint for creating/editing and testing storage targets
app.MapPost("/storage", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
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
        return Results.Created($"/storage/{storageTarget.Id}", storageTarget);
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
app.MapDelete("/storage/{storageTargetId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
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
app.MapGet("/connections", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var databaseConnections = await dbContext.DatabaseConnections.ToListAsync();
    return Results.Ok(databaseConnections);
});

// Get a connection by id
app.MapGet("/connections/{databaseConnectionId:long}", async (HttpContext _,
    IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseConnectionId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var databaseConnection = await dbContext.DatabaseConnections
        .FirstOrDefaultAsync(x => x.Id == databaseConnectionId);
    return databaseConnection == null ? Results.NotFound() : Results.Ok(databaseConnection);
});

// Combined endpoint for creating/editing and testing database connections
app.MapPost("/connections", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
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
        return Results.Created($"/connections/{databaseConnection.Id}", databaseConnection);
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
app.MapDelete("/connections/{databaseConnectionId:long}", async (HttpContext _,
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
app.MapPost("/connections/{databaseConnectionId:long}/scan", async (HttpContext _,
    IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseConnectionId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var databaseConnection = dbContext.DatabaseConnections.Find(databaseConnectionId);
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

#region Backup Plans

// Get a list of all backup plans
app.MapGet("/backup-plans", async (IDbContextFactory<ScreamDbContext> dbContextFactory) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var backupPlans = await dbContext.BackupPlans
        .Include(i => i.DatabaseConnection)
        .Include(i => i.StorageTarget)
        .ToListAsync();
    return Results.Ok(backupPlans);
});
// Get a backup plan by id
app.MapGet("/backup-plans/{backupPlanId:long}", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
    long backupPlanId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var backupPlan = await dbContext.BackupPlans
        .Include(i => i.DatabaseConnection)
        .Include(i => i.StorageTarget)
        .Include(i => i.Items)
        .FirstOrDefaultAsync(x => x.Id == backupPlanId);
    return backupPlan == null ? Results.NotFound() : Results.Ok(backupPlan);
});
// Create or update a backup plan
app.MapPost("/backup-plans", async (IDbContextFactory<ScreamDbContext> dbContextFactory,
    BackupPlan backupPlan) =>
{
    // First test the database connection
    var isValid = ValidateDatabaseConnection(backupPlan.DatabaseConnection);
    if (!isValid)
    {
        return Results.BadRequest("Invalid database connection configuration.");
    }

    var testResult = await TestDatabaseConnection(backupPlan.DatabaseConnection);
    if (!testResult)
    {
        return Results.BadRequest("Database connection test failed.");
    }

    // If test succeeds, save the backup plan
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();

    if (backupPlan.Id == 0)
    {
        // Create new backup plan
        dbContext.BackupPlans.Add(backupPlan);
        await dbContext.SaveChangesAsync();
        return Results.Created($"/backup-plans/{backupPlan.Id}", backupPlan);
    }
    else
    {
        // Update existing backup plan
        var existingBackupPlan = await dbContext.BackupPlans
            .Include(i => i.DatabaseConnection)
            .Include(i => i.StorageTarget)
            .FirstOrDefaultAsync(x => x.Id == backupPlan.Id);

        if (existingBackupPlan == null)
        {
            return Results.NotFound();
        }

        // Use EF Update function 
        dbContext.Entry(existingBackupPlan).CurrentValues.SetValues(backupPlan);
        await dbContext.SaveChangesAsync();

        return Results.Ok(backupPlan);
    }
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