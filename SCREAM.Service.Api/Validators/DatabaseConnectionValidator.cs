using SCREAM.Data.Entities;
using System.ComponentModel.DataAnnotations;
using MySqlConnector;

namespace SCREAM.Service.Api.Validators;

public static class DatabaseConnectionValidator
{
    public static bool Validate(DatabaseConnection databaseConnection)
    {
        var validationContext = new ValidationContext(databaseConnection);
        var validationResults = new List<ValidationResult>();
        bool isValid = Validator.TryValidateObject(databaseConnection, validationContext, validationResults, true);

        if (!isValid)
        {
            return false;
        }

        return ValidateDatabaseConnectionProperties(databaseConnection);
    }

    private static bool ValidateDatabaseConnectionProperties(DatabaseConnection databaseConnection)
    {
        return !string.IsNullOrEmpty(databaseConnection.HostName) &&
               databaseConnection.Port > 0 &&
               !string.IsNullOrEmpty(databaseConnection.UserName) &&
               !string.IsNullOrEmpty(databaseConnection.Password);
    }

    public static bool ValidateConnectionString(string connectionString)
    {
        try
        {
            var builder = new MySqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrEmpty(builder.Server) &&
                   builder.Port > 0 &&
                   !string.IsNullOrEmpty(builder.UserID) &&
                   !string.IsNullOrEmpty(builder.Password);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> TestDatabaseConnection(DatabaseConnection databaseConnection)
    {
        try
        {
            using var connection = new MySqlConnection(databaseConnection.ConnectionString);
            await connection.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
