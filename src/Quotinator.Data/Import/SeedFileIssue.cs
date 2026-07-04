namespace Quotinator.Data.Import;

/// <summary>Why a source file contributed zero quotes to a seed/preview operation, when it isn't simply empty.</summary>
public enum SeedFileIssue
{
    /// <summary>The file does not exist on disk.</summary>
    Missing,

    /// <summary>The file exists but is empty or its content is not valid JSON in the expected shape.</summary>
    InvalidJson
}
