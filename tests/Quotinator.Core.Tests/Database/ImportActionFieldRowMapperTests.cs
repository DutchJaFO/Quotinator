using Quotinator.Core.Database;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;

namespace Quotinator.Core.Tests.Database;

[TestClass]
public class ImportActionFieldRowMapperTests
{
    private static ImportActionFieldRow Row(string entityType, string field, FieldResolutionChoice? decision = null, string? customValue = null, CompletenessStatus? markCompletenessAs = null) =>
        new()
        {
            ActionId            = Guid.NewGuid(),
            EntityId            = "e0000001-0000-4000-8000-000000000001",
            EntityType          = entityType,
            Field               = field,
            Decision            = decision,
            CustomValue         = customValue,
            MarkCompletenessAs  = markCompletenessAs,
        };

    [TestMethod]
    public void ToCsvRow_PopulatedRow_EmitsFieldsInCsvHeaderOrder()
    {
        var actionId = Guid.NewGuid();
        var row = new ImportActionFieldRow
        {
            ActionId           = actionId,
            EntityId           = "e0000001-0000-4000-8000-000000000001",
            EntityType         = ImportActionEntityTypes.Person,
            Field              = "name",
            ExistingValue      = "Old Name",
            IncomingValue      = "New Name",
            Decision           = FieldResolutionChoice.Custom,
            CustomValue        = "Custom Name",
            MarkCompletenessAs = CompletenessStatus.Complete,
        };

        var fields = ImportActionFieldRowMapper.ToCsvRow(row).ToList();

        CollectionAssert.AreEqual(new[]
        {
            actionId.ToString(), "e0000001-0000-4000-8000-000000000001", "Person", "name",
            "Old Name", "New Name", "Custom", "Custom Name", "Complete",
        }, fields);
    }

    [TestMethod]
    public void ToCsvRow_UndecidedRow_EmitsNullForDecisionCustomValueAndMarkCompletenessAs()
    {
        var row = new ImportActionFieldRow
        {
            ActionId      = Guid.NewGuid(),
            EntityId      = "e0000001-0000-4000-8000-000000000001",
            EntityType    = ImportActionEntityTypes.Person,
            Field         = "name",
            ExistingValue = "Old Name",
            IncomingValue = "New Name",
        };

        var fields = ImportActionFieldRowMapper.ToCsvRow(row).ToList();

        Assert.IsNull(fields[6], "Decision");
        Assert.IsNull(fields[7], "CustomValue");
        Assert.IsNull(fields[8], "MarkCompletenessAs");
    }

    [TestMethod]
    public void ToCsvRow_ThenFromCsvRow_RoundTripsAllFields()
    {
        var row = new ImportActionFieldRow
        {
            ActionId           = Guid.NewGuid(),
            EntityId           = "e0000001-0000-4000-8000-000000000001",
            EntityType         = ImportActionEntityTypes.Person,
            Field              = "name",
            ExistingValue      = "Old Name",
            IncomingValue      = "New Name",
            Decision           = FieldResolutionChoice.Custom,
            CustomValue        = "Custom Name",
            MarkCompletenessAs = CompletenessStatus.Complete,
        };

        var fields = ImportActionFieldRowMapper.ToCsvRow(row).ToList();
        var parsed = ImportActionFieldRowMapper.FromCsvRow(fields!);

        Assert.AreEqual(row.ActionId, parsed.ActionId);
        Assert.AreEqual(row.EntityId, parsed.EntityId);
        Assert.AreEqual(row.EntityType, parsed.EntityType);
        Assert.AreEqual(row.Field, parsed.Field);
        Assert.AreEqual(row.ExistingValue, parsed.ExistingValue);
        Assert.AreEqual(row.IncomingValue, parsed.IncomingValue);
        Assert.AreEqual(row.Decision, parsed.Decision);
        Assert.AreEqual(row.CustomValue, parsed.CustomValue);
        Assert.AreEqual(row.MarkCompletenessAs, parsed.MarkCompletenessAs);
    }

    [TestMethod]
    public void FromCsvRow_EmptyOptionalFields_ParsedAsNull()
    {
        var parsed = ImportActionFieldRowMapper.FromCsvRow(
            [Guid.NewGuid().ToString(), "e0000001-0000-4000-8000-000000000001", "Person", "name", "", "", "", "", ""]);

        Assert.IsNull(parsed.ExistingValue);
        Assert.IsNull(parsed.IncomingValue);
        Assert.IsNull(parsed.Decision);
        Assert.IsNull(parsed.CustomValue);
        Assert.IsNull(parsed.MarkCompletenessAs);
    }

    [TestMethod]
    public void FromCsvRow_MalformedActionId_ThrowsFormatException() =>
        Assert.ThrowsExactly<FormatException>(() => ImportActionFieldRowMapper.FromCsvRow(
            ["not-a-guid", "e0000001-0000-4000-8000-000000000001", "Person", "name", "", "", "", "", ""]));

    [TestMethod]
    public void FromCsvRow_MalformedDecision_ThrowsFormatException() =>
        Assert.ThrowsExactly<FormatException>(() => ImportActionFieldRowMapper.FromCsvRow(
            [Guid.NewGuid().ToString(), "e0000001-0000-4000-8000-000000000001", "Person", "name", "", "", "NotARealChoice", "", ""]));

    [TestMethod]
    public void FromCsvRow_MalformedMarkCompletenessAs_ThrowsFormatException() =>
        Assert.ThrowsExactly<FormatException>(() => ImportActionFieldRowMapper.FromCsvRow(
            [Guid.NewGuid().ToString(), "e0000001-0000-4000-8000-000000000001", "Person", "name", "", "", "", "", "NotAStatus"]));

    [TestMethod]
    public void FromCsvRow_WrongColumnCount_ThrowsFormatException() =>
        Assert.ThrowsExactly<FormatException>(() => ImportActionFieldRowMapper.FromCsvRow(["too", "few", "columns"]));

    [TestMethod]
    public void FromCsvRow_DecisionCaseInsensitive_Parses() =>
        Assert.AreEqual(FieldResolutionChoice.Replace, ImportActionFieldRowMapper.FromCsvRow(
            [Guid.NewGuid().ToString(), "e0000001-0000-4000-8000-000000000001", "Person", "name", "", "", "replace", "", ""]).Decision);

    [TestMethod]
    public void EncodeGenres_NullList_ReturnsNull() =>
        Assert.IsNull(ImportActionFieldRowMapper.EncodeGenres(null));

    [TestMethod]
    public void EncodeGenres_MultipleValues_JoinsWithSeparator() =>
        Assert.AreEqual("drama;comedy", ImportActionFieldRowMapper.EncodeGenres(["drama", "comedy"]));

    [TestMethod]
    public void DecodeGenres_NullString_ReturnsNull() =>
        Assert.IsNull(ImportActionFieldRowMapper.DecodeGenres(null));

