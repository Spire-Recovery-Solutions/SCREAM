using CliWrap.Builders;

namespace SCREAM.Data.Entities;

/// <summary>
/// Represents a table structure (CREATE TABLE statement)
/// </summary>
public class TableStructureItem : BackupItem
{
    public override BackupItemType Type
    {
        get => BackupItemType.TableStructure;
        set => throw new NotImplementedException();
    }

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