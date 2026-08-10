namespace Quotinator.Data.Enums;

/// <summary>
/// Which write-path mechanism captured an <see cref="Quotinator.Data.Entities.FileResourceEntity"/>'s
/// content (#251). Deliberately named for the mechanism, not for import/seeding specifically — a
/// <see cref="Quotinator.Data.Entities.FileResourceEntity"/> row is not conceptually tied to imports at
/// all, so a future unrelated text-file consumer can reuse these same three values without the naming
/// implying quote-source content (#252). Renamed from <c>Bundled</c>/<c>UserImports</c>/<c>Uploaded</c>
/// to <c>System</c>/<c>User</c>/<c>Upload</c> for that reason — see
/// <see cref="Quotinator.Data.Entities.FileResourceEntity.HomeDirectoryKey"/> for the property that
/// replaces the old implicit Origin-to-directory mapping.
/// </summary>
public enum FileResourceOrigin
{
    /// <summary>Written by the application's own internal scan of a fixed/read-only local directory (e.g. the bundled sources folder).</summary>
    System,

    /// <summary>Written by a scan of a user-writable local directory (e.g. the user-imports folder).</summary>
    User,

    /// <summary>Written via a REST call — <c>POST /api/v1/import</c> or <c>POST /api/v1/import/preview</c>. Carries no local folder, so <see cref="Quotinator.Data.Entities.FileResourceEntity.HomeDirectoryKey"/> is always <see langword="null"/> for this origin.</summary>
    Upload
}
