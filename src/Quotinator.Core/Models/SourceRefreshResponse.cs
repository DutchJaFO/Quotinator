namespace Quotinator.Core.Models;

/// <summary>Response envelope for <c>POST /api/v1/admin/sources/refresh</c>.</summary>
public sealed class SourceRefreshResponse
{
    /// <summary>One result per manifest entry that declares a <c>downloadUrl</c>/<c>github</c>.</summary>
    public required IReadOnlyList<SourceRefreshResultResponse> Results { get; init; }
}

/// <summary>The outcome of resolving a single manifest entry, within a <see cref="SourceRefreshResponse"/>.</summary>
public sealed class SourceRefreshResultResponse
{
    /// <summary>The source file's basename (e.g. <c>vilaboim_movie-quotes.json</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The <c>downloadUrl</c> this entry was resolved from.</summary>
    public required string Url { get; init; }

    /// <summary>What happened (wire value, e.g. <c>"updated"</c>, <c>"uptodate"</c>, <c>"failed"</c>, <c>"skippedcollision"</c>).</summary>
    public required string Outcome { get; init; }

    /// <summary>Optional human-readable detail (e.g. a collision's shared path, or a failure reason).</summary>
    public string? Detail { get; init; }

    /// <summary>The effective cache file's own last-write time, or <c>null</c> when no trusted cache file exists.</summary>
    public DateTime? LastRefreshedAtUtc { get; init; }
}
