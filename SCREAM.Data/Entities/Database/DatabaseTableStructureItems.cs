using CliWrap.Builders;
using SCREAM.Data.Entities.Database.DatabaseItems;

namespace SCREAM.Data.Entities.Database;

/// <summary>
/// Represents a table structure (CREATE TABLE statement)
/// </summary>
public class DatabaseTableStructureItems : DatabaseItem
{
    public override DatabaseItemType Type
    {
        get => DatabaseItemType.TableStructure;
        set { }
    }

    /// <summary>
    /// The storage engine of the table (e.g., InnoDB, MyISAM).
    /// </summary>
    public string Engine { get; set; } = string.Empty;

    public override void ConfigureBackupArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize)
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

    public override void ConfigureRestoreArguments(ArgumentsBuilder args, string host, string user, string password)
    {
        args.Add($"--host={host}")
            .Add($"--user={user}")
            .Add($"--password={password}")
            .Add(Schema)
            .Add(Name);
    }

    public override string GetBackupFileName()
    {
        return $"{Schema}.{Name}-schema.sql.xz.enc";
    }
}