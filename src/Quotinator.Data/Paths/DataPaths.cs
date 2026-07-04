namespace Quotinator.Data.Paths;

/// <summary>File and folder names used within the Quotinator data directory.</summary>
public static class DataPaths
{
    /// <summary>Subdirectory containing one JSON file per bundled quote dataset plus a <c>manifest.json</c>.</summary>
    public const string SourcesFolder = "sources";

    /// <summary>Subdirectory for user-supplied import files. Optional — omitted in the default install.</summary>
    public const string ImportsFolder = "imports";

    /// <summary>
    /// Subdirectory (within <see cref="SourcesFolder"/> or <see cref="ImportsFolder"/>) where the
    /// auto-update mechanism caches downloaded copies of manifest entries that declare a
    /// <c>downloadUrl</c>/<c>github</c>. Combined with <see cref="SourcesFolder"/> for the
    /// "internal" cache (bundled sources default) or with <see cref="ImportsFolder"/> for the
    /// "external" cache (user imports default).
    /// </summary>
    public const string DownloadedSourcesFolder = "download";

    /// <summary>SQLite database file.</summary>
    public const string DatabaseFile = "quotinatordata.db";

    /// <summary>
    /// Legacy database filename used before v1.2.1.
    /// Renamed to <see cref="DatabaseFile"/> automatically on first startup after upgrade.
    /// </summary>
    public const string LegacyDatabaseFile = "quotes.db";

    /// <summary>
    /// Subdirectory where pre-migration database backups are written.
    /// Individual backups are named <c>quotinatordata_v{N}_{timestamp}Z.db</c> and are safe to delete after verification.
    /// </summary>
    public const string BackupsFolder = "backups";

    /// <summary>
    /// Subdirectory where ASP.NET Core Data Protection keys are stored.
    /// These keys sign antiforgery tokens and Blazor session descriptors.
    /// Deleting this folder invalidates all active browser sessions; the app recovers on restart but users must reload.
    /// </summary>
    public const string DataProtectionFolder = "keys";
}
