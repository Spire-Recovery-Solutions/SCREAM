using SCREAM.Data.Entities;
using System.ComponentModel.DataAnnotations;

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
}
