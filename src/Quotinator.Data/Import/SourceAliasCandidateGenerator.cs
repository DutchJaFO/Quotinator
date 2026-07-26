using System.Text;

namespace Quotinator.Data.Import;

/// <summary>
/// One candidate pair of existing Sources that appear to refer to the same real work under
/// differently-punctuated titles (#153) — surfaced for a human to research and confirm via a
/// hand-authored <see cref="SourceAliasRule"/> entry (per
/// <c>docs/workflow/source-verification.md</c>'s verify-before-trust policy); never auto-applied.
/// </summary>
public sealed class SourceAliasCandidate
{
    /// <summary>Id of the first Source in the pair.</summary>
    public required string SourceIdA { get; init; }

    /// <summary>Title of the first Source in the pair, exactly as stored.</summary>
    public required string TitleA { get; init; }

    /// <summary>Id of the second Source in the pair.</summary>
    public required string SourceIdB { get; init; }

    /// <summary>Title of the second Source in the pair, exactly as stored.</summary>
    public required string TitleB { get; init; }

    /// <summary>The shared <c>Type</c> both Sources carry.</summary>
    public required string Type { get; init; }
}

/// <summary>
/// Scans existing Sources for near-duplicate <c>(Title, Type)</c> pairs not already covered by a
/// <see cref="SourceAliasRule"/> — detect-and-suggest only, never auto-writing an alias entry, per this
/// project's source-verification policy (a title/canonicalization claim must be checked against real
/// sources before being recorded). Two rows already differing only by case are never candidates here —
/// natural-key Source resolution is already case-insensitive (#175), so two such rows could not both
/// exist as separate Sources in the first place; this generator exists for the punctuation-level
/// duplicates that case-insensitive matching alone does not catch (a trailing "!", a curly vs. straight
/// apostrophe, doubled whitespace — the exact defect classes #181's own cleanup found live).
/// </summary>
public static class SourceAliasCandidateGenerator
{
    /// <summary>
    /// Groups <paramref name="sources"/> by normalized title (punctuation stripped, whitespace
    /// collapsed) and type (case-insensitive), then pairs up every two distinct-cased titles sharing a
    /// group — skipping any pair where either side already has a <see cref="SourceAliasRule"/> covering
    /// it (already handled; not worth re-suggesting).
    /// </summary>
    public static IReadOnlyList<SourceAliasCandidate> Generate(
        IEnumerable<(string Id, string Title, string Type)> sources,
        SourceAliasLookup existingAliases)
    {
        var candidates = new List<SourceAliasCandidate>();

        var groups = sources.GroupBy(
            s => (NormalizedTitle: Normalize(s.Title), NormalizedType: s.Type.Trim().ToUpperInvariant()));

        foreach (var group in groups)
        {
            var members = group.ToList();
            if (members.Count < 2) continue;

            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    var a = members[i];
                    var b = members[j];

                    // Already case-insensitively identical — cannot both exist as separate Source rows
                    // under #175's natural-key matching, but guard anyway rather than assume the caller
                    // only ever passes genuinely-distinct rows.
                    if (string.Equals(a.Title, b.Title, StringComparison.OrdinalIgnoreCase)) continue;

                    if (existingAliases.TryResolve(a.Title, a.Type, out _)) continue;
                    if (existingAliases.TryResolve(b.Title, b.Type, out _)) continue;

                    candidates.Add(new SourceAliasCandidate
                    {
                        SourceIdA = a.Id,
                        TitleA    = a.Title,
                        SourceIdB = b.Id,
                        TitleB    = b.Title,
                        Type      = a.Type,
                    });
                }
            }
        }

        return candidates;
    }

    /// <summary>Lowercases, strips everything but letters/digits/spaces, and collapses whitespace — punctuation-blind, case-blind title comparison.</summary>
    private static string Normalize(string title)
    {
        var builder = new StringBuilder(title.Length);
        var lastWasSpace = false;

        foreach (var c in title)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (char.IsWhiteSpace(c) && !lastWasSpace && builder.Length > 0)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }
}
