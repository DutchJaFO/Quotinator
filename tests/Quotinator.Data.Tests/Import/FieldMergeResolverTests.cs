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
        var existing = new Dictionary<string, object?>();
        var incoming = new Dictionary<string, object?>();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => FieldMergeResolver.Resolve(existing, incoming, policy));
    }

    [TestMethod]
    [DataRow(DuplicateResolutionPolicy.MergeOurs)]
    [DataRow(DuplicateResolutionPolicy.MergeTheirs)]
    public void Resolve_ExistingBlankIncomingSet_AutoFillsFromIncoming(DuplicateResolutionPolicy policy)
    {
        var existing = new Dictionary<string, object?> { ["date"] = null };
        var incoming = new Dictionary<string, object?> { ["date"] = "1994" };

        var result = FieldMergeResolver.Resolve(existing, incoming, policy);

        Assert.AreEqual("1994", result.MergedFields["date"]);
        CollectionAssert.Contains(result.FieldsFromIncoming.ToList(), "date");
    }

    [TestMethod]
    [DataRow(DuplicateResolutionPolicy.MergeOurs)]
    [DataRow(DuplicateResolutionPolicy.MergeTheirs)]
    public void Resolve_ExistingSetIncomingBlank_KeepsExisting(DuplicateResolutionPolicy policy)
    {
        var existing = new Dictionary<string, object?> { ["date"] = "1994" };
        var incoming = new Dictionary<string, object?> { ["date"] = "" };

        var result = FieldMergeResolver.Resolve(existing, incoming, policy);

        Assert.AreEqual("1994", result.MergedFields["date"]);
        CollectionAssert.DoesNotContain(result.FieldsFromIncoming.ToList(), "date");
    }

    [TestMethod]
    public void Resolve_TrueConflictScalarField_MergeOursKeepsExisting()
    {
        var existing = new Dictionary<string, object?> { ["date"] = "1994" };
        var incoming = new Dictionary<string, object?> { ["date"] = "1995" };

        var result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeOurs);

        Assert.AreEqual("1994", result.MergedFields["date"]);
        CollectionAssert.DoesNotContain(result.FieldsFromIncoming.ToList(), "date");
    }

    [TestMethod]
    public void Resolve_TrueConflictScalarField_MergeTheirsTakesIncoming()
    {
        var existing = new Dictionary<string, object?> { ["date"] = "1994" };
        var incoming = new Dictionary<string, object?> { ["date"] = "1995" };

        var result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeTheirs);

        Assert.AreEqual("1995", result.MergedFields["date"]);
        CollectionAssert.Contains(result.FieldsFromIncoming.ToList(), "date");
    }

    [TestMethod]
    public void Resolve_TrueConflictArrayField_MergeOursKeepsExistingWholesaleNoUnion()
    {
        var existing = new Dictionary<string, object?> { ["genres"] = new List<string> { "drama" } };
        var incoming = new Dictionary<string, object?> { ["genres"] = new List<string> { "drama", "thriller" } };

        var result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeOurs);

        CollectionAssert.AreEqual(new[] { "drama" }, ((List<string>)result.MergedFields["genres"]!));
    }

    [TestMethod]
    public void Resolve_TrueConflictArrayField_MergeTheirsTakesIncomingWholesaleNoUnion()
    {
        var existing = new Dictionary<string, object?> { ["genres"] = new List<string> { "drama" } };
        var incoming = new Dictionary<string, object?> { ["genres"] = new List<string> { "drama", "thriller" } };

        var result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeTheirs);

        CollectionAssert.AreEqual(new[] { "drama", "thriller" }, ((List<string>)result.MergedFields["genres"]!));
    }

    [TestMethod]
    public void Resolve_EmptyArrayFieldAutoFillsFromNonEmptySide()
    {
        var existing = new Dictionary<string, object?> { ["genres"] = new List<string>() };
        var incoming = new Dictionary<string, object?> { ["genres"] = new List<string> { "drama" } };

        var result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeOurs);

        CollectionAssert.AreEqual(new[] { "drama" }, ((List<string>)result.MergedFields["genres"]!));
        CollectionAssert.Contains(result.FieldsFromIncoming.ToList(), "genres");
    }

    [TestMethod]
    public void Resolve_EqualValues_NotRecordedAsFromIncoming()
    {
        var existing = new Dictionary<string, object?> { ["genres"] = new List<string> { "drama" } };
        var incoming = new Dictionary<string, object?> { ["genres"] = new List<string> { "drama" } };

        var result = FieldMergeResolver.Resolve(existing, incoming, DuplicateResolutionPolicy.MergeTheirs);

        CollectionAssert.DoesNotContain(result.FieldsFromIncoming.ToList(), "genres");
    }

    // ── ResolveWithDecisions (#149) ──────────────────────────────────────────

    [TestMethod]
    public void ResolveWithDecisions_UnambiguousFieldNoDecision_AutoResolvesEmptySideWins()
    {
        var existing = new Dictionary<string, object?> { ["date"] = null };
        var incoming = new Dictionary<string, object?> { ["date"] = "1994" };

        var result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, new Dictionary<string, FieldMergeDecision>());

        Assert.AreEqual("1994", result.MergedFields["date"]);
        CollectionAssert.Contains(result.FieldsFromIncoming.ToList(), "date");
    }

    [TestMethod]
    public void ResolveWithDecisions_UnambiguousFieldNoDecision_EqualValuesKeepExisting()
    {
        var existing = new Dictionary<string, object?> { ["date"] = "1994" };
        var incoming = new Dictionary<string, object?> { ["date"] = "1994" };

        var result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, new Dictionary<string, FieldMergeDecision>());

        Assert.AreEqual("1994", result.MergedFields["date"]);
        CollectionAssert.DoesNotContain(result.FieldsFromIncoming.ToList(), "date");
    }

    [TestMethod]
    public void ResolveWithDecisions_AmbiguousFieldNoDecision_ThrowsWithFieldName()
    {
        var existing = new Dictionary<string, object?> { ["date"] = "1994", ["source"] = "A" };
        var incoming = new Dictionary<string, object?> { ["date"] = "1995", ["source"] = "A" };

        var ex = Assert.ThrowsExactly<UnresolvedFieldConflictException>(
            () => FieldMergeResolver.ResolveWithDecisions(existing, incoming, new Dictionary<string, FieldMergeDecision>()));

        CollectionAssert.AreEqual(new[] { "date" }, ex.FieldNames.ToList());
    }

    [TestMethod]
    public void ResolveWithDecisions_AmbiguousFieldsNoDecision_ThrowsWithEveryAmbiguousFieldName()
    {
        var existing = new Dictionary<string, object?> { ["date"] = "1994", ["character"] = "Bob" };
        var incoming = new Dictionary<string, object?> { ["date"] = "1995", ["character"] = "Alice" };

        var ex = Assert.ThrowsExactly<UnresolvedFieldConflictException>(
            () => FieldMergeResolver.ResolveWithDecisions(existing, incoming, new Dictionary<string, FieldMergeDecision>()));

        CollectionAssert.AreEquivalent(new[] { "date", "character" }, ex.FieldNames.ToList());
    }

    [TestMethod]
    public void ResolveWithDecisions_KeepDecision_AlwaysKeepsExistingEvenWhenUnambiguous()
    {
        var existing = new Dictionary<string, object?> { ["date"] = "1994" };
        var incoming = new Dictionary<string, object?> { ["date"] = "" };
        var decisions = new Dictionary<string, FieldMergeDecision> { ["date"] = new(FieldResolutionChoice.Keep, null) };

        var result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, decisions);

        Assert.AreEqual("1994", result.MergedFields["date"]);
        CollectionAssert.DoesNotContain(result.FieldsFromIncoming.ToList(), "date");
    }

    [TestMethod]
    public void ResolveWithDecisions_ReplaceDecision_AlwaysTakesIncomingEvenForAmbiguousField()
    {
        var existing = new Dictionary<string, object?> { ["date"] = "1994" };
        var incoming = new Dictionary<string, object?> { ["date"] = "1995" };
        var decisions = new Dictionary<string, FieldMergeDecision> { ["date"] = new(FieldResolutionChoice.Replace, null) };

        var result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, decisions);

        Assert.AreEqual("1995", result.MergedFields["date"]);
        CollectionAssert.Contains(result.FieldsFromIncoming.ToList(), "date");
    }

    [TestMethod]
    public void ResolveWithDecisions_CustomDecision_UsesCallerSuppliedValueOverridingBothSides()
    {
        var existing = new Dictionary<string, object?> { ["genres"] = new List<string> { "drama" } };
        var incoming = new Dictionary<string, object?> { ["genres"] = new List<string> { "thriller" } };
        var custom   = new List<string> { "drama", "thriller", "mystery" };
        var decisions = new Dictionary<string, FieldMergeDecision> { ["genres"] = new(FieldResolutionChoice.Custom, custom) };

        var result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, decisions);

        CollectionAssert.AreEqual(custom, (List<string>)result.MergedFields["genres"]!);
        CollectionAssert.Contains(result.FieldsFromIncoming.ToList(), "genres");
    }

    [TestMethod]
    public void ResolveWithDecisions_MixOfDecidedAndAutoResolvedFields_BothApplyCorrectly()
    {
        var existing = new Dictionary<string, object?> { ["date"] = "1994", ["character"] = null };
        var incoming = new Dictionary<string, object?> { ["date"] = "1995", ["character"] = "Alice" };
        var decisions = new Dictionary<string, FieldMergeDecision> { ["date"] = new(FieldResolutionChoice.Replace, null) };

        var result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, decisions);

        Assert.AreEqual("1995", result.MergedFields["date"]);
        Assert.AreEqual("Alice", result.MergedFields["character"]);
    }
}
