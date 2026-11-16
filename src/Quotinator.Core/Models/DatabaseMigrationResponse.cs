namespace Quotinator.Core.Models;

/// <summary>
/// Response envelope for <c>POST /api/v1/admin/database/migrate</c> (#490 step 17): the schema versions the database
/// now records, and what the migration left to restore from.
/// </summary>
public sealed class DatabaseMigrationResponse
{
    /// <summary>The version of Quotinator.Data's own migrations the database now records.</summary>
    public required int DataSchemaVersion { get; init; }

    /// <summary>The version of this application's own migrations the database now records.</summary>
    public required int AppSchemaVersion { get; init; }

    /// <summary>How many migrations this call applied.</summary>
    public required int MigrationsApplied { get; init; }

    /// <summary>
    /// What the migration left to restore from: the backup it took, or that it took none, because the operator
    /// confirmed it, and why. Absent when no migration ran.
    /// </summary>
    public RestorePointDto? Backup { get; init; }
}
