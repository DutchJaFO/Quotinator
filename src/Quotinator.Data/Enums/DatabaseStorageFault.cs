namespace Quotinator.Data.Enums;

/// <summary>
/// What the database check's Storage layer found wrong (#490, ADR 028), so a caller can act on it without
/// reading the finding's text, which is never parsed.
/// </summary>
public enum DatabaseStorageFault
{
    /// <summary>The database file exists but cannot be read.</summary>
    FileUnreadable,

    /// <summary>The data directory, or the folder the check's copy goes in, cannot be written.</summary>
    DirectoryNotWritable,

    /// <summary>
    /// There is not enough room for the copy the check needs, measured before it is made, or found when the
    /// operating system refused it for want of space. Room for that copy is room a backup needs too.
    /// </summary>
    InsufficientSpace,
}
