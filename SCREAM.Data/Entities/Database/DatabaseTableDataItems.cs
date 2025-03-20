using CliWrap.Builders;
using SCREAM.Data.Entities.Database.DatabaseItems;

namespace SCREAM.Data.Entities.Database;

/// <summary>
/// Represents table data (INSERT statements)
/// </summary>
public class DatabaseTableDataItems : DatabaseItem
{
    public override DatabaseItemType Type
    {
        get => DatabaseItemType.TableData;
        set { }
    }


    /// <summary>
    /// The number of rows in the table.
    /// </summary>
    public long RowCount { get; set; }

    public override void ConfigureBackupArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize)
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
        return $"{Schema}.{Name}-data.sql.xz.enc";
    }
}