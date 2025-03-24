using CliWrap.Builders;

namespace SCREAM.Data.Entities.Database;

/// <summary>
/// Represents database functions and procedures for an entire schema
/// </summary>
public class DatabaseFunctionProcedureItems : DatabaseItem
{
    public override DatabaseItemType Type
    {
        get => DatabaseItemType.FunctionProcedure;
        set { }
    }

    public override void ConfigureBackupArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize)
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
}