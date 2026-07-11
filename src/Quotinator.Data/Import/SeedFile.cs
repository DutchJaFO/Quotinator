namespace Quotinator.Data.Import;

/// <summary>A single source file within a <see cref="SeedBatch"/>, with optional provenance and download URLs.</summary>
/// <param name="FilePath">Absolute path to the JSON source file.</param>
/// <param name="Url">Optional URL identifying where the file was sourced from, for human-readable provenance. When set, the seeder records the batch as <c>Seed</c> type; when absent, <c>System</c>.</param>
/// <param name="DownloadUrl">Optional direct, fetchable URL to the raw source file, used by the auto-update mechanism to refresh the file. Resolved at manifest-parse time — never persisted.</param>
public record SeedFile(string FilePath, string? Url, string? DownloadUrl = null);
