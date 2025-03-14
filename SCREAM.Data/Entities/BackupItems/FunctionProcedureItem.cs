using CliWrap.Builders;
using System.Text.Json.Serialization;

namespace SCREAM.Data.Entities.BackupItems;

/// <summary>
/// Represents database functions and procedures for an entire schema
/// </summary>
public class FunctionProcedureItem : BackupItem
{
    public override BackupItemType Type
    {
        get => BackupItemType.FunctionProcedure;
        set { }
    }

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