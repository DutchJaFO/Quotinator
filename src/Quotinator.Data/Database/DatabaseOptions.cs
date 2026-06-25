namespace Quotinator.Data.Database;

/// <summary>Runtime paths and settings passed to <see cref="DatabaseInitializer"/> at startup.</summary>
public sealed record DatabaseOptions
{
    /// <summary>Absolute path to the <c>.db</c> file.</summary>
    public required string DbPath { get; init; }

    /// <summary>Directory where pre-migration backups are written.</summary>
    public string BackupsPath { get; init; } = string.Empty;
}
