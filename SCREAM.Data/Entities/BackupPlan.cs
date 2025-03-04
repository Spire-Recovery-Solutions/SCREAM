namespace SCREAM.Data.Entities;

public class BackupPlan : ScreamDbBaseEntity
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required DatabaseConnection DatabaseConnection { get; set; }
    
    
}