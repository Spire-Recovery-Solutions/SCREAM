namespace SCREAM.Data.Entities.StorageTargets;

public class AzureBlobStorageTarget : StorageTarget
{
    public required string ConnectionString { get; set; }
    public required string ContainerName { get; set; }
}