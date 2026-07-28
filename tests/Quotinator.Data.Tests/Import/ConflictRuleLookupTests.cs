using System.Text.Json;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class ConflictRuleLookupTests
{
    private static readonly JsonElement EmptyRecord = JsonSerializer.Deserialize<JsonElement>("{}");

    private static JsonElement Record(string json) => JsonSerializer.Deserialize<JsonElement>(json);

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

        var found = lookup.TryResolve("abc123", "date", null, null, out var decision, out _);

        Assert.IsTrue(found);
        Assert.AreEqual(FieldResolutionChoice.Keep, decision.Choice);
    }

    [TestMethod]
    public void TryResolve_EntityIdDiffersOnlyByCase_StillMatches()
    {
        var lookup = new ConflictRuleLookup([BuildRule("ABC123", "date", FieldResolutionChoice.Keep)]);

        var found = lookup.TryResolve("abc123", "date", null, null, out _, out _);

        Assert.IsTrue(found, "Entity id matching must be case-insensitive, per this project's id-comparison convention");
    }

    [TestMethod]
    public void TryResolve_NoMatchingRule_ReturnsFalse()
    {
        var lookup = new ConflictRuleLookup([BuildRule("abc123", "date", FieldResolutionChoice.Keep)]);

        Assert.IsFalse(lookup.TryResolve("abc123", "type", null, null, out _, out _), "A rule for a different field must not match");
        Assert.IsFalse(lookup.TryResolve("xyz789", "date", null, null, out _, out _), "A rule for a different entity id must not match");
    }

    [TestMethod]
    public void Empty_TryResolve_AlwaysReturnsFalse()
        => Assert.IsFalse(ConflictRuleLookup.Empty.TryResolve("abc123", "date", null, null, out _, out _));

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

        Assert.IsTrue(lookup.TryResolve("abc123", "date", null, null, out var dateDecision, out _));
        Assert.AreEqual(FieldResolutionChoice.Keep, dateDecision.Choice);
        Assert.IsTrue(lookup.TryResolve("abc123", "type", null, null, out var typeDecision, out _));
        Assert.AreEqual(FieldResolutionChoice.Replace, typeDecision.Choice);
    }

    [TestMethod]
    public void TryResolve_CustomResolution_CarriesCustomValue()
    {
        var lookup = new ConflictRuleLookup([BuildRule("abc123", "character", FieldResolutionChoice.Custom, customValue: "Galadriel")]);

        var found = lookup.TryResolve("abc123", "character", null, null, out var decision, out _);

        Assert.IsTrue(found);
        Assert.AreEqual(FieldResolutionChoice.Custom, decision.Choice);
        Assert.AreEqual("Galadriel", decision.CustomValue);
    }

    // ── #153: staleness detection ──────────────────────────────────────────────────────────

    [TestMethod]
    public void TryResolve_CurrentValuesMatchRecordedSnapshot_NotStale()
    {
        var rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"date":"1980"}"""),
            IncomingRecord = Record("""{"date":null}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        var lookup = new ConflictRuleLookup([rule]);

        var found = lookup.TryResolve("abc123", "date", "1980", null, out _, out var isStale);

        Assert.IsTrue(found);
        Assert.IsFalse(isStale, "Current values matching the recorded snapshot exactly must not be stale");
    }

    [TestMethod]
    public void TryResolve_CurrentExistingValueDiffersFromRecordedSnapshot_IsStale()
    {
        var rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"date":"1980"}"""),
            IncomingRecord = Record("""{"date":null}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        var lookup = new ConflictRuleLookup([rule]);

        var found = lookup.TryResolve("abc123", "date", "1990", null, out _, out var isStale);

        Assert.IsTrue(found, "A stale rule still matches — the caller decides whether to trust it");
        Assert.IsTrue(isStale, "The existing side's real value moved since the rule was authored");
    }

    [TestMethod]
    public void TryResolve_CurrentIncomingValueDiffersFromRecordedSnapshot_IsStale()
    {
        var rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"date":"1980"}"""),
            IncomingRecord = Record("""{"date":null}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        var lookup = new ConflictRuleLookup([rule]);

        var found = lookup.TryResolve("abc123", "date", "1980", "1975", out _, out var isStale);

        Assert.IsTrue(found);
        Assert.IsTrue(isStale, "The incoming side's real value moved since the rule was authored");
    }

    [TestMethod]
    public void TryResolve_GovernedFieldMissingFromRecordedSnapshot_IsStale()
    {
        // Simulates a rule authored before this field existed in the schema, or a malformed rule —
        // a rule can only be trusted when both sides were actually recorded.
        var rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = EmptyRecord,
            IncomingRecord = EmptyRecord,
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        var lookup = new ConflictRuleLookup([rule]);

        var found = lookup.TryResolve("abc123", "date", "1980", null, out _, out var isStale);

        Assert.IsTrue(found);
        Assert.IsTrue(isStale, "A field absent from the recorded snapshot can never be confirmed fresh");
    }

    [TestMethod]
    public void TryResolve_RecordedValueDiffersOnlyByCase_NotStale()
    {
        var rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"source":"Star Wars"}"""),
            IncomingRecord = Record("""{"source":"Star Wars"}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "source", Resolution = FieldResolutionChoice.Keep }],
        };
        var lookup = new ConflictRuleLookup([rule]);

        var found = lookup.TryResolve("abc123", "source", "star wars", "star wars", out _, out var isStale);

        Assert.IsTrue(found);
        Assert.IsFalse(isStale, "A casing-only difference must never be treated as staleness, matching this project's case-insensitive-by-default convention");
    }

    [TestMethod]
    public void TryResolve_RecordedListValueMatchesCurrentSequence_NotStale()
    {
        var rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"genres":["drama","sci-fi"]}"""),
            IncomingRecord = Record("""{"genres":[]}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "genres", Resolution = FieldResolutionChoice.Keep }],
        };
        var lookup = new ConflictRuleLookup([rule]);

        var found = lookup.TryResolve("abc123", "genres", new List<string> { "drama", "sci-fi" }, new List<string>(), out _, out var isStale);

        Assert.IsTrue(found);
        Assert.IsFalse(isStale);
    }

    [TestMethod]
    public void TryResolve_RecordedListValueDiffersFromCurrentSequence_IsStale()
    {
        var rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"genres":["drama"]}"""),
            IncomingRecord = Record("""{"genres":[]}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "genres", Resolution = FieldResolutionChoice.Keep }],
        };
        var lookup = new ConflictRuleLookup([rule]);

        var found = lookup.TryResolve("abc123", "genres", new List<string> { "drama", "sci-fi" }, new List<string>(), out _, out var isStale);

        Assert.IsTrue(found);
        Assert.IsTrue(isStale, "A genres list that has grown since the rule was authored must be treated as stale");
    }
}
