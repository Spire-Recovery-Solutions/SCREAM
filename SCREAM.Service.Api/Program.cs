using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities;
using SCREAM.Data.Enums;
using SCREAM.Service.Api.Endpoints.BackupItemStatuses;
using SCREAM.Service.Api.Endpoints.DatabaseItems;
using SCREAM.Service.Api.Endpoints.Items;
using SCREAM.Service.Api.Endpoints.Jobs;
using SCREAM.Service.Api.Endpoints.Plans;
using SCREAM.Service.Api.Endpoints.Settings;
using SCREAM.Service.Api.Endpoints.Targets;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CONTAINER_APP_PORT")))
{
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        var port = int.Parse(Environment.GetEnvironmentVariable("CONTAINER_APP_PORT") ?? "80");
        serverOptions.ListenAnyIP(port);
    });
}

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

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Map all endpoint groups
app.MapStorageTargetEndpoints();
app.MapDatabaseTargetEndpoints();
app.MapBackupPlanEndpoints();
app.MapRestorePlanEndpoints();
app.MapBackupJobEndpoints();
app.MapRestoreJobEndpoints();
app.MapRestoreSettingsEndpoints();
app.MapBackupSettingsEndpoints();
app.MapBackupItemStatusEndpoints();
app.MapDatabaseItemEndpoints();
app.MapBackupItemEndpoints();
app.MapRestoreItemEndpoints();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ScreamDbContext>>();
    var dbContext = dbContextFactory.CreateDbContext();
    dbContext.Database.EnsureDeleted();
    dbContext.Database.Migrate();

    // Add a sample database target for development
    dbContext.DatabaseTargets.Add(new DatabaseTarget
    {
        HostName = Environment.GetEnvironmentVariable("MYSQL_HOSTNAME") ?? "localhost",
        Port = 3306,
        UserName = "root",
        // TODO: Move this password to user secrets or environment variables
        Password = "Here!Lives@A#Happy4Little%Password^",
        Type = DatabaseType.MySQL
    });

    dbContext.SaveChanges();
}

app.Run();