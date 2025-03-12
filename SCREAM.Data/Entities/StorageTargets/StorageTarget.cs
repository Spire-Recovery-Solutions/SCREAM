using System.Text.Json.Serialization;

namespace SCREAM.Data.Entities.StorageTargets;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "typeObj")]
[JsonDerivedType(typeof(LocalStorageTarget), typeDiscriminator:  (int)(StorageTargetType.Local))]
[JsonDerivedType(typeof(S3StorageTarget), typeDiscriminator:  (int)(StorageTargetType.S3))]
[JsonDerivedType(typeof(AzureBlobStorageTarget),  (int)(StorageTargetType.AzureBlob))]
[JsonDerivedType(typeof(GoogleCloudStorageTarget), (int)StorageTargetType.GoogleCloudStorage)]
public abstract class StorageTarget : ScreamDbBaseEntity
{
    public required string Name { get; set; }

    public StorageTargetType Type { get; set; }

    public string? Description { get; set; }

}