using Quotinator.Data.Import;

namespace Quotinator.Core.Models;

/// <summary>
/// Response envelope for <c>POST /api/v1/admin/database/reseed</c> and <c>.../database/reset</c> —
/// identical shape for both endpoints.
/// </summary>
public sealed class DatabaseSeedSummaryResponse
{
    /// <summary>Row count in the <c>Quotes</c> table after the operation completed.</summary>
    public required int Quotes { get; init; }

    /// <summary>Row count in the <c>Sources</c> table after the operation completed.</summary>
    public required int Sources { get; init; }

    /// <summary>Row count in the <c>Characters</c> table after the operation completed.</summary>
    public required int Characters { get; init; }

    /// <summary>Row count in the <c>People</c> table after the operation completed.</summary>
    public required int People { get; init; }

    /// <summary>Row count in the <c>Series</c> table after the operation completed.</summary>
    public required int Series { get; init; }

    /// <summary>Row count in the <c>Universes</c> table after the operation completed.</summary>
    public required int Universes { get; init; }

    /// <summary>Row count in the <c>StageDirections</c> table after the operation completed.</summary>
    public required int StageDirections { get; init; }

    /// <summary>Row count in the <c>SoundCues</c> table after the operation completed.</summary>
    public required int SoundCues { get; init; }

    /// <summary>Row count in the <c>Conversations</c> table after the operation completed.</summary>
    public required int Conversations { get; init; }

    /// <summary>Per-file, per-entity-type new/modified/blocked/discarded/pending/stale action report (issue #221).</summary>
    public required IReadOnlyList<FileImportReport> Reports { get; init; }
}
