namespace Quotinator.Data.Import;

/// <summary>Which cache folder an auto-updated source's downloaded copy is written to.</summary>
public enum DownloadTarget
{
    /// <summary><c>{dataDir}/sources/download/</c> — default for entries in the bundled sources manifest.</summary>
    Internal,

    /// <summary><c>{dataDir}/imports/download/</c> — default for entries in the user imports manifest.</summary>
    External
}
