namespace Quotinator.Data.Enums;

/// <summary>
/// An action on the whole database that a user can run from within the application (#348): what a
/// <see cref="NotificationMetadataKind.DatabaseActionResult"/> notification reports the result of.
/// </summary>
public enum DatabaseAction
{
    /// <summary>The database was replaced with a new, empty one.</summary>
    Reset,

    /// <summary>The bundled and user content was loaded into the database again.</summary>
    Reseed,
}
