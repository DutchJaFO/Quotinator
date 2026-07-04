namespace Quotinator.Data.Import;

/// <summary>
/// Converts a source's raw, non-canonical upstream format into Quotinator's canonical quote schema
/// (see <c>schemas/source-flat.schema.json</c>). Implementations are first-party, compiled plugins —
/// one per source — invoked by <see cref="ISourceCacheUpdater"/> when a manifest entry names one via
/// its <c>converter</c> field. Never invoked as an external process; only the file-path contract
/// crosses the boundary, keeping this interface free of any dependency on the canonical quote model.
/// </summary>
public interface IQuoteSourceConverter
{
    /// <summary>The identifier matched against a manifest entry's <c>converter</c> field (case-insensitive).</summary>
    string Name { get; }

    /// <summary>
    /// Reads the raw file at <paramref name="inputPath"/> and writes the canonical-schema equivalent to
    /// <paramref name="outputPath"/>. Throws <see cref="SourceConversionException"/> on unrecoverable
    /// input (e.g. the raw format could not be parsed at all, or zero entries converted successfully) —
    /// never writes a near-empty output file silently.
    /// </summary>
    Task ConvertAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default);
}
