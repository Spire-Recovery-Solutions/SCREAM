using SCREAM.Data.Entities;

namespace SCREAM.Web.Services
{
    public interface IDatabaseService
    {
        Task<List<string>> GetDatabasesAsync(DatabaseConnection connection);
    }

    public class DatabaseService : IDatabaseService
    {
        public async Task<List<string>> GetDatabasesAsync(DatabaseConnection connection)
        {
            // This would use MySqlConnector to actually query databases
            // For now, we return a mock list
            await Task.Delay(100); // Simulate network delay
            return new List<string> { "mysql", "information_schema", "sample_db" };
        }
    }
}
