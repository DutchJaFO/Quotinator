using Quotinator.Data.Enums;
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
        ConflictRuleLookup lookup = new ConflictRuleLookup([BuildRule("abc123", "date", FieldResolutionChoice.Keep)]);

        bool found = lookup.TryResolve("abc123", "date", null, null, out FieldMergeDecision decision, out _);

        Assert.IsTrue(found);
        Assert.AreEqual(FieldResolutionChoice.Keep, decision.Choice);
    }

    [TestMethod]
    public void TryResolve_EntityIdDiffersOnlyByCase_StillMatches()
    {
        ConflictRuleLookup lookup = new ConflictRuleLookup([BuildRule("ABC123", "date", FieldResolutionChoice.Keep)]);

        bool found = lookup.TryResolve("abc123", "date", null, null, out _, out _);

        Assert.IsTrue(found, "Entity id matching must be case-insensitive, per this project's id-comparison convention");
    }

    [TestMethod]
    public void TryResolve_NoMatchingRule_ReturnsFalse()
    {
        ConflictRuleLookup lookup = new ConflictRuleLookup([BuildRule("abc123", "date", FieldResolutionChoice.Keep)]);

        Assert.IsFalse(lookup.TryResolve("abc123", "type", null, null, out _, out _), "A rule for a different field must not match");
        Assert.IsFalse(lookup.TryResolve("xyz789", "date", null, null, out _, out _), "A rule for a different entity id must not match");
    }

    [TestMethod]
    public void Empty_TryResolve_AlwaysReturnsFalse()
        => Assert.IsFalse(ConflictRuleLookup.Empty.TryResolve("abc123", "date", null, null, out _, out _));

    [TestMethod]
    public void TryResolve_EntityWithMultipleFields_EachResolvesIndependently()
    {
        ConflictRuleLookup lookup = new ConflictRuleLookup([
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

        Assert.IsTrue(lookup.TryResolve("abc123", "date", null, null, out FieldMergeDecision dateDecision, out _));
        Assert.AreEqual(FieldResolutionChoice.Keep, dateDecision.Choice);
        Assert.IsTrue(lookup.TryResolve("abc123", "type", null, null, out FieldMergeDecision typeDecision, out _));
        Assert.AreEqual(FieldResolutionChoice.Replace, typeDecision.Choice);
    }

    [TestMethod]
    public void TryResolve_CustomResolution_CarriesCustomValue()
    {
        ConflictRuleLookup lookup = new ConflictRuleLookup([BuildRule("abc123", "character", FieldResolutionChoice.Custom, customValue: "Galadriel")]);

        bool found = lookup.TryResolve("abc123", "character", null, null, out FieldMergeDecision decision, out _);

        Assert.IsTrue(found);
        Assert.AreEqual(FieldResolutionChoice.Custom, decision.Choice);
        Assert.AreEqual("Galadriel", decision.CustomValue);
    }

    // ── #153: staleness detection, incoming side only (existing side no longer read — #374) ──

    [TestMethod]
    public void TryResolve_CurrentValuesMatchRecordedSnapshot_NotStale()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"date":"1980"}"""),
            IncomingRecord = Record("""{"date":null}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        bool found = lookup.TryResolve("abc123", "date", "1980", null, out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.IsFalse(outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable,
            "Current values matching the recorded snapshot exactly must not be stale or retirable");
    }

    /// <summary>#374: renamed and reversed from `TryResolve_CurrentExistingValueDiffersFromRecordedSnapshot_IsStale`
    /// — `existingRecord` is no longer read for staleness at all (step 4), so a stored value that has
    /// drifted from what was recorded at authoring time is not, by itself, a reason to distrust the rule.</summary>
    [TestMethod]
    public void TryResolve_StoredValueDriftedFromRecordedExisting_IsNotStale()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"date":"1980"}"""),
            IncomingRecord = Record("""{"date":null}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        bool found = lookup.TryResolve("abc123", "date", "1990", null, out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found, "A rule whose existing side has drifted still matches — the caller decides whether to trust it");
        Assert.IsFalse(outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable,
            "The existing side's real value moving since the rule was authored must no longer be treated as staleness");
    }

    /// <summary>#153's incoming-side half, unchanged by #374 — renamed only, from
    /// `TryResolve_CurrentIncomingValueDiffersFromRecordedSnapshot_IsStale`.</summary>
    [TestMethod]
    public void TryResolve_IncomingValueDiffersFromRecordedSnapshot_ReportsStale()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"date":"1980"}"""),
            IncomingRecord = Record("""{"date":null}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        bool found = lookup.TryResolve("abc123", "date", "1980", "1975", out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.AreEqual(ConflictRuleOutcome.Stale, outcome, "The incoming side's real value moved since the rule was authored");
    }

    /// <summary>#374: this scenario is stale even though the stored value already equals the rule's
    /// wanted value (Keep → the current existing value, "1980") — a moved incoming side must not be
    /// masked by an already-correct target. The control for this is
    /// <see cref="TryResolve_CurrentValuesMatchRecordedSnapshot_NotStale"/>, which is identical except
    /// the incoming side has not moved.</summary>
    [TestMethod]
    public void TryResolve_IncomingMovedButOutcomeAlreadyStored_ReportsStale()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"date":"1980"}"""),
            IncomingRecord = Record("""{"date":null}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        // Existing already equals Keep's wanted value ("1980"), but the incoming side no longer
        // matches what was recorded (recorded null, now "2000") — must still be Stale.
        bool found = lookup.TryResolve("abc123", "date", "1980", "2000", out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.AreEqual(ConflictRuleOutcome.Stale, outcome,
            "An already-correct stored value must not mask a rule whose incoming side has moved");
    }

    /// <summary>#374: half of the split of `TryResolve_GovernedFieldMissingFromRecordedSnapshot_IsStale`
    /// — a field absent from the rule's own recorded <c>incomingRecord</c> can never be confirmed fresh.</summary>
    [TestMethod]
    public void TryResolve_GovernedFieldMissingFromIncomingRecord_ReportsStale()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"date":"1980"}"""),
            IncomingRecord = EmptyRecord,
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        bool found = lookup.TryResolve("abc123", "date", "1980", null, out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.AreEqual(ConflictRuleOutcome.Stale, outcome, "A field absent from the recorded incoming snapshot can never be confirmed fresh");
    }

    /// <summary>#374: the other half of the split — the visible record of the reversal. A field absent
    /// from <c>existingRecord</c> no longer makes a rule stale, since that snapshot is not read at all.</summary>
    [TestMethod]
    public void TryResolve_GovernedFieldMissingFromExistingRecord_IsNotStale()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = EmptyRecord,
            IncomingRecord = Record("""{"date":null}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        bool found = lookup.TryResolve("abc123", "date", "1980", null, out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.IsFalse(outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable,
            "A field absent only from the recorded existing snapshot must no longer be treated as staleness");
    }

    [TestMethod]
    public void TryResolve_RecordedValueDiffersOnlyByCase_NotStale()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"source":"Star Wars"}"""),
            IncomingRecord = Record("""{"source":"Star Wars"}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "source", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        bool found = lookup.TryResolve("abc123", "source", "star wars", "star wars", out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.IsFalse(outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable,
            "A casing-only difference must never be treated as staleness, matching this project's case-insensitive-by-default convention");
    }

    [TestMethod]
    public void TryResolve_RecordedListValueMatchesCurrentSequence_NotStale()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"genres":["drama","sci-fi"]}"""),
            IncomingRecord = Record("""{"genres":[]}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "genres", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        bool found = lookup.TryResolve("abc123", "genres", new List<string> { "drama", "sci-fi" }, new List<string>(), out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.IsFalse(outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable);
    }

    /// <summary>#374: renamed and reversed from `TryResolve_RecordedListValueDiffersFromCurrentSequence_IsStale`
    /// — the drift here is on the existing side only (the incoming side still matches its recorded empty
    /// list), so under the new design it is no longer stale, the list-valued analogue of
    /// <see cref="TryResolve_StoredValueDriftedFromRecordedExisting_IsNotStale"/>.</summary>
    [TestMethod]
    public void TryResolve_ListValueDriftedFromRecordedExisting_IsNotStale()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"genres":["drama"]}"""),
            IncomingRecord = Record("""{"genres":[]}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "genres", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        bool found = lookup.TryResolve("abc123", "genres", new List<string> { "drama", "sci-fi" }, new List<string>(), out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.IsFalse(outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable,
            "A genres list that has grown on the existing side only, since the rule was authored, is no longer treated as stale");
    }

    // ── #374: already-applied vs. apply — the outcome defect this issue is named for ──────────

    /// <summary>Verification row 4 — the control for row 3. Without this, a lookup that answers
    /// already-applied unconditionally would still pass row 3.</summary>
    [TestMethod]
    public void TryResolve_StoredValueDiffersFromTheRulesOutcome_ReportsApply()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"character":"Frodo"}"""),
            IncomingRecord = Record("""{"character":"Merry"}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "character", Resolution = FieldResolutionChoice.Custom, CustomValue = "Galadriel" }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        // Stored value ("Frodo") differs from the rule's wanted value (Custom → "Galadriel"); the two
        // sides disagree throughout ("Frodo" vs "Merry"), keeping this orthogonal to retirement.
        bool found = lookup.TryResolve("abc123", "character", "Frodo", "Merry", out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.AreEqual(ConflictRuleOutcome.Apply, outcome);
    }

    [TestMethod]
    public void TryResolve_StoredValueAlreadyEqualsTheRulesOutcome_ReportsAlreadyApplied()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"character":"Galadriel"}"""),
            IncomingRecord = Record("""{"character":"Frodo"}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "character", Resolution = FieldResolutionChoice.Custom, CustomValue = "Galadriel" }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        // Stored value already equals the rule's wanted value; the incoming side still carries the
        // wrong value ("Frodo"), same as it did when the rule was authored — nothing to do.
        bool found = lookup.TryResolve("abc123", "character", "Galadriel", "Frodo", out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.AreEqual(ConflictRuleOutcome.AlreadyApplied, outcome);
    }

    /// <summary>"...or add it if it was missing" — a `Custom` rule whose target field has never been set.</summary>
    [TestMethod]
    public void TryResolve_StoredValueIsMissing_ReportsApply()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = EmptyRecord,
            IncomingRecord = Record("""{"character":"Frodo"}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "character", Resolution = FieldResolutionChoice.Custom, CustomValue = "Galadriel" }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        bool found = lookup.TryResolve("abc123", "character", null, "Frodo", out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.AreEqual(ConflictRuleOutcome.Apply, outcome);
    }

    /// <summary>`Keep` and `Replace` are judged against their own wanted value, not only `Custom`.
    /// A `Keep` rule's wanted value is always the current existing value, so once a row exists it is
    /// always already-applied (never `Apply`) — the stored value cannot differ from itself.</summary>
    [TestMethod]
    public void TryResolve_KeepAndReplaceOutcomes_AreJudgedAgainstTheirOwnWantedValue()
    {
        ConflictResolutionRule keepRule = new ConflictResolutionRule
        {
            EntityId       = "keep-entity",
            ExistingRecord = Record("""{"date":"1980"}"""),
            IncomingRecord = Record("""{"date":"1975"}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictResolutionRule replaceRule = new ConflictResolutionRule
        {
            EntityId       = "replace-entity",
            ExistingRecord = Record("""{"date":"1980"}"""),
            IncomingRecord = Record("""{"date":"1975"}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Replace }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([keepRule, replaceRule]);

        // Keep: wanted value is the current existing value ("1980") — always already stored.
        Assert.IsTrue(lookup.TryResolve("keep-entity", "date", "1980", "1975", out _, out ConflictRuleOutcome keepOutcome));
        Assert.AreEqual(ConflictRuleOutcome.AlreadyApplied, keepOutcome, "Keep's wanted value is the stored value itself");

        // Replace: wanted value is the current incoming value ("1975") — stored side ("1980") still
        // needs updating.
        Assert.IsTrue(lookup.TryResolve("replace-entity", "date", "1980", "1975", out _, out ConflictRuleOutcome replaceOutcome));
        Assert.AreEqual(ConflictRuleOutcome.Apply, replaceOutcome, "Replace's wanted value is the incoming value, which the stored value has not yet taken");
    }

    // ── #374: retirement — a rule whose incoming side has moved into agreement ─────────────────

    /// <summary>A `Keep` rule where the incoming side, which disagreed when the rule was authored, now
    /// agrees with the existing side — the rule would produce the same result if deleted.</summary>
    [TestMethod]
    public void TryResolve_IncomingMovedIntoAgreement_ReportsRetirable()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"date":"1980"}"""),
            IncomingRecord = Record("""{"date":"1975"}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "date", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        // The incoming side has moved from "1975" (disagreeing, as recorded) to "1980" (agreeing with
        // the existing side) — the field would resolve to "1980" with or without this rule.
        bool found = lookup.TryResolve("abc123", "date", "1980", "1980", out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.AreEqual(ConflictRuleOutcome.Retirable, outcome);
    }

    /// <summary>Both sides agreeing is not enough for a `Custom` rule to be retirable when they agree on
    /// a value other than the custom one — that agreement is exactly the wrong value the rule exists to
    /// correct, so deleting the rule would let the wrong value back in.</summary>
    [TestMethod]
    public void TryResolve_CustomRuleWhereBothSidesAgreeButNotWithCustomValue_IsNotRetirable()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"character":"Frodo"}"""),
            IncomingRecord = Record("""{"character":"Frodo"}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "character", Resolution = FieldResolutionChoice.Custom, CustomValue = "Galadriel" }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        // Both sides still agree on "Frodo" — the wrong value the Custom rule exists to correct —
        // never on "Galadriel". Not retirable, and the stored value ("Frodo") still needs the rule
        // applied.
        bool found = lookup.TryResolve("abc123", "character", "Frodo", "Frodo", out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.AreNotEqual(ConflictRuleOutcome.Retirable, outcome);
        Assert.AreEqual(ConflictRuleOutcome.Apply, outcome);
    }

    /// <summary>An already-applied rule is still doing work on every import — the incoming file still
    /// carries the wrong value, and the rule is what stops it overwriting the corrected stored value.
    /// It must never be reported retirable.</summary>
    [TestMethod]
    public void TryResolve_AlreadyAppliedRule_IsNotRetirable()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"character":"Galadriel"}"""),
            IncomingRecord = Record("""{"character":"Frodo"}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "character", Resolution = FieldResolutionChoice.Custom, CustomValue = "Galadriel" }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        bool found = lookup.TryResolve("abc123", "character", "Galadriel", "Frodo", out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.AreNotEqual(ConflictRuleOutcome.Retirable, outcome);
        Assert.AreEqual(ConflictRuleOutcome.AlreadyApplied, outcome);
    }

    /// <summary>Proves the already-applied comparison goes through <see cref="FieldMergeResolver.ValuesEqual(object?, object?)"/>,
    /// not <c>Equals</c> — a casing-only difference between the stored value and the rule's wanted value
    /// must still report already-applied.</summary>
    [TestMethod]
    public void TryResolve_OutcomeDiffersOnlyByCase_ReportsAlreadyApplied()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"source":"Star Wars"}"""),
            IncomingRecord = Record("""{"source":"star wras"}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "source", Resolution = FieldResolutionChoice.Custom, CustomValue = "Star Wars" }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        bool found = lookup.TryResolve("abc123", "source", "STAR WARS", "star wras", out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.AreEqual(ConflictRuleOutcome.AlreadyApplied, outcome, "The stored value differs from the custom value only by case");
    }

    /// <summary>
    /// The list-valued sibling of <see cref="TryResolve_OutcomeDiffersOnlyByCase_ReportsAlreadyApplied"/>
    /// — proves the "moved into agreement" comparison behind <see cref="ConflictRuleOutcome.Retirable"/>
    /// also handles list-valued fields via <c>ValuesEqual</c>'s order-independent set comparison, not
    /// element-wise <c>Equals</c>. (<see cref="ConflictResolutionFieldRule.CustomValue"/> is string-only,
    /// so a list-valued field cannot exercise <c>AlreadyApplied</c> directly via a <c>Custom</c> rule —
    /// <c>Retirable</c> is the reachable list-comparison case.)
    /// </summary>
    [TestMethod]
    public void TryResolve_ListOutcomeAgreesOnlyByOrder_ReportsRetirable()
    {
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = "abc123",
            ExistingRecord = Record("""{"genres":["drama"]}"""),
            IncomingRecord = Record("""{"genres":["sci-fi"]}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "genres", Resolution = FieldResolutionChoice.Keep }],
        };
        ConflictRuleLookup lookup = new ConflictRuleLookup([rule]);

        // The incoming side has moved from ["sci-fi"] (disagreeing, as recorded) to ["sci-fi","drama"] —
        // the same set as the existing side ["drama","sci-fi"], only reordered.
        bool found = lookup.TryResolve("abc123", "genres", new List<string> { "drama", "sci-fi" }, new List<string> { "sci-fi", "drama" }, out _, out ConflictRuleOutcome outcome);

        Assert.IsTrue(found);
        Assert.AreEqual(ConflictRuleOutcome.Retirable, outcome, "The two sides agree once order is ignored");
    }
}
