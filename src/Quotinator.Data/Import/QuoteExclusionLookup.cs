namespace Quotinator.Data.Import;

/// <summary>
/// #219: fast lookup over a loaded <see cref="QuoteExclusionRuleFileDto"/>'s exclusions, keyed by quote
/// id — case-insensitive, per this project's id-comparison convention. Consulted at the very start of
/// <c>ImportActionPlanner.PlanAsync</c>, before any resolution runs, so an excluded quote produces no
/// action at all — not a decision to make, simply never imported.
/// </summary>
/// <param name="exclusions">Every exclusion this lookup is built from.</param>
public sealed class QuoteExclusionLookup(IEnumerable<QuoteExclusionRule> exclusions)
{
    /// <summary>A lookup with no exclusions — every <see cref="Contains"/> call returns <see langword="false"/>.</summary>
    public static readonly QuoteExclusionLookup Empty = new([]);

    private readonly HashSet<string> _excludedIds = new HashSet<string>(exclusions.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns <see langword="true"/> when <paramref name="quoteId"/> is excluded and must not be imported.</summary>
    public bool Contains(string quoteId) => _excludedIds.Contains(quoteId);
}
