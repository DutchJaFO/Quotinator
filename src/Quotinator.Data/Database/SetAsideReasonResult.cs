using Quotinator.Data.Database.Checks;

namespace Quotinator.Data.Database;

/// <summary>
/// Why a Reset set a database file aside rather than backing it up (#482, #490): the layer of the database
/// check that showed the file was not a sound copy, or, when the check passed, the backup's own error on
/// finding the file unreadable. Exactly one of the two is set.
/// <para>
/// Reported to whoever ran the Reset, in its response, its notifications and its refusal: a reason that
/// stops something is never left in the log alone.
/// </para>
/// </summary>
/// <remarks>Suffixed <c>Result</c> per ADR 016: part of the in-process outcome of a Reset.</remarks>
public sealed class SetAsideReasonResult
{
    /// <summary>The check layer that showed the file was not a sound copy, with its status and finding; otherwise <see langword="null"/>.</summary>
    public DatabaseCheckLayerResult? CheckLayer { get; private init; }

    /// <summary>
    /// The backup's own error, verbatim, when the check passed but the backup copy found the file
    /// unreadable; otherwise <see langword="null"/>. Carried to be shown, never parsed.
    /// </summary>
    public string? BackupError { get; private init; }

    /// <summary>The database check showed the file was not a sound copy.</summary>
    /// <param name="layer">The layer that showed it.</param>
    public static SetAsideReasonResult FromCheck(DatabaseCheckLayerResult layer) => new() { CheckLayer = layer };

    /// <summary>The check passed, but the backup copy found the file unreadable.</summary>
    /// <param name="backupError">The backup's own error.</param>
    public static SetAsideReasonResult FromBackup(string backupError) => new() { BackupError = backupError };
}
