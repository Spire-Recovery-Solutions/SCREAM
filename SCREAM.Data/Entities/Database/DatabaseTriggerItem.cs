using CliWrap.Builders;
using SCREAM.Data.Entities.Database.DatabaseItems;

namespace SCREAM.Data.Entities.Database;

/// <summary>
/// Represents database triggers for an entire schema
/// </summary>
public class DatabaseTriggerItems : DatabaseItem
{
    public override DatabaseItemType Type
    {
        get => DatabaseItemType.Trigger;
        set { }
    }

    public override void ConfigureBackupArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize)
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

    public override void ConfigureRestoreArguments(ArgumentsBuilder args, string host, string user, string password)
    {
        args.Add($"--host={host}")
            .Add($"--user={user}")
            .Add($"--password={password}")
            .Add("--triggers")
            .Add(Schema);
    }

    public override string GetBackupFileName()
    {
        return $"{Schema}-triggers.sql.xz.enc";
    }
}