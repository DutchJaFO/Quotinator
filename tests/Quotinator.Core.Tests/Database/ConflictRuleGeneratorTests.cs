using Quotinator.Data.Enums;
using System.Text.Json;
using Quotinator.Core.Database;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Core.Tests.Database;

[TestClass]
public class ConflictRuleGeneratorTests
{
    private const string EntityId = "e0000001-0000-4000-8000-000000000001";

    private static ImportActionFieldRowResponse Row(string field, string? existingValue, string? incomingValue, FieldResolutionChoice? decision = null, string? customValue = null, string entityId = EntityId) =>
        new()
        {
            ActionId      = Guid.NewGuid(),
            EntityId      = entityId,
            EntityType    = ImportActionEntityTypes.Quote,
            Field         = field,
            ExistingValue = existingValue,
            IncomingValue = incomingValue,
            Decision      = decision,
            CustomValue   = customValue,
        };

    // ── Generate ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_OneDecidedField_ProducesSingleFieldRule()
    {
        ImportActionFieldRowResponse[] rows =
        [
            Row("quoteText", "Original text", "A changed line.", null),
            Row("date", "1939", null, FieldResolutionChoice.Keep),
        ];

        IReadOnlyList<ConflictResolutionRule> rules = ConflictRuleGenerator.Generate(rows);

        ConflictResolutionRule rule = rules.Single();
        Assert.AreEqual(EntityId, rule.EntityId);
        ConflictResolutionFieldRule field = rule.Fields.Single();
        Assert.AreEqual("date", field.Field);
        Assert.AreEqual(FieldResolutionChoice.Keep, field.Resolution);
    }

    [TestMethod]
    public void Generate_MultipleDecidedFieldsSameEntity_CollapseIntoOneRule()
    {
        ImportActionFieldRowResponse[] rows =
        [
            Row("date", "1939", null, FieldResolutionChoice.Keep),
            Row("character", null, "Rick Blaine", FieldResolutionChoice.Replace),
        ];

        IReadOnlyList<ConflictResolutionRule> rules = ConflictRuleGenerator.Generate(rows);

        Assert.HasCount(1, rules, "Both decided fields for the same entity must collapse into a single rule, per #153's Step 10 finding");
        Assert.HasCount(2, rules[0].Fields);
    }

    [TestMethod]
    public void Generate_NoDecidedFieldsForEntity_ProducesNoRule()
    {
        ImportActionFieldRowResponse[] rows =
        [
            Row("quoteText", "Original text", "A changed line.", null),
            Row("date", "1939", null, null),
        ];

        IReadOnlyList<ConflictResolutionRule> rules = ConflictRuleGenerator.Generate(rows);

        Assert.IsEmpty(rules, "An entity with every field still undecided (Pending/Stale/Blocked) has nothing to generate a rule from yet");
    }

    [TestMethod]
    public void Generate_CustomResolution_CarriesCustomValue()
    {
        ImportActionFieldRowResponse[] rows = [Row("character", null, null, FieldResolutionChoice.Custom, "Rick Blaine")];

        ConflictResolutionRule rule = ConflictRuleGenerator.Generate(rows).Single();

        ConflictResolutionFieldRule field = rule.Fields.Single();
        Assert.AreEqual(FieldResolutionChoice.Custom, field.Resolution);
        Assert.AreEqual("Rick Blaine", field.CustomValue);
    }

    [TestMethod]
    public void Generate_ExistingAndIncomingRecords_ReflectEveryRowRegardlessOfDecision()
    {
        ImportActionFieldRowResponse[] rows =
        [
            Row("quoteText", "Original text", "A changed line.", null),
            Row("date", "1939", null, FieldResolutionChoice.Keep),
        ];

        ConflictResolutionRule rule = ConflictRuleGenerator.Generate(rows).Single();

        Assert.AreEqual("Original text", rule.ExistingRecord.GetProperty("quoteText").GetString());
        Assert.AreEqual("A changed line.", rule.IncomingRecord.GetProperty("quoteText").GetString());
        Assert.AreEqual("1939", rule.ExistingRecord.GetProperty("date").GetString());
        Assert.AreEqual(JsonValueKind.Null, rule.IncomingRecord.GetProperty("date").ValueKind);
    }

    [TestMethod]
    public void Generate_GenresField_DecodedFromDelimitedStringIntoArray()
    {
        ImportActionFieldRowResponse[] rows = [Row("genres", "drama;sci-fi", "", FieldResolutionChoice.Keep)];

        ConflictResolutionRule rule = ConflictRuleGenerator.Generate(rows).Single();

        string?[] genres = [.. rule.ExistingRecord.GetProperty("genres").EnumerateArray().Select(e => e.GetString())];
        Assert.AreSequenceEqual(["drama", "sci-fi"], genres);
    }

    [TestMethod]
    public void Generate_MultipleEntities_EachGetsItsOwnRule()
    {
        ImportActionFieldRowResponse[] rows =
        [
            Row("date", "1939", null, FieldResolutionChoice.Keep, entityId: "e0000001-0000-4000-8000-000000000001"),
            Row("date", "1994", null, FieldResolutionChoice.Keep, entityId: "e0000002-0000-4000-8000-000000000002"),
        ];

        IReadOnlyList<ConflictResolutionRule> rules = ConflictRuleGenerator.Generate(rows);

        Assert.HasCount(2, rules);
    }

