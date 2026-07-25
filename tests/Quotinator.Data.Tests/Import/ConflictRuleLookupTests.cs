using System.Text.Json;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class ConflictRuleLookupTests
{
    private static readonly JsonElement EmptyRecord = JsonSerializer.Deserialize<JsonElement>("{}");

    private static ConflictResolutionRule BuildRule(string entityId, string field, FieldResolutionChoice resolution, string? customValue = null) => new()
    {
        EntityId = entityId,
        ExistingRecord = EmptyRecord,
        IncomingRecord = EmptyRecord,
        Fields = [new ConflictResolutionFieldRule { Field = field, Resolution = resolution, CustomValue = customValue }],
    };

    [TestMethod]
    public void TryResolve_MatchingEntityIdAndField_ReturnsTrueWithResolution()
    {
        var lookup = new ConflictRuleLookup([BuildRule("abc123", "date", FieldResolutionChoice.Keep)]);

        var found = lookup.TryResolve("abc123", "date", out var decision);

        Assert.IsTrue(found);
        Assert.AreEqual(FieldResolutionChoice.Keep, decision.Choice);
    }

    [TestMethod]
    public void TryResolve_EntityIdDiffersOnlyByCase_StillMatches()
    {
        var lookup = new ConflictRuleLookup([BuildRule("ABC123", "date", FieldResolutionChoice.Keep)]);

        var found = lookup.TryResolve("abc123", "date", out _);

        Assert.IsTrue(found, "Entity id matching must be case-insensitive, per this project's id-comparison convention");
    }

    [TestMethod]
    public void TryResolve_NoMatchingRule_ReturnsFalse()
    {
        var lookup = new ConflictRuleLookup([BuildRule("abc123", "date", FieldResolutionChoice.Keep)]);

        Assert.IsFalse(lookup.TryResolve("abc123", "type", out _), "A rule for a different field must not match");
        Assert.IsFalse(lookup.TryResolve("xyz789", "date", out _), "A rule for a different entity id must not match");
    }

    [TestMethod]
    public void Empty_TryResolve_AlwaysReturnsFalse()
        => Assert.IsFalse(ConflictRuleLookup.Empty.TryResolve("abc123", "date", out _));

    [TestMethod]
    public void TryResolve_EntityWithMultipleFields_EachResolvesIndependently()
    {
        var lookup = new ConflictRuleLookup([
            new ConflictResolutionRule
            {
                EntityId = "abc123",
                ExistingRecord = EmptyRecord,
                IncomingRecord = EmptyRecord,
                Fields =
                [
                    new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep },
                    new ConflictResolutionFieldRule { Field = "type", Resolution = FieldResolutionChoice.Replace },
                ],
            },
        ]);

        Assert.IsTrue(lookup.TryResolve("abc123", "date", out var dateDecision));
        Assert.AreEqual(FieldResolutionChoice.Keep, dateDecision.Choice);
        Assert.IsTrue(lookup.TryResolve("abc123", "type", out var typeDecision));
        Assert.AreEqual(FieldResolutionChoice.Replace, typeDecision.Choice);
    }

    [TestMethod]
    public void TryResolve_CustomResolution_CarriesCustomValue()
    {
        var lookup = new ConflictRuleLookup([BuildRule("abc123", "character", FieldResolutionChoice.Custom, customValue: "Galadriel")]);

        var found = lookup.TryResolve("abc123", "character", out var decision);

        Assert.IsTrue(found);
        Assert.AreEqual(FieldResolutionChoice.Custom, decision.Choice);
        Assert.AreEqual("Galadriel", decision.CustomValue);
    }
}
