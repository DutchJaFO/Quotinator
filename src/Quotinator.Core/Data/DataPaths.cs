namespace Quotinator.Core.Data;

/// <summary>File and folder names used within the Quotinator data directory.</summary>
public static class DataPaths
{
    /// <summary>Default quote dataset file (seed source and custom additions).</summary>
    public const string SeedFile = "quotes.json";

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
