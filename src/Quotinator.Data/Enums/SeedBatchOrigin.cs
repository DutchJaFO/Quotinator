using Quotinator.Data.Import;

namespace Quotinator.Data.Enums;

/// <summary>Where a <see cref="SeedBatch"/>'s files were discovered from.</summary>
public enum SeedBatchOrigin
{
    /// <summary>Read-only directory bundled with the application image.</summary>
    Bundled,

    /// <summary>User-writable directory scanned for import files at startup.</summary>
    UserImports
}
