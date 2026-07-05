using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Import;

namespace Quotinator.Engine.Services;

/// <summary>
/// Imports a single source file — reusing the same duplicate-detection/merge engine the startup
/// seeder uses (see <see cref="Database.QuoteSeedWriter"/>) — for the live
/// <c>POST /api/v1/quotes/import</c> and <c>.../import/preview</c> endpoints.
/// </summary>
/// <remarks>
/// Lives in <c>Quotinator.Engine</c> rather than <c>Quotinator.Core</c> (unlike <see cref="IQuoteService"/>)
/// because its signature needs <see cref="ImportRequestSettingsDto"/>, a <c>Quotinator.Data</c> type —
/// Core and Data must never depend on each other, so an interface needing both must live where both
/// are already legitimately referenced.
/// </remarks>
public interface IQuoteImportService
{
    /// <summary>
    /// Imports every quote in <paramref name="file"/>. When <paramref name="preview"/> is <c>true</c>,
    /// the full pipeline runs (including conflict detection and logging) but every write is rolled
    /// back before returning — the response reflects exactly what a real run would have done.
    /// </summary>
    /// <param name="file">The uploaded file's raw content.</param>
    /// <param name="fileName">The uploaded file's own name — used as the <c>ImportBatch</c>'s display name.</param>
    /// <param name="settings">Optional converter/duplicate-resolution/enrich settings for this run.</param>
    /// <param name="preview">When <c>true</c>, rolls back all writes instead of committing them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuoteImportValidationException">
    /// The settings named an unrecognised converter, or the file's content could not be parsed/converted
    /// into at least one valid quote.
    /// </exception>
    Task<ImportResultResponse> ImportAsync(
        Stream file, string fileName, ImportRequestSettingsDto? settings, bool preview, CancellationToken cancellationToken = default);
}
