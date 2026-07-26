using Quotinator.Data.Import;
using Quotinator.Data.Paths;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="IRuleFileOverridePathResolver"/> for use in tests that do not exercise the rule-file override feature (#153) — resolves to a path under the system temp directory that never actually exists, so callers fall back to the bundled file every time.</summary>
public sealed class NoOpRuleFileOverridePathResolver : IRuleFileOverridePathResolver
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpRuleFileOverridePathResolver Instance = new();

    /// <inheritdoc/>
    public string Resolve(string fileName, SeedBatchOrigin origin)
        => Path.Combine(Path.GetTempPath(), "quotinator-noop-rule-override", origin.ToString(), fileName);
}
