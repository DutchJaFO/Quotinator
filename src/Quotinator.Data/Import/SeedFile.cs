namespace Quotinator.Data.Import;

/// <summary>A single source file within a <see cref="SeedBatch"/>, with optional provenance URL.</summary>
/// <param name="FilePath">Absolute path to the JSON source file.</param>
/// <param name="Url">Optional URL identifying where the file was sourced from. When set, the seeder records the batch as <c>Seed</c> type; when absent, <c>System</c>.</param>
public record SeedFile(string FilePath, string? Url);
