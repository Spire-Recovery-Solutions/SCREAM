using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SCREAM.Data.Migrations;

public class ScreamDbDesignTimeContextFactory : IDesignTimeDbContextFactory<ScreamDbContext>
{
    public ScreamDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ScreamDbContext>();
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "ScreamDb.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new ScreamDbContext(optionsBuilder.Options);
    }
}