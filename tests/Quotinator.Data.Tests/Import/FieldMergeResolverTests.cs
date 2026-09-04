using Quotinator.Data.Enums;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class FieldMergeResolverTests
{
    [TestMethod]
    [DataRow(DuplicateResolutionPolicy.Skip)]
    [DataRow(DuplicateResolutionPolicy.NewestWins)]
    [DataRow(DuplicateResolutionPolicy.Review)]
    public void Resolve_UnsupportedPolicy_Throws(DuplicateResolutionPolicy policy)
    {
        Dictionary<string, object?> existing = [];
        Dictionary<string, object?> incoming = [];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => FieldMergeResolver.Resolve(existing, incoming, policy));
    }

    [TestMethod]
    [DataRow(DuplicateResolutionPolicy.MergeOurs)]
    [DataRow(DuplicateResolutionPolicy.MergeTheirs)]
    public void Resolve_ExistingBlankIncomingSet_AutoFillsFromIncoming(DuplicateResolutionPolicy policy)
    {
        Dictionary<string, object?> existing = new() { ["date"] = null };
        Dictionary<string, object?> incoming = new() { ["date"] = "1994" };

        FieldMergeResult result = FieldMergeResolver.Resolve(existing, incoming, policy);

        Assert.AreEqual("1994", result.MergedFields["date"]);
        Assert.Contains("date", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    [DataRow(DuplicateResolutionPolicy.MergeOurs)]
    [DataRow(DuplicateResolutionPolicy.MergeTheirs)]
    public void Resolve_ExistingSetIncomingBlank_KeepsExisting(DuplicateResolutionPolicy policy)
    {
        Dictionary<string, object?> existing = new() { ["date"] = "1994" };
        Dictionary<string, object?> incoming = new() { ["date"] = "" };

        FieldMergeResult result = FieldMergeResolver.Resolve(existing, incoming, policy);

        Assert.AreEqual("1994", result.MergedFields["date"]);
        Assert.DoesNotContain("date", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    public void Resolve_TrueConflictScalarField_MergeOursKeepsExisting()
    {
        Dictionary<string, object?> existing = new() { ["date"] = "1994" };
        Dictionary<string, object?> incoming = new() { ["date"] = "1995" };

        FieldMergeResult result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeOurs);

        Assert.AreEqual("1994", result.MergedFields["date"]);
        Assert.DoesNotContain("date", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    public void Resolve_TrueConflictScalarField_MergeTheirsTakesIncoming()
    {
        Dictionary<string, object?> existing = new() { ["date"] = "1994" };
        Dictionary<string, object?> incoming = new() { ["date"] = "1995" };

        FieldMergeResult result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeTheirs);

        Assert.AreEqual("1995", result.MergedFields["date"]);
        Assert.Contains("date", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    public void Resolve_TrueConflictArrayField_MergeOursKeepsExistingWholesaleNoUnion()
    {
        Dictionary<string, object?> existing = new() { ["genres"] = new List<string> { "drama" } };
        Dictionary<string, object?> incoming = new() { ["genres"] = new List<string> { "drama", "thriller" } };

        FieldMergeResult result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeOurs);

        Assert.AreSequenceEqual(["drama"], ((List<string>)result.MergedFields["genres"]!));
    }

    [TestMethod]
    public void Resolve_TrueConflictArrayField_MergeTheirsTakesIncomingWholesaleNoUnion()
    {
        Dictionary<string, object?> existing = new() { ["genres"] = new List<string> { "drama" } };
        Dictionary<string, object?> incoming = new() { ["genres"] = new List<string> { "drama", "thriller" } };

        FieldMergeResult result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeTheirs);

        Assert.AreSequenceEqual(["drama", "thriller"], ((List<string>)result.MergedFields["genres"]!));
    }

    [TestMethod]
    public void Resolve_EmptyArrayFieldAutoFillsFromNonEmptySide()
    {
        Dictionary<string, object?> existing = new() { ["genres"] = new List<string>() };
        Dictionary<string, object?> incoming = new() { ["genres"] = new List<string> { "drama" } };

        FieldMergeResult result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeOurs);

        Assert.AreSequenceEqual(["drama"], ((List<string>)result.MergedFields["genres"]!));
        Assert.Contains("genres", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    public void Resolve_EqualValues_NotRecordedAsFromIncoming()
    {
        Dictionary<string, object?> existing = new() { ["genres"] = new List<string> { "drama" } };
        Dictionary<string, object?> incoming = new() { ["genres"] = new List<string> { "drama" } };

        FieldMergeResult result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeTheirs);

        Assert.DoesNotContain("genres", [.. result.FieldsFromIncoming]);
    }

    // ── Case-insensitive value comparison ────────────────────────────────────

    [TestMethod]
    [DataRow("Star Wars", "star wars")]
    [DataRow("Luke", "luke")]
    [DataRow("THE SIMPSONS MOVIE", "the simpsons movie")]
    public void ValuesEqual_ScalarStringsDifferOnlyByCase_ReturnsTrue(string a, string b)
        => Assert.IsTrue(FieldMergeResolver.ValuesEqual(a, b));

    [TestMethod]
    public void ValuesEqual_ScalarStringsGenuinelyDiffer_ReturnsFalse()
        => Assert.IsFalse(FieldMergeResolver.ValuesEqual("Star Wars", "Star Trek"));

    [TestMethod]
    public void ValuesEqual_ArrayOfStringsDifferOnlyByCase_ReturnsTrue()
        => Assert.IsTrue(FieldMergeResolver.ValuesEqual(new List<string> { "Drama", "Sci-Fi" }, new List<string> { "drama", "sci-fi" }));

    /// <summary>
    /// Genre order carries no meaning anywhere in this project's schema, UI, or storage — a list-valued
    /// field is a set, not a sequence, so two lists holding the same elements in a different order must
    /// compare equal. Found live (2026-09-04): <c>quotinator-curated.json</c> genuinely stores
    /// <c>["sci-fi", "action"]</c> for one quote, and <c>Sql.QuoteGenres.LoadForQuote</c> has no
    /// <c>ORDER BY</c>, so SQLite's returned row order for a quote's stored genres is unspecified by the
    /// SQL standard and not guaranteed to match the order the source file lists them in. Before this
    /// fix, that mismatch made an unchanged quote compare as Modified purely because of row-return
    /// order — nothing about its actual content had changed.
    /// </summary>
    [TestMethod]
    public void ValuesEqual_ArrayOfStringsSameElementsDifferentOrder_ReturnsTrue()
        => Assert.IsTrue(FieldMergeResolver.ValuesEqual(new List<string> { "sci-fi", "action" }, new List<string> { "action", "sci-fi" }));

    [TestMethod]
    public void ValuesEqual_ArrayOfStringsGenuinelyDiffer_ReturnsFalse()
        => Assert.IsFalse(FieldMergeResolver.ValuesEqual(new List<string> { "action", "comedy" }, new List<string> { "action", "drama" }));

    [TestMethod]
    public void Resolve_ScalarStringsDifferOnlyByCase_TreatedAsEqual_KeepsExistingCasing()
    {
        Dictionary<string, object?> existing = new() { ["source"] = "The Simpsons Movie" };
        Dictionary<string, object?> incoming = new() { ["source"] = "the simpsons movie" };

        FieldMergeResult result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeTheirs);

        Assert.AreEqual("The Simpsons Movie", result.MergedFields["source"], "A casing-only difference is not a true conflict — the existing side's casing is kept, not silently replaced");
        Assert.DoesNotContain("source", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    public void ResolveWithDecisions_FieldsDifferOnlyByCase_NotTreatedAsAmbiguous()
    {
        Dictionary<string, object?> existing = new() { ["source"] = "Star Wars" };
        Dictionary<string, object?> incoming = new() { ["source"] = "star wars" };

        FieldMergeResult result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, new Dictionary<string, FieldMergeDecision>());

        Assert.AreEqual("Star Wars", result.MergedFields["source"]);
    }

    // ── Per-field case-sensitive override (#374) ─────────────────────────────
    //
    // Found live (T2 Docker, 2026-09-04): a case-only difference on a quote's own quoteText/source/
    // character was silently treated as "equal, keep existing" by every path above — the same
    // case-insensitive-by-default rule these three fields have always used. That silence is what let
    // vilaboim's "Gone with the Wind" vs the stored "Gone With the Wind" resolve to keep-existing on
    // every single reseed, forever, with nobody ever told a source disagreed on casing. Developer
    // decision (2026-09-04): a case-only difference on quote/title/character content is genuinely
    // ambiguous — it could be a correction (an upstream typo finally fixed) or an unwanted downgrade
    // (a lower-quality source overwriting a curated correction), and only a human can tell which. An
    // unstable result across repeated reseeds is itself proof the existing rule was wrong, not proof
    // the data is flaky. These four fields (default case-insensitive, per the existing tests above)
    // are unaffected — this is an opt-in per caller, not a global reversal of the case-insensitive-by-
    // default convention; the caller (`Quotinator.Core`'s `QuoteFieldMerge.CaseSensitiveContentFields`)
    // decides which of its own fields need this, since `FieldMergeResolver` itself stays domain-agnostic
    // (ADR 004) and never hardcodes a quote-specific field name.

    [TestMethod]
    public void ValuesEqual_FieldInCaseSensitiveSet_DiffersOnlyByCase_ReturnsFalse()
        => Assert.IsFalse(FieldMergeResolver.ValuesEqual("source", "Gone With the Wind", "Gone with the Wind", new HashSet<string> { "source" }));

    [TestMethod]
    public void ValuesEqual_FieldNotInCaseSensitiveSet_DiffersOnlyByCase_ReturnsTrue()
        => Assert.IsTrue(FieldMergeResolver.ValuesEqual("date", "1994", "1994", new HashSet<string> { "source" }));

    [TestMethod]
    public void ValuesEqual_FieldInCaseSensitiveSet_ExactMatch_ReturnsTrue()
        => Assert.IsTrue(FieldMergeResolver.ValuesEqual("source", "Gone With the Wind", "Gone With the Wind", new HashSet<string> { "source" }));

    [TestMethod]
    public void ValuesEqual_FieldInCaseSensitiveSet_OneSideNull_ReturnsFalse()
        => Assert.IsFalse(FieldMergeResolver.ValuesEqual("source", "Gone With the Wind", null, new HashSet<string> { "source" }));

    [TestMethod]
    public void Resolve_FieldInCaseSensitiveSet_DiffersOnlyByCase_MergeTheirsTakesIncomingAsGenuineConflict()
    {
        Dictionary<string, object?> existing = new() { ["source"] = "Star Wars" };
        Dictionary<string, object?> incoming = new() { ["source"] = "star wars" };

        FieldMergeResult result = FieldMergeResolver.Resolve(
            existing, incoming, DuplicateResolutionPolicy.MergeTheirs, new HashSet<string> { "source" });

        Assert.AreEqual("star wars", result.MergedFields["source"],
            "A case-only difference on a case-sensitive field is a true conflict — the tie-break policy decides, not silent case-folding");
        Assert.Contains("source", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    public void ResolveWithDecisions_FieldInCaseSensitiveSet_DiffersOnlyByCase_ThrowsWithFieldName()
    {
        Dictionary<string, object?> existing = new() { ["source"] = "Gone With the Wind" };
        Dictionary<string, object?> incoming = new() { ["source"] = "Gone with the Wind" };

        UnresolvedFieldConflictException ex = Assert.ThrowsExactly<UnresolvedFieldConflictException>(() =>
            FieldMergeResolver.ResolveWithDecisions(
                existing, incoming, new Dictionary<string, FieldMergeDecision>(), new HashSet<string> { "source" }));

        Assert.AreSequenceEqual(["source"], [.. ex.FieldNames]);
    }

    [TestMethod]
    public void ResolveWithDecisions_FieldInCaseSensitiveSet_WithExplicitDecision_StillResolves()
    {
        Dictionary<string, object?> existing = new() { ["source"] = "Gone With the Wind" };
        Dictionary<string, object?> incoming = new() { ["source"] = "Gone with the Wind" };
        Dictionary<string, FieldMergeDecision> decisions = new() { ["source"] = new FieldMergeDecision(FieldResolutionChoice.Replace, null) };

        FieldMergeResult result = FieldMergeResolver.ResolveWithDecisions(
            existing, incoming, decisions, new HashSet<string> { "source" });

        Assert.AreEqual("Gone with the Wind", result.MergedFields["source"],
            "An explicit decision always wins regardless of case-sensitivity — a curator's own resolution is not second-guessed");
    }

    [TestMethod]
    public void ResolveWithDecisions_FieldInCaseSensitiveSet_ExactMatch_AutoResolvesNoDecisionNeeded()
    {
        Dictionary<string, object?> existing = new() { ["source"] = "Gone With the Wind" };
        Dictionary<string, object?> incoming = new() { ["source"] = "Gone With the Wind" };

        FieldMergeResult result = FieldMergeResolver.ResolveWithDecisions(
            existing, incoming, new Dictionary<string, FieldMergeDecision>(), new HashSet<string> { "source" });

        Assert.AreEqual("Gone With the Wind", result.MergedFields["source"]);
    }

    // ── ResolveWithDecisions (#149) ──────────────────────────────────────────

    [TestMethod]
    public void ResolveWithDecisions_UnambiguousFieldNoDecision_AutoResolvesEmptySideWins()
    {
        Dictionary<string, object?> existing = new() { ["date"] = null };
        Dictionary<string, object?> incoming = new() { ["date"] = "1994" };

        FieldMergeResult result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, new Dictionary<string, FieldMergeDecision>());

        Assert.AreEqual("1994", result.MergedFields["date"]);
        Assert.Contains("date", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    public void ResolveWithDecisions_UnambiguousFieldNoDecision_EqualValuesKeepExisting()
    {
        Dictionary<string, object?> existing = new() { ["date"] = "1994" };
        Dictionary<string, object?> incoming = new() { ["date"] = "1994" };

        FieldMergeResult result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, new Dictionary<string, FieldMergeDecision>());

        Assert.AreEqual("1994", result.MergedFields["date"]);
        Assert.DoesNotContain("date", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    public void ResolveWithDecisions_AmbiguousFieldNoDecision_ThrowsWithFieldName()
    {
        Dictionary<string, object?> existing = new() { ["date"] = "1994", ["source"] = "A" };
        Dictionary<string, object?> incoming = new() { ["date"] = "1995", ["source"] = "A" };

        UnresolvedFieldConflictException ex = Assert.ThrowsExactly<UnresolvedFieldConflictException>(
            () => FieldMergeResolver.ResolveWithDecisions(existing, incoming, new Dictionary<string, FieldMergeDecision>()));

        Assert.AreSequenceEqual(["date"], [.. ex.FieldNames]);
    }

    [TestMethod]
    public void ResolveWithDecisions_AmbiguousFieldsNoDecision_ThrowsWithEveryAmbiguousFieldName()
    {
        Dictionary<string, object?> existing = new() { ["date"] = "1994", ["character"] = "Bob" };
        Dictionary<string, object?> incoming = new() { ["date"] = "1995", ["character"] = "Alice" };

        UnresolvedFieldConflictException ex = Assert.ThrowsExactly<UnresolvedFieldConflictException>(
            () => FieldMergeResolver.ResolveWithDecisions(existing, incoming, new Dictionary<string, FieldMergeDecision>()));

        Assert.AreSequenceEqual(["date", "character"], [.. ex.FieldNames], Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public void ResolveWithDecisions_KeepDecision_AlwaysKeepsExistingEvenWhenUnambiguous()
    {
        Dictionary<string, object?> existing = new() { ["date"] = "1994" };
        Dictionary<string, object?> incoming = new() { ["date"] = "" };
        Dictionary<string, FieldMergeDecision> decisions = new() { ["date"] = new(FieldResolutionChoice.Keep, null) };

        FieldMergeResult result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, decisions);

        Assert.AreEqual("1994", result.MergedFields["date"]);
        Assert.DoesNotContain("date", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    public void ResolveWithDecisions_ReplaceDecision_AlwaysTakesIncomingEvenForAmbiguousField()
    {
        Dictionary<string, object?> existing = new() { ["date"] = "1994" };
        Dictionary<string, object?> incoming = new() { ["date"] = "1995" };
        Dictionary<string, FieldMergeDecision> decisions = new() { ["date"] = new(FieldResolutionChoice.Replace, null) };

        FieldMergeResult result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, decisions);

        Assert.AreEqual("1995", result.MergedFields["date"]);
        Assert.Contains("date", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    public void ResolveWithDecisions_CustomDecision_UsesCallerSuppliedValueOverridingBothSides()
    {
        Dictionary<string, object?> existing = new() { ["genres"] = new List<string> { "drama" } };
        Dictionary<string, object?> incoming = new() { ["genres"] = new List<string> { "thriller" } };
        List<string> custom   = ["drama", "thriller", "mystery"];
        Dictionary<string, FieldMergeDecision> decisions = new() { ["genres"] = new(FieldResolutionChoice.Custom, custom) };

        FieldMergeResult result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, decisions);

        Assert.AreSequenceEqual(custom, (List<string>)result.MergedFields["genres"]!);
        Assert.Contains("genres", [.. result.FieldsFromIncoming]);
    }

    [TestMethod]
    public void ResolveWithDecisions_MixOfDecidedAndAutoResolvedFields_BothApplyCorrectly()
    {
        Dictionary<string, object?> existing = new() { ["date"] = "1994", ["character"] = null };
        Dictionary<string, object?> incoming = new() { ["date"] = "1995", ["character"] = "Alice" };
        Dictionary<string, FieldMergeDecision> decisions = new() { ["date"] = new(FieldResolutionChoice.Replace, null) };

        FieldMergeResult result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, decisions);

        Assert.AreEqual("1995", result.MergedFields["date"]);
        Assert.AreEqual("Alice", result.MergedFields["character"]);
    }
}
