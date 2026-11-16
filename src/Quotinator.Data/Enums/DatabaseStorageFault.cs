namespace Quotinator.Data.Enums;

/// <summary>
/// What the database check's Storage layer found wrong (#490, ADR 028), so a caller can act on it without
/// reading the finding's text, which is never parsed.
/// </summary>
public enum DatabaseStorageFault
{
    /// <summary>The database file exists but cannot be read.</summary>
    FileUnreadable,

    /// <summary>The data directory cannot be written.</summary>
    DirectoryNotWritable,
}
