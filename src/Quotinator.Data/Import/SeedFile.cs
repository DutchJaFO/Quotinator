namespace Quotinator.Data.Import;

/// <summary>A single source file within a <see cref="SeedBatch"/>, with optional provenance and download URLs.</summary>
/// <param name="FilePath">Absolute path to the JSON source file.</param>
/// <param name="Url">Optional URL identifying where the file was sourced from, for human-readable provenance. A bundled file is always recorded as <c>Seed</c> type regardless of whether this is set — present when externally sourced, null when authored internally (e.g. curated content).</param>
/// <param name="DownloadUrl">Optional direct, fetchable URL to the raw source file, used by the auto-update mechanism to refresh the file. Resolved at manifest-parse time — never persisted.</param>
/// <param name="RefreshIntervalHours">Optional per-source override of <c>Quotinator__SourceUpdateIntervalHours</c> — how long a downloaded copy of this file is considered fresh. Only meaningful alongside <paramref name="DownloadUrl"/>.</param>
/// <param name="DownloadTarget">Optional per-source override of which cache folder (internal or external) a downloaded copy of this file is written to. When <c>null</c>, the default is derived from the owning <see cref="SeedBatch"/>'s <see cref="SeedBatchOrigin"/>.</param>
/// <param name="Converter">Optional name of an <see cref="IQuoteSourceConverter"/> plugin that transforms this source's raw upstream format into Quotinator's canonical schema before it is cached. Only meaningful alongside <paramref name="DownloadUrl"/>.</param>
public record SeedFile(
    string FilePath,
    string? Url,
    string? DownloadUrl = null,
    int? RefreshIntervalHours = null,
    DownloadTarget? DownloadTarget = null,
    string? Converter = null);
