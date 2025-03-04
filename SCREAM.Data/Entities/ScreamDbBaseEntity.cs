namespace SCREAM.Data.Entities;

public class ScreamDbBaseEntity
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}