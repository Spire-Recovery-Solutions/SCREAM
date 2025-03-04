namespace SCREAM.Data.Entities.StorageTargets;

public enum StorageTargetType
{
    Local,
    S3,
    [Obsolete("Currently not implemented")]
    AzureBlob,
    [Obsolete("Currently not implemented")]
    GoogleCloudStorage
}