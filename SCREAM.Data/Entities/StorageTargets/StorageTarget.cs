namespace SCREAM.Data.Entities.StorageTargets;

public class StorageTarget : ScreamDbBaseEntity
{
    public required string Name { get; set; }
    public required StorageTargetType Type { get; set; }
    public string? Description { get; set; }

}