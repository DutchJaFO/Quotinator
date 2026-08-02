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

    private static ImportActionFieldRow Row(string field, string? existingValue, string? incomingValue, FieldResolutionChoice? decision = null, string? customValue = null, string entityId = EntityId) =>
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
        var rows = new[]
        {
            Row("quoteText", "Original text", "A changed line.", null),
            Row("date", "1939", null, FieldResolutionChoice.Keep),
        };

        var rules = ConflictRuleGenerator.Generate(rows);

        var rule = rules.Single();
        Assert.AreEqual(EntityId, rule.EntityId);
        var field = rule.Fields.Single();
        Assert.AreEqual("date", field.Field);
        Assert.AreEqual(FieldResolutionChoice.Keep, field.Resolution);
    }

    [TestMethod]
    public void Generate_MultipleDecidedFieldsSameEntity_CollapseIntoOneRule()
    {
        var rows = new[]
        {
            Row("date", "1939", null, FieldResolutionChoice.Keep),
            Row("character", null, "Rick Blaine", FieldResolutionChoice.Replace),
        };

        var rules = ConflictRuleGenerator.Generate(rows);

        Assert.HasCount(1, rules, "Both decided fields for the same entity must collapse into a single rule, per #153's Step 10 finding");
        Assert.HasCount(2, rules[0].Fields);
    }

    [TestMethod]
    public void Generate_NoDecidedFieldsForEntity_ProducesNoRule()
    {
        var rows = new[]
        {
            Row("quoteText", "Original text", "A changed line.", null),
            Row("date", "1939", null, null),
        };

        var rules = ConflictRuleGenerator.Generate(rows);

        Assert.IsEmpty(rules, "An entity with every field still undecided (Pending/Stale/Blocked) has nothing to generate a rule from yet");
    }

    [TestMethod]
    public void Generate_CustomResolution_CarriesCustomValue()
    {
        var rows = new[] { Row("character", null, null, FieldResolutionChoice.Custom, "Rick Blaine") };

        var rule = ConflictRuleGenerator.Generate(rows).Single();

        var field = rule.Fields.Single();
        Assert.AreEqual(FieldResolutionChoice.Custom, field.Resolution);
        Assert.AreEqual("Rick Blaine", field.CustomValue);
    }

    [TestMethod]
    public void Generate_ExistingAndIncomingRecords_ReflectEveryRowRegardlessOfDecision()
    {
        var rows = new[]
        {
            Row("quoteText", "Original text", "A changed line.", null),
            Row("date", "1939", null, FieldResolutionChoice.Keep),
        };

        var rule = ConflictRuleGenerator.Generate(rows).Single();

        Assert.AreEqual("Original text", rule.ExistingRecord.GetProperty("quoteText").GetString());
        Assert.AreEqual("A changed line.", rule.IncomingRecord.GetProperty("quoteText").GetString());
        Assert.AreEqual("1939", rule.ExistingRecord.GetProperty("date").GetString());
        Assert.AreEqual(JsonValueKind.Null, rule.IncomingRecord.GetProperty("date").ValueKind);
    }

    [TestMethod]
    public void Generate_GenresField_DecodedFromDelimitedStringIntoArray()
    {
        var rows = new[] { Row("genres", "drama;sci-fi", "", FieldResolutionChoice.Keep) };

        var rule = ConflictRuleGenerator.Generate(rows).Single();

        var genres = rule.ExistingRecord.GetProperty("genres").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.AreSequenceEqual(new[] { "drama", "sci-fi" }, genres);
    }

    [TestMethod]
    public void Generate_MultipleEntities_EachGetsItsOwnRule()
    {
        var rows = new[]
        {
            Row("date", "1939", null, FieldResolutionChoice.Keep, entityId: "e0000001-0000-4000-8000-000000000001"),
            Row("date", "1994", null, FieldResolutionChoice.Keep, entityId: "e0000002-0000-4000-8000-000000000002"),
        };

        var rules = ConflictRuleGenerator.Generate(rows);

        Assert.HasCount(2, rules);
    }

    // ── Merge ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Merge_NoExistingFile_ReturnsGeneratedRulesAsIs()
    {
        var generated = ConflictRuleGenerator.Generate([Row("date", "1939", null, FieldResolutionChoice.Keep)]);

        var merged = ConflictRuleGenerator.Merge(null, generated);

        Assert.HasCount(1, merged.Rules);
    }

    [TestMethod]
    public void Merge_NewEntityId_IsAppended()
    {
        var existingFile = new ConflictResolutionRuleFile
        {
            Rules = [BuildRule("e0000001-0000-4000-8000-000000000001", "date", FieldResolutionChoice.Keep)],
        };
        var generated = ConflictRuleGenerator.Generate([Row("date", "1994", null, FieldResolutionChoice.Keep, entityId: "e0000002-0000-4000-8000-000000000002")]);

        var merged = ConflictRuleGenerator.Merge(existingFile, generated);

        Assert.HasCount(2, merged.Rules);
    }

    [TestMethod]
    public void Merge_EntityAlreadyCoversField_ManualEditIsNeverOverwritten()
    {
        var existingFile = new ConflictResolutionRuleFile
        {
            Rules = [BuildRule(EntityId, "date", FieldResolutionChoice.Custom, "1942")],
        };
        // A generated rule for the SAME field, with a DIFFERENT resolution — must never win.
        var generated = ConflictRuleGenerator.Generate([Row("date", "1939", null, FieldResolutionChoice.Keep)]);

        var merged = ConflictRuleGenerator.Merge(existingFile, generated);

        var rule = merged.Rules.Single(r => r.EntityId == EntityId);
        var field = rule.Fields.Single(f => f.Field == "date");
        Assert.AreEqual(FieldResolutionChoice.Custom, field.Resolution, "The file's own hand-authored resolution must survive a generation run untouched");
        Assert.AreEqual("1942", field.CustomValue);
    }

    [TestMethod]
    public void Merge_EntityCoversDifferentField_NewFieldIsAdded()
    {
        var existingFile = new ConflictResolutionRuleFile
        {
            Rules = [BuildRule(EntityId, "date", FieldResolutionChoice.Keep)],
        };
        var generated = ConflictRuleGenerator.Generate([Row("character", null, "Rick Blaine", FieldResolutionChoice.Replace)]);

        var merged = ConflictRuleGenerator.Merge(existingFile, generated);

        var rule = merged.Rules.Single(r => r.EntityId == EntityId);
        Assert.HasCount(2, rule.Fields, "A genuinely new field for an already-covered entity must be added alongside the existing one");
        Assert.Contains(f => f.Field == "date", rule.Fields);
        Assert.Contains(f => f.Field == "character", rule.Fields);
    }

    private static readonly JsonElement EmptyRecord = JsonSerializer.Deserialize<JsonElement>("{}");

    private static ConflictResolutionRule BuildRule(string entityId, string field, FieldResolutionChoice resolution, string? customValue = null) => new()
    {
        EntityId       = entityId,
        ExistingRecord = EmptyRecord,
        IncomingRecord = EmptyRecord,
        Fields         = [new ConflictResolutionFieldRule { Field = field, Resolution = resolution, CustomValue = customValue }],
    };
}
