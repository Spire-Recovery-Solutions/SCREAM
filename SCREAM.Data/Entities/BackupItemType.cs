namespace SCREAM.Data.Entities;

/// <summary>
/// Types of database objects that can be backed up
/// </summary>
public enum BackupItemType
{
    TableStructure, // The schema definition of a table
    TableData,      // The data (rows) of a table
    View,           // A database view
    Trigger,        // A database trigger
    Event,          // A scheduled event
    FunctionProcedure  // Functions and stored procedures (dumped together)
}