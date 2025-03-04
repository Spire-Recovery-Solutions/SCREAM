using System.ComponentModel.DataAnnotations.Schema;
using CliWrap.Builders;

namespace SCREAM.Data.Entities;

/// <summary>
/// Base class for all database objects that can be backed up
/// </summary>
public abstract class BackupItem : ScreamDbBaseEntity
{
    public long Id { get; set; }

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
    public abstract BackupItemType Type { get; set; }


    /// <summary>
    /// Configures the CliWrap ArgumentsBuilder with arguments specific to this item type
    /// </summary>
    public abstract void ConfigureArguments(ArgumentsBuilder args, string host, string user, string password, string maxPacketSize);

    /// <summary>
    /// Gets the output filename for this backup item
    /// </summary>
    public abstract string GetOutputFileName();
}