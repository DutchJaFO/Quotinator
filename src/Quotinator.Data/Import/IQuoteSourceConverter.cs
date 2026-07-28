using System.Text.Json;

namespace Quotinator.Data.Import;

/// <summary>
/// Converts a source's raw, non-canonical upstream format into Quotinator's canonical quote schema
/// (see <c>schemas/source-flat.schema.json</c>). Implementations are first-party, compiled plugins —
/// one per format — invoked by <see cref="ISourceCacheUpdater"/> when a manifest entry names one via
/// its <c>converter</c> field, or directly by the manual <c>POST /api/v1/import</c> upload path. Never
/// invoked as an external process; only the file-path contract (plus an opaque configuration payload)
/// crosses the boundary, keeping this interface free of any dependency on the canonical quote model or
/// on any specific plugin's own options shape.
/// </summary>
public interface IQuoteSourceConverter
{
    /// <summary>The identifier matched against a manifest entry's <c>converter</c> field (case-insensitive).</summary>
    string Name { get; }

    /// <summary>
    /// When <c>true</c>, this converter can only be selected from the bundled sources manifest
    /// (<see cref="SeedBatchOrigin.Bundled"/>) — a user-writable <c>imports/manifest.json</c> entry
    /// naming it is treated exactly like an unregistered converter name (fails closed). Defaults to
    /// <c>false</c>; no shipping converter currently opts in.
    /// </summary>
    bool IsInternalOnly => false;

    /// <summary>
    /// Reads the raw file at <paramref name="inputPath"/> and writes the canonical-schema equivalent to
    /// <paramref name="outputPath"/>. Throws <see cref="SourceConversionException"/> on unrecoverable
    /// input (e.g. the raw format could not be parsed at all, or zero entries converted successfully) —
    /// never writes a near-empty output file silently. <paramref name="options"/> is this converter's
    /// own configuration (from a manifest entry's or import request's <c>converterOptions</c>), passed
    /// through verbatim — deserialize it into this converter's own options type immediately; the
    /// interface itself carries no knowledge of what shape it is.
    /// </summary>
    Task ConvertAsync(string inputPath, string outputPath, JsonElement? options = null, CancellationToken cancellationToken = default);
}
