namespace Quotinator.Data.Database;

/// <summary>Runtime paths and settings passed to <see cref="DatabaseInitializer"/> at startup.</summary>
public sealed record DatabaseOptions
{
    /// <summary>Absolute path to the <c>.db</c> file.</summary>
    public required string DbPath { get; init; }

    /// <summary>Directory where pre-migration backups are written.</summary>
    public string BackupsPath { get; init; } = string.Empty;

    /// <summary>
    /// Maximum total size, in GB, the <see cref="BackupsPath"/> folder's own accumulated backup files
    /// may grow to. A new backup is skipped (warning logged, no exception) rather than written once
    /// this budget would be exceeded — this is a hard, self-imposed ceiling independent of how much
    /// real disk space happens to be free. Default <c>1</c>: sized from a representative database size
    /// (~8 MB) × 10 backups = 80 MB, rounded up to a clean, convenient value. Overridable via
    /// <c>Quotinator:MaxBackupStorageGb</c>.
    /// </summary>
    public int MaxBackupStorageGb { get; init; } = 1;
}
