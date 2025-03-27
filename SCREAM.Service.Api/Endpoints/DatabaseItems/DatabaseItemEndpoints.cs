using Microsoft.EntityFrameworkCore;
using SCREAM.Data;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.Database;
using SCREAM.Data.Entities.Restore;

namespace SCREAM.Service.Api.Endpoints.DatabaseItems
{
    public static class DatabaseItemEndpoints
    {
        public static void MapDatabaseItemEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/database/items")
                .WithTags("DatabaseItems");

            group.MapGet("/", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var items = await dbContext.DatabaseItems
                    .ToListAsync();
                return Results.Ok(items);
            });

            // GET: Get database items by type
            group.MapGet("/type/{type}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                DatabaseItemType type) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var items = await dbContext.DatabaseItems
                    .Where(i => i.Type == type)
                    .ToListAsync();
                return Results.Ok(items);
            });

            // GET: Get a specific database item by id
            group.MapGet("/{itemId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long itemId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                var item = await dbContext.DatabaseItems
                    .FirstOrDefaultAsync(i => i.Id == itemId);
                return item == null ? Results.NotFound() : Results.Ok(item);
            });

            // POST: Create a new database item
            group.MapPost("/", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                DatabaseItem item) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                // Validate required fields
                if (string.IsNullOrWhiteSpace(item.Schema) || string.IsNullOrWhiteSpace(item.Name))
                    return Results.BadRequest("Schema and Name are required");

                if (item.Id == 0)
                {
                    // Create new DatabaseItem.
                    dbContext.DatabaseItems.Add(item);
                    await dbContext.SaveChangesAsync();
                    return Results.Created($"/database/items/{item.Id}", item);
                }
                else
                {
                    var existingItem = await dbContext.DatabaseItems
                        .FirstOrDefaultAsync(i => i.Id == item.Id);
                    if (existingItem == null)
                        return Results.NotFound();

                    existingItem.Schema = item.Schema;
                    existingItem.Name = item.Name;

                    switch (existingItem)
                    {
                        case DatabaseTableStructureItems tableStructure when item is DatabaseTableStructureItems updatedTableStructure:
                            tableStructure.Engine = updatedTableStructure.Engine;
                            break;
                        case DatabaseTableDataItems tableData when item is DatabaseTableDataItems updatedTableData:
                            tableData.RowCount = updatedTableData.RowCount;
                            break;
                        default:
                            break;
                    }
                    await dbContext.SaveChangesAsync();
                    return Results.Ok(existingItem);
                }
            });

            group.MapDelete("/{itemId:long}", async (
                IDbContextFactory<ScreamDbContext> dbContextFactory,
                long itemId) =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();

                // Check if the item is referenced in BackupItems or RestoreItems
                var isReferenced = await dbContext.BackupItems.AnyAsync(b => b.DatabaseItemId == itemId) ||
                                   await dbContext.Set<RestoreItem>().AnyAsync(r => r.DatabaseItemId == itemId);

                if (isReferenced)
                    return Results.Conflict("Cannot delete database item referenced by backup or restore items");

                var item = await dbContext.DatabaseItems
                    .FirstOrDefaultAsync(i => i.Id == itemId);

                if (item == null)
                    return Results.NotFound();

                dbContext.DatabaseItems.Remove(item);
                await dbContext.SaveChangesAsync();
                return Results.NoContent();
            });
        }
    }
}
