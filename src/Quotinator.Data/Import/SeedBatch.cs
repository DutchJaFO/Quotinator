namespace Quotinator.Data.Import;

/// <summary>A group of source files that share a common manifest and duplicate-resolution policy.</summary>
/// <param name="Files">Source files, in the order they should be imported.</param>
/// <param name="Policy">Resolved duplicate-resolution policy governing all files in this batch.</param>
/// <param name="Label">Human-readable label used in log messages (e.g. <c>"bundled sources"</c>, <c>"user imports"</c>).</param>
public record SeedBatch(
    IReadOnlyList<SeedFile> Files,
    ManifestPolicy          Policy,
    string                  Label);
