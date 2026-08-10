namespace Quotinator.Core.Models;

/// <summary>Response envelope for <c>POST /api/v1/import/file-resources/prune</c> (#251).</summary>
public sealed class FileResourcePruneResponse
{
    /// <summary>Number of <c>Import_FileResource</c> rows hard-deleted by the sweep.</summary>
    public required int PrunedCount { get; init; }
}