    // ── Merge ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Merge_NoExistingFile_ReturnsGeneratedRulesAsIs()
    {
        IReadOnlyList<ConflictResolutionRule> generated = ConflictRuleGenerator.Generate([Row("date", "1939", null, FieldResolutionChoice.Keep)]);

        ConflictResolutionRuleFileDto merged = ConflictRuleGenerator.Merge(null, generated).File!;

        Assert.HasCount(1, merged.Rules);
    }

    [TestMethod]
    public void Merge_NewEntityId_IsAppended()
    {
        ConflictResolutionRuleFileDto existingFile = new()
        {
            Rules = [BuildRule("e0000001-0000-4000-8000-000000000001", "date", FieldResolutionChoice.Keep)],
        };
        IReadOnlyList<ConflictResolutionRule> generated = ConflictRuleGenerator.Generate([Row("date", "1994", null, FieldResolutionChoice.Keep, entityId: "e0000002-0000-4000-8000-000000000002")]);

        ConflictResolutionRuleFileDto merged = ConflictRuleGenerator.Merge(existingFile, generated).File!;

        Assert.HasCount(2, merged.Rules);
    }

    [TestMethod]
    public void Merge_EntityAlreadyCoversField_ManualEditIsNeverOverwritten()
    {
        ConflictResolutionRuleFileDto existingFile = new()
        {
            Rules = [BuildRule(EntityId, "date", FieldResolutionChoice.Custom, "1942")],
        };
        // A generated rule for the SAME field, with a DIFFERENT resolution — must never win.
        IReadOnlyList<ConflictResolutionRule> generated = ConflictRuleGenerator.Generate([Row("date", "1939", null, FieldResolutionChoice.Keep)]);

        ConflictResolutionRuleFileDto merged = ConflictRuleGenerator.Merge(existingFile, generated).File!;

        ConflictResolutionRule rule = merged.Rules.Single(r => r.EntityId == EntityId);
        ConflictResolutionFieldRule field = rule.Fields.Single(f => f.Field == "date");
        Assert.AreEqual(FieldResolutionChoice.Custom, field.Resolution, "The file's own hand-authored resolution must survive a generation run untouched");
        Assert.AreEqual("1942", field.CustomValue);
    }

    [TestMethod]
    public void Merge_EntityCoversDifferentField_NewFieldIsAdded()
    {
        ConflictResolutionRuleFileDto existingFile = new()
        {
            Rules = [BuildRule(EntityId, "date", FieldResolutionChoice.Keep)],
        };
        IReadOnlyList<ConflictResolutionRule> generated = ConflictRuleGenerator.Generate([Row("character", null, "Rick Blaine", FieldResolutionChoice.Replace)]);

        ConflictResolutionRuleFileDto merged = ConflictRuleGenerator.Merge(existingFile, generated).File!;

        ConflictResolutionRule rule = merged.Rules.Single(r => r.EntityId == EntityId);
        Assert.HasCount(2, rule.Fields, "A genuinely new field for an already-covered entity must be added alongside the existing one");
        Assert.Contains(f => f.Field == "date", rule.Fields);
        Assert.Contains(f => f.Field == "character", rule.Fields);
    }

    // ── #420: the recorded incoming value belongs to the field it governs (ADR 023) ────────────

    /// <summary>#420: `Generate` already holds each row's own `IncomingValue`; ADR 023 records it on the
    /// field rather than only in the entry-level snapshot, so two fields of one entity can each carry the
    /// value that is correct for them.</summary>
    [TestMethod]
    public void Generate_RecordsEachFieldsOwnIncomingValue()
    {
        ImportActionFieldRowResponse[] rows =
        [
            Row("date", "1939", "2017", FieldResolutionChoice.Keep),
            Row("character", null, null, FieldResolutionChoice.Custom, "Fernando Vera"),
        ];

        ConflictResolutionRule rule = ConflictRuleGenerator.Generate(rows).Single();

        ConflictResolutionFieldRule date = rule.Fields.Single(f => f.Field == "date");
        Assert.AreEqual("2017", date.RecordedIncomingValue.GetString(), "The date field must record its own incoming value");
        ConflictResolutionFieldRule character = rule.Fields.Single(f => f.Field == "character");
        Assert.AreEqual(JsonValueKind.Null, character.RecordedIncomingValue.ValueKind,
            "A null incoming value is recorded as an explicit JSON null, never left Undefined — Undefined means 'not recorded'");
    }

