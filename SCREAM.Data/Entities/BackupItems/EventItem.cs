using CliWrap.Builders;
using System.Text.Json.Serialization;

namespace SCREAM.Data.Entities.BackupItems;

/// <summary>
/// Represents database events for an entire schema
/// </summary>
public class EventItem : BackupItem
{
    public EventItem()
    {
        
    }

    [JsonIgnore]
    public override BackupItemType Type
    {
        get => BackupItemType.Event;
        set => throw new NotImplementedException();
    }

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