using SCREAM.Data.Entities;
using System.ComponentModel.DataAnnotations;
using MySqlConnector;

namespace SCREAM.Service.Api.Validators;

public static class DatabaseTargetValidator
{
    public static bool Validate(DatabaseTarget databaseTarget)
    {
        var validationContext = new ValidationContext(databaseTarget);
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(databaseTarget, validationContext, validationResults, true);

        return isValid && ValidateDatabaseTargetProperties(databaseTarget);
    }

    private static bool ValidateDatabaseTargetProperties(DatabaseTarget databaseTarget)
    {
        return !string.IsNullOrEmpty(databaseTarget.HostName) &&
               databaseTarget.Port > 0 &&
               !string.IsNullOrEmpty(databaseTarget.UserName) &&
               !string.IsNullOrEmpty(databaseTarget.Password);
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

    public static async Task<bool> TestDatabaseTarget(DatabaseTarget databaseTarget)
    {
        try
        {
            await using var connection = new MySqlConnection(databaseTarget.ConnectionString);
            await connection.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