    /// <summary>#420: a `genres` row decodes to a list, and the per-field recorded value has to carry
    /// that shape rather than the delimited string.</summary>
    [TestMethod]
    public void Generate_RecordedIncomingValueForGenres_IsAnArray()
    {
        ImportActionFieldRowResponse[] rows = [Row("genres", "drama", "drama;sci-fi", FieldResolutionChoice.Replace)];

        ConflictResolutionRule rule = ConflictRuleGenerator.Generate(rows).Single();

        JsonElement recorded = rule.Fields.Single().RecordedIncomingValue;
        Assert.AreEqual(JsonValueKind.Array, recorded.ValueKind);
        Assert.AreSequenceEqual(["drama", "sci-fi"], recorded.EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    /// <summary>#420: the field a merge adds carries its own recorded value, so it is judged on its own
    /// terms instead of inheriting the entry's snapshot — which is what made "which entry does a new
    /// field join" an arbitrary choice under the old shape.</summary>
    [TestMethod]
    public void Merge_NewFieldCarriesItsOwnRecordedIncomingValue()
    {
        ConflictResolutionRuleFileDto existingFile = new()
        {
            Rules = [BuildRule(EntityId, "date", FieldResolutionChoice.Keep)],
        };
        IReadOnlyList<ConflictResolutionRule> generated = ConflictRuleGenerator.Generate([Row("character", null, "Rick Blaine", FieldResolutionChoice.Replace)]);

        ConflictResolutionRuleFileDto merged = ConflictRuleGenerator.Merge(existingFile, generated).File!;

        ConflictResolutionFieldRule added = merged.Rules.Single(r => r.EntityId == EntityId).Fields.Single(f => f.Field == "character");
        Assert.AreEqual("Rick Blaine", added.RecordedIncomingValue.GetString());
    }

    /// <summary>#420: the defect this issue exists for. `Merge` keyed a dictionary on entity id, so a
    /// file naming one entity twice threw `ArgumentException` and surfaced as an unhandled 500. The
    /// condition is checkable before the dictionary is built, so per ADR 022 it is an outcome.</summary>
    [TestMethod]
    public void Merge_ExistingFileNamesOneEntityTwice_ReportsTheDuplicateInsteadOfThrowing()
    {
        ConflictResolutionRuleFileDto existingFile = new()
        {
            Rules =
            [
                BuildRule(EntityId, "date", FieldResolutionChoice.Custom, "2015"),
                BuildRule(EntityId, "character", FieldResolutionChoice.Custom, "Fernando Vera"),
            ],
        };
        IReadOnlyList<ConflictResolutionRule> generated = ConflictRuleGenerator.Generate([Row("type", "tv", "tv", FieldResolutionChoice.Keep)]);

        ConflictRuleMergeResult result = ConflictRuleGenerator.Merge(existingFile, generated);

        Assert.IsFalse(result.IsMerged, "A file naming one entity twice must be refused, not merged");
        Assert.AreEqual(EntityId, result.DuplicateEntityId, "The repeated id is named so the caller can report which one it was");
        Assert.IsNull(result.File);
    }

    /// <summary>#420: the duplicate check is case-insensitive, per this project's id-comparison
    /// convention — two entries differing only in casing are the same entity, and `Merge`'s own
    /// dictionary already used `OrdinalIgnoreCase`, so a case-variant pair threw exactly as an exact
    /// one did.</summary>
    [TestMethod]
    public void Merge_ExistingFileNamesOneEntityTwiceDifferingOnlyByCase_IsStillADuplicate()
    {
        ConflictResolutionRuleFileDto existingFile = new()
        {
            Rules =
            [
                BuildRule(EntityId.ToLowerInvariant(), "date", FieldResolutionChoice.Keep),
                BuildRule(EntityId.ToUpperInvariant(), "character", FieldResolutionChoice.Keep),
            ],
        };

        ConflictRuleMergeResult result = ConflictRuleGenerator.Merge(existingFile, []);

        Assert.IsFalse(result.IsMerged, "Entity ids differing only by case are the same entity");
    }

    /// <summary>#420: the duplicate check must not fire on a file that is merely long, nor on the same
    /// id appearing in two different rule files — `Merge` runs for one file at a time.</summary>
    [TestMethod]
    public void Merge_ExistingFileNamesEachEntityOnce_IsMerged()
    {
        ConflictResolutionRuleFileDto existingFile = new()
        {
            Rules =
            [
                BuildRule("e0000001-0000-4000-8000-000000000001", "date", FieldResolutionChoice.Keep),
                BuildRule("e0000002-0000-4000-8000-000000000002", "date", FieldResolutionChoice.Keep),
            ],
        };

        ConflictRuleMergeResult result = ConflictRuleGenerator.Merge(existingFile, []);

        Assert.IsTrue(result.IsMerged);
        Assert.IsNull(result.DuplicateEntityId);
        Assert.HasCount(2, result.File!.Rules);
    }

    private static readonly JsonElement EmptyRecord = JsonSerializer.Deserialize<JsonElement>("{}");

    private static JsonElement Value(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static ConflictResolutionRule BuildRule(string entityId, string field, FieldResolutionChoice resolution, string? customValue = null) => new()
    {
        EntityId       = entityId,
        ExistingRecord = EmptyRecord,
        IncomingRecord = EmptyRecord,
        Fields         =
        [
            new ConflictResolutionFieldRule
            {
                Field                 = field,
                Resolution            = resolution,
                CustomValue           = customValue,
                RecordedIncomingValue = Value("null"),
            },
        ],
    };
}
