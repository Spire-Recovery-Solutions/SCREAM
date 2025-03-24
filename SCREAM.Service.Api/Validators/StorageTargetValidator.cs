using SCREAM.Data.Entities.StorageTargets;
using System.ComponentModel.DataAnnotations;

namespace SCREAM.Service.Api.Validators;

public static class StorageTargetValidator
{
    public static bool Validate(StorageTarget storageTarget)
    {
        var validationContext = new ValidationContext(storageTarget);
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(storageTarget, validationContext, validationResults, true);

        return isValid && storageTarget switch
        {
            LocalStorageTarget localStorageTarget => ValidateLocalStorageTarget(localStorageTarget),
            S3StorageTarget s3StorageTarget => ValidateS3StorageTarget(s3StorageTarget),
            _ => false
        };
    }

    private static bool ValidateLocalStorageTarget(LocalStorageTarget localStorageTarget)
    {
        return !string.IsNullOrEmpty(localStorageTarget.Path);
    }

    private static bool ValidateS3StorageTarget(S3StorageTarget s3StorageTarget)
    {
        return !string.IsNullOrEmpty(s3StorageTarget.BucketName) &&
               !string.IsNullOrEmpty(s3StorageTarget.AccessKey) &&
               !string.IsNullOrEmpty(s3StorageTarget.SecretKey);
    }
}
