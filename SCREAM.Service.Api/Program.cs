using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SCREAM.Business;
using SCREAM.Data;
using SCREAM.Data.Entities;
using SCREAM.Data.Enums;

var builder = WebApplication.CreateBuilder(args);

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/connections/{databaseConnectionId:long}/scan", async (HttpContext httpContext,
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