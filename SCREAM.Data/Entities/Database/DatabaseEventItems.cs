using CliWrap.Builders;
using SCREAM.Data.Entities.Database.DatabaseItems;

namespace SCREAM.Data.Entities.Database;

/// <summary>
/// Represents database events for an entire schema
/// </summary>
public class DatabaseEventItems : DatabaseItem
{
    public override DatabaseItemType Type
    {
        get => DatabaseItemType.Event;
        set { }
    }

    public override void ConfigureBackupArguments(ArgumentsBuilder args,
        string host, string user, string password, string maxPacketSize)
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

    public override void ConfigureRestoreArguments(ArgumentsBuilder args,
        string host, string user, string password)
    {
        args.Add($"--host={host}")
            .Add($"--user={user}")
            .Add($"--password={password}")
            .Add("--skip-routines")
            .Add("--events");

    }

    public override string GetBackupFileName() => $"{Schema}-events.sql.xz.enc";
}