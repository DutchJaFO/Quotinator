using Quotinator.Data.Entities;

namespace Quotinator.Core.Models;

/// <summary>The API response shape for one item in the paginated Conversations list — a lighter summary
/// than the full ordered line list <c>GET /api/v1/conversations/{id}</c> returns
/// (<see cref="Quotinator.Core.Models.ConversationResponse"/>), to avoid loading every conversation's
/// full line list (with translations) on a single paginated page.</summary>
public sealed class ConversationSummaryResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>Optional human-readable label for the conversation.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required CompletenessStatus CompletenessStatus { get; init; }

    /// <summary>The number of active lines in this conversation. Fetch the full ordered line list via
    /// <c>GET /api/v1/conversations/{id}</c> for more detail.</summary>
    public required int LineCount { get; init; }
}
