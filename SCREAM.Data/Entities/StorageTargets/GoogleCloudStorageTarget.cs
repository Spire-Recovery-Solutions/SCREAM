namespace SCREAM.Data.Entities.StorageTargets;

public class GoogleCloudStorageTarget : StorageTarget
{
    public required string BucketName { get; set; }
    public required string ServiceAccountKey { get; set; }
}