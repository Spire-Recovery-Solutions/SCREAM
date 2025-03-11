using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SCREAM.Business;
using SCREAM.Data;
using SCREAM.Data.Entities;
using SCREAM.Data.Enums;

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

//Edit connection
app.MapPut("/connections/{databaseConnectionId:long}", async (HttpContext _,
    IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseConnectionId, DatabaseConnection databaseConnection) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var existingConnection = await dbContext.DatabaseConnections
        .FirstOrDefaultAsync(x => x.Id == databaseConnectionId);
    if (existingConnection == null)
    {
        return Results.NotFound();
    }

    existingConnection.HostName = databaseConnection.HostName;
    existingConnection.Port = databaseConnection.Port;
    existingConnection.UserName = databaseConnection.UserName;
    existingConnection.Password = databaseConnection.Password;
    existingConnection.Type = databaseConnection.Type;

    await dbContext.SaveChangesAsync();

    return Results.NoContent();
});

// Test a connection
app.MapPost("/connections/{databaseConnectionId:long}/test", async (HttpContext _,
    IDbContextFactory<ScreamDbContext> dbContextFactory, long databaseConnectionId) =>
{
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var databaseConnection = await dbContext.DatabaseConnections
        .FirstOrDefaultAsync(x => x.Id == databaseConnectionId);
    if (databaseConnection == null)
    {
        return Results.NotFound();
    }

    //TODO: Test connection
    // var connectionTester = new ConnectionTester();
    // var result = await connectionTester.TestConnection(databaseConnection);

    return Results.Ok();
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