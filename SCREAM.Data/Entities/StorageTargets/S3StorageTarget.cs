namespace SCREAM.Data.Entities.StorageTargets;

public class S3StorageTarget : StorageTarget
{
    public required string BucketName { get; set; }
    public required string AccessKey { get; set; }
    public required string SecretKey { get; set; }
}