using CliWrap.Builders;

namespace SCREAM.Data.Entities;

/// <summary>
/// Base class for all database objects that can be backed up
/// </summary>
public abstract class BackupItem
{
    /// <summary>
    /// The schema (database) the item belongs to.
    /// </summary>
    public string Schema { get; set; } = string.Empty;

    /// <summary>
    /// The name of the item (e.g., table name, view name). May be empty for schema-level objects.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the item is selected for backup.
    /// </summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>
    /// The type of backup item.
    /// </summary>
    public abstract BackupItemType Type { get; }

    /// <summary>
    /// Configures the CliWrap ArgumentsBuilder with arguments specific to this item type
    /// </summary>
    public abstract void ConfigureArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize);

    /// <summary>
    /// Gets the output filename for this backup item
    /// </summary>
    public abstract string GetOutputFileName();
}

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

/// <summary>
/// Represents a table structure (CREATE TABLE statement)
/// </summary>
public class TableStructureItem : BackupItem
{
    public override BackupItemType Type => BackupItemType.TableStructure;

    /// <summary>
    /// The storage engine of the table (e.g., InnoDB, MyISAM).
    /// </summary>
    public string Engine { get; set; } = string.Empty;

    public override void ConfigureArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize)
    {
        args.Add($"--host={host}")
            .Add($"--user={user}")
            .Add($"--password={password}")
            .Add("--add-drop-table")
            .Add("--dump-date")
            .Add("--single-transaction")
            .Add("--skip-add-locks")
            .Add("--quote-names")
            .Add("--no-data")
            .Add("--skip-routines")
            .Add("--skip-events")
            .Add("--skip-triggers")
            .Add($"--max-allowed-packet={maxPacketSize}")
            .Add("--column-statistics=0")
            .Add(Schema)
            .Add(Name);
    }

    public override string GetOutputFileName()
    {
        return $"{Schema}.{Name}-schema.sql.xz.enc";
    }
}

/// <summary>
/// Represents table data (INSERT statements)
/// </summary>
public class TableDataItem : BackupItem
{
    public override BackupItemType Type => BackupItemType.TableData;

    /// <summary>
    /// The number of rows in the table.
    /// </summary>
    public long RowCount { get; set; }

    public override void ConfigureArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize)
    {
        args.Add($"--host={host}")
            .Add($"--user={user}")
            .Add($"--password={password}")
            .Add("--no-create-info")
            .Add("--skip-triggers")
            .Add("--skip-routines")
            .Add("--skip-events")
            .Add("--complete-insert")
            .Add("--disable-keys")
            .Add("--dump-date")
            .Add("--extended-insert")
            .Add("--no-autocommit")
            .Add("--quick")
            .Add("--single-transaction")
            .Add("--skip-add-locks")
            .Add("--quote-names")
            .Add($"--max-allowed-packet={maxPacketSize}")
            .Add("--column-statistics=0")
            .Add(Schema)
            .Add(Name);
    }

    public override string GetOutputFileName()
    {
        return $"{Schema}.{Name}-data.sql.xz.enc";
    }
}

/// <summary>
/// Represents a database view
/// </summary>
public class ViewItem : BackupItem
{
    public override BackupItemType Type => BackupItemType.View;

    public override void ConfigureArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize)
    {
        args.Add($"--host={host}")
            .Add($"--user={user}")
            .Add($"--password={password}")
            .Add("--add-drop-table")
            .Add("--dump-date")
            .Add("--single-transaction")
            .Add("--skip-add-locks")
            .Add("--quote-names")
            .Add("--no-data")
            .Add("--skip-routines")
            .Add("--skip-events")
            .Add("--skip-triggers")
            .Add($"--max-allowed-packet={maxPacketSize}")
            .Add("--column-statistics=0")
            .Add(Schema)
            .Add(Name);
    }

    public override string GetOutputFileName()
    {
        return $"{Schema}.{Name}-view.sql.xz.enc";
    }
}

/// <summary>
/// Represents database triggers for an entire schema
/// </summary>
public class TriggerItem : BackupItem
{
    public override BackupItemType Type => BackupItemType.Trigger;

    public override void ConfigureArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize)
    {
        args.Add($"--host={host}")
            .Add($"--user={user}")
            .Add($"--password={password}")
            .Add("--add-drop-trigger")
            .Add("--dump-date")
            .Add("--single-transaction")
            .Add("--skip-add-locks")
            .Add("--quote-names")
            .Add("--no-data")
            .Add("--no-create-db")
            .Add("--no-create-info")
            .Add("--skip-routines")
            .Add("--skip-events")
            .Add("--triggers")
            .Add($"--max-allowed-packet={maxPacketSize}")
            .Add("--column-statistics=0")
            .Add(Schema);
    }

    public override string GetOutputFileName()
    {
        return $"{Schema}-triggers.sql.xz.enc";
    }
}

/// <summary>
/// Represents database events for an entire schema
/// </summary>
public class EventItem : BackupItem
{
    public override BackupItemType Type => BackupItemType.Event;

    public override void ConfigureArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize)
    {
        args.Add($"--host={host}")
            .Add($"--user={user}")
            .Add($"--password={password}")
            .Add("--no-data")
            .Add("--no-create-db")
            .Add("--no-create-info")
            .Add("--skip-routines")
            .Add("--events")
            .Add("--skip-triggers")
            .Add("--dump-date")
            .Add("--single-transaction")
            .Add("--skip-add-locks")
            .Add("--quote-names")
            .Add($"--max-allowed-packet={maxPacketSize}")
            .Add("--column-statistics=0")
            .Add(Schema);
    }

    public override string GetOutputFileName()
    {
        return $"{Schema}-events.sql.xz.enc";
    }
}

/// <summary>
/// Represents database functions and procedures for an entire schema
/// </summary>
public class FunctionProcedureItem : BackupItem
{
    public override BackupItemType Type => BackupItemType.FunctionProcedure;

    public override void ConfigureArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize)
    {
        args.Add($"--host={host}")
            .Add($"--user={user}")
            .Add($"--password={password}")
            .Add("--no-data")
            .Add("--no-create-db")
            .Add("--no-create-info")
            .Add("--routines")
            .Add("--skip-events")
            .Add("--skip-triggers")
            .Add("--single-transaction")
            .Add("--skip-add-locks")
            .Add("--quote-names")
            .Add($"--max-allowed-packet={maxPacketSize}")
            .Add("--column-statistics=0")
            .Add(Schema);
    }

    public override string GetOutputFileName()
    {
        return $"{Schema}-funcs.sql.xz.enc";
    }
}