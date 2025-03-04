using CliWrap.Builders;

namespace SCREAM.Data.Entities;

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