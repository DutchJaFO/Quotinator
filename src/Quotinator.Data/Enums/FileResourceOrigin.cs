namespace Quotinator.Data.Enums;

/// <summary>Where an <see cref="Quotinator.Data.Entities.FileResourceEntity"/>'s content was captured from (#251).</summary>
public enum FileResourceOrigin
{
    /// <summary>Read-only directory bundled with the application image.</summary>
    Bundled,

    /// <summary>User-writable directory scanned for import files at startup.</summary>
    UserImports,

    /// <summary>Uploaded directly through <c>POST /api/v1/import</c> or <c>POST /api/v1/import/preview</c>.</summary>
    Uploaded
}
