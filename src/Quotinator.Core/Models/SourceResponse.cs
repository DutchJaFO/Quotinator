using Quotinator.Data.Entities;
using Quotinator.Data.Enums;

namespace Quotinator.Core.Models;

/// <summary>The API response shape for a single Source — a film, television series, book, or other
/// source from which quotes are drawn.</summary>
public sealed class SourceResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The title of the source in its original language.</summary>
    public required string Title { get; init; }

    /// <summary>Media category: <c>movie</c>, <c>tv</c>, <c>anime</c>, <c>book</c>, or <c>person</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Publication or release date, as precise as the source allows (e.g. <c>"1994"</c>,
    /// <c>"1994-06"</c>). <c>null</c> when unknown.</summary>
    public string? Date { get; init; }

    /// <summary>The series this source belongs to, if any (#179), as a minimal read-only reference — the
    /// series' <c>Id</c>/<c>Name</c> only, resolved via <c>ISourceSeriesReferenceReader</c>.
    /// <c>null</c> for a standalone source, and <c>null</c> if the linked series has been soft-deleted
    /// (per CLAUDE.md's "Soft-deleted rows are invisible by default" convention — a dangling reference to
    /// a deleted series is never surfaced).</summary>
    public MasterDataReference? Series { get; init; }

    /// <summary>The season this source is, if it is an instalment within a series (#375, ADR 011), as a
    /// minimal read-only reference whose <c>Name</c> is the season's rendered display name (e.g. "Book
    /// One: Water") — resolved via <c>ISourceSeasonReferenceReader</c>. <c>null</c> when the source is
    /// not a season's episode, and <c>null</c> if the linked season has been soft-deleted (same
    /// dangling-reference rule as <see cref="Series"/>).</summary>
    public MasterDataReference? Season { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required CompletenessStatus CompletenessStatus { get; init; }
}
