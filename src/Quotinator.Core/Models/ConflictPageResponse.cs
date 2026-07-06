namespace Quotinator.Core.Models;

/// <summary>Paged conflict list response — matches <c>GET /api/v1/admin/audit</c>'s existing shape.</summary>
public sealed class ConflictPageResponse
{
    /// <summary>Total conflicts matching the filter, across all pages.</summary>
    public required int TotalMatching { get; init; }

    /// <summary>Total pages available at the requested page size.</summary>
    public required int TotalPages { get; init; }

    /// <summary>The requested page number (1-based).</summary>
    public required int Page { get; init; }

    /// <summary>The requested page size.</summary>
    public required int PageSize { get; init; }

    /// <summary>The conflicts on this page.</summary>
    public required IReadOnlyList<ConflictSummaryResponse> Items { get; init; }
}
