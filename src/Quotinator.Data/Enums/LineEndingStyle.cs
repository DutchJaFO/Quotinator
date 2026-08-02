namespace Quotinator.Data.Enums;

/// <summary>
/// The line-terminator style detected in an <see cref="Quotinator.Data.Entities.FileResourceEntity"/>'s
/// content (#251). Recorded once per file — this project's own confirmed assumption is that line
/// endings are uniform within a single file, not mixed.
/// </summary>
public enum LineEndingStyle
{
    /// <summary><c>\n</c> — Unix-style.</summary>
    LF,

    /// <summary><c>\r\n</c> — Windows-style.</summary>
    CRLF,

    /// <summary><c>\r</c> — legacy classic Mac-style.</summary>
    CR
}