    [TestMethod]
    public void DecodeGenres_EmptyString_ReturnsEmptyList() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), ImportActionFieldRowMapper.DecodeGenres(""));

    [TestMethod]
    public void DecodeGenres_DelimitedString_RoundTripsFromEncode()
    {
        var encoded = ImportActionFieldRowMapper.EncodeGenres(["drama", "comedy", "sci-fi"]);
        CollectionAssert.AreEqual(new[] { "drama", "comedy", "sci-fi" }, ImportActionFieldRowMapper.DecodeGenres(encoded));
    }

    [TestMethod]
    public void BuildRequest_UnknownEntityType_ThrowsImportActionUnknownEntityTypeException()
    {
        var rows = new[] { Row("NotAnEntityType", "name") };
        var ex = Assert.ThrowsExactly<ImportActionUnknownEntityTypeException>(() => ImportActionFieldRowMapper.BuildRequest("NotAnEntityType", rows));
        Assert.AreEqual("NotAnEntityType", ex.EntityType);
    }

    [TestMethod]
    public void BuildRequest_FieldNotValidForEntityType_ThrowsImportActionUnknownFieldException()
    {
        // "quoteText" is a real field, but not one of Person's decidable fields.
        var rows = new[] { Row(ImportActionEntityTypes.Person, "quoteText", FieldResolutionChoice.Replace) };
        var ex = Assert.ThrowsExactly<ImportActionUnknownFieldException>(() => ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Person, rows));
        Assert.AreEqual(ImportActionEntityTypes.Person, ex.EntityType);
        Assert.AreEqual("quoteText", ex.Field);
    }

    [TestMethod]
    public void BuildRequest_FieldNameReusedAcrossEntityTypes_MapsToTheCorrectEntitySpecificProperty()
    {
        // "name" means something different on every one of these four entity types — proves the
        // (EntityType, Field) pair is what disambiguates, not Field alone.
        var person = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Person, [Row(ImportActionEntityTypes.Person, "name", FieldResolutionChoice.Replace)]);
        Assert.IsNotNull(person.PersonName);
        Assert.IsNull(person.CharacterName);
        Assert.IsNull(person.SeriesName);
        Assert.IsNull(person.UniverseName);

        var character = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Character, [Row(ImportActionEntityTypes.Character, "name", FieldResolutionChoice.Replace)]);
        Assert.IsNotNull(character.CharacterName);
        Assert.IsNull(character.PersonName);

        var series = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Series, [Row(ImportActionEntityTypes.Series, "name", FieldResolutionChoice.Replace)]);
        Assert.IsNotNull(series.SeriesName);
        Assert.IsNull(series.UniverseName);

        var universe = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Universe, [Row(ImportActionEntityTypes.Universe, "name", FieldResolutionChoice.Replace)]);
        Assert.IsNotNull(universe.UniverseName);
        Assert.IsNull(universe.SeriesName);
    }

    [TestMethod]
    public void BuildRequest_QuoteAllScalarFieldsPlusGenres_MapsEveryPropertyWithCorrectChoiceAndValue()
    {
        var rows = new[]
        {
            Row(ImportActionEntityTypes.Quote, "quoteText", FieldResolutionChoice.Custom, "Custom text"),
            Row(ImportActionEntityTypes.Quote, "originalLanguage", FieldResolutionChoice.Keep),
            Row(ImportActionEntityTypes.Quote, "source", FieldResolutionChoice.Replace),
            Row(ImportActionEntityTypes.Quote, "date", FieldResolutionChoice.Keep),
            Row(ImportActionEntityTypes.Quote, "character", FieldResolutionChoice.Replace),
            Row(ImportActionEntityTypes.Quote, "author", FieldResolutionChoice.Keep),
            Row(ImportActionEntityTypes.Quote, "type", FieldResolutionChoice.Replace),
            Row(ImportActionEntityTypes.Quote, "genres", FieldResolutionChoice.Custom, "drama;comedy"),
        };

        var request = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Quote, rows);

        Assert.AreEqual(FieldResolutionChoice.Custom, request.QuoteText!.Choice);
        Assert.AreEqual("Custom text", request.QuoteText.Value);
        Assert.AreEqual(FieldResolutionChoice.Keep, request.OriginalLanguage!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Replace, request.Source!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Keep, request.Date!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Replace, request.Character!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Keep, request.Author!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Replace, request.Type!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Custom, request.Genres!.Choice);
        CollectionAssert.AreEqual(new[] { "drama", "comedy" }, request.Genres.Value);
    }

    [TestMethod]
    public void BuildRequest_SourceFields_MapToSourcePrefixedProperties()
    {
        var rows = new[]
        {
            Row(ImportActionEntityTypes.Source, "title", FieldResolutionChoice.Replace),
            Row(ImportActionEntityTypes.Source, "type", FieldResolutionChoice.Keep),
            Row(ImportActionEntityTypes.Source, "date", FieldResolutionChoice.Replace),
            Row(ImportActionEntityTypes.Source, "seriesId", FieldResolutionChoice.Custom, "s0000001-0000-4000-8000-000000000001"),
        };

        var request = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Source, rows);

        Assert.AreEqual(FieldResolutionChoice.Replace, request.SourceTitle!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Keep, request.SourceType!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Replace, request.SourceDate!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Custom, request.SourceSeriesId!.Choice);
        Assert.AreEqual("s0000001-0000-4000-8000-000000000001", request.SourceSeriesId.Value);
    }

    [TestMethod]
    public void BuildRequest_SeriesFields_MapToSeriesPrefixedProperties()
    {
        var rows = new[]
        {
            Row(ImportActionEntityTypes.Series, "name", FieldResolutionChoice.Replace),
            Row(ImportActionEntityTypes.Series, "universeId", FieldResolutionChoice.Keep),
        };

        var request = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Series, rows);

        Assert.AreEqual(FieldResolutionChoice.Replace, request.SeriesName!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Keep, request.SeriesUniverseId!.Choice);
    }

    [TestMethod]
    public void BuildRequest_StageDirectionFields_MapToStageDirectionPrefixedProperties()
    {
        var rows = new[]
        {
            Row(ImportActionEntityTypes.StageDirection, "text", FieldResolutionChoice.Replace),
            Row(ImportActionEntityTypes.StageDirection, "imageUrl", FieldResolutionChoice.Keep),
        };

        var request = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.StageDirection, rows);

        Assert.AreEqual(FieldResolutionChoice.Replace, request.StageDirectionText!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Keep, request.StageDirectionImageUrl!.Choice);
    }

    [TestMethod]
    public void BuildRequest_SoundCueFields_MapToSoundCuePrefixedProperties()
    {
        var rows = new[]
        {
            Row(ImportActionEntityTypes.SoundCue, "text", FieldResolutionChoice.Replace),
            Row(ImportActionEntityTypes.SoundCue, "soundFileUrl", FieldResolutionChoice.Keep),
            Row(ImportActionEntityTypes.SoundCue, "imageUrl", FieldResolutionChoice.Replace),
        };

        var request = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.SoundCue, rows);

        Assert.AreEqual(FieldResolutionChoice.Replace, request.SoundCueText!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Keep, request.SoundCueSoundFileUrl!.Choice);
        Assert.AreEqual(FieldResolutionChoice.Replace, request.SoundCueImageUrl!.Choice);
    }

    [TestMethod]
    public void BuildRequest_ConversationField_MapsToConversationDescription_NoDedicatedToDecisionMapExistsForThisEntity()
    {
        var rows = new[] { Row(ImportActionEntityTypes.Conversation, "description", FieldResolutionChoice.Replace) };

        var request = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Conversation, rows);

        Assert.AreEqual(FieldResolutionChoice.Replace, request.ConversationDescription!.Choice);
    }

    [TestMethod]
    public void BuildRequest_RowWithNullDecision_SuppliesNoOverrideForThatField()
    {
        var rows = new[] { Row(ImportActionEntityTypes.Person, "name", decision: null) };

        var request = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Person, rows);

        Assert.IsNull(request.PersonName);
    }

    [TestMethod]
    public void BuildRequest_MarkCompletenessAsRepeatedAcrossRows_TakesFirstNonNullValue()
    {
        var rows = new[]
        {
            Row(ImportActionEntityTypes.Person, "name", FieldResolutionChoice.Replace, markCompletenessAs: CompletenessStatus.Complete),
            Row(ImportActionEntityTypes.Person, "dateOfBirth", FieldResolutionChoice.Keep, markCompletenessAs: CompletenessStatus.Complete),
        };

        var request = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Person, rows);

        Assert.AreEqual(CompletenessStatus.Complete, request.MarkCompletenessAs);
    }

    [TestMethod]
    public void BuildRequest_MarkCompletenessAsOmitted_LeavesRequestValueNull()
    {
        var rows = new[] { Row(ImportActionEntityTypes.Person, "name", FieldResolutionChoice.Replace) };

        var request = ImportActionFieldRowMapper.BuildRequest(ImportActionEntityTypes.Person, rows);

        Assert.IsNull(request.MarkCompletenessAs);
    }

    [TestMethod]
    public void DecidableFieldsByEntityType_CoversAllNineEntityTypes()
    {
        foreach (var entityType in ImportActionEntityTypes.All)
            Assert.IsTrue(ImportActionFieldRowMapper.DecidableFieldsByEntityType.ContainsKey(entityType), $"Missing decidable-fields entry for '{entityType}'");
        Assert.AreEqual(9, ImportActionFieldRowMapper.DecidableFieldsByEntityType.Count);
    }
}
