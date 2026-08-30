using Quotinator.Api.Startup;
using Quotinator.Changelog.Models;
using Quotinator.Constants.Api;
using Quotinator.Core.Services;
using Quotinator.Data.Notifications;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #319: the producers that run at startup need every language at once, not the one
/// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> happens to be — a startup producer
/// has no request culture, which is the whole reason a notification's text is stored per language
/// rather than resolved at render time.
/// </summary>
[TestClass]
public class NotificationTranslationSourceTests
{
    private static readonly string I18nDir = Path.Combine(AppContext.BaseDirectory, "i18ntext");

    // The non-original languages this project ships, alphabetically — the order .Order() produces.
    private static readonly string[] ExpectedTranslatedLanguages = ["de", "nl"];

    public TestContext TestContext { get; set; } = null!;

    /// <summary>Every non-English locale file contributes one translation, resolving no culture at all.</summary>
    [TestMethod]
    public void Build_ReturnsOneTranslationPerNonOriginalLanguage()
    {
        ApiLocalizer localizer = new(I18nDir);

        IReadOnlyList<NotificationTranslation> translations = NotificationTranslations.Build(
            localizer,
            ApiMessages.NotificationOperationIdRenameTitle,
            ApiMessages.NotificationOperationIdRenameBody);

        List<string> languages = [.. translations.Select(t => t.Language).Order()];

        Assert.AreSequenceEqual(ExpectedTranslatedLanguages, languages,
            "English is the original and stays on the notification row, so it is never a translation.");
    }

    /// <summary>The values come from the locale files, not from the key names or the English baseline.</summary>
    [TestMethod]
    public void Build_TakesTitleAndBodyFromTheLocaleFiles()
    {
        ApiLocalizer localizer = new(I18nDir);

        IReadOnlyList<NotificationTranslation> translations = NotificationTranslations.Build(
            localizer,
            ApiMessages.NotificationOperationIdRenameTitle,
            ApiMessages.NotificationOperationIdRenameBody);

        NotificationTranslation dutch = translations.Single(t => t.Language == "nl");

        Assert.AreEqual("Twee API-bewerkings-ID's zijn hernoemd", dutch.Title);
        Assert.Contains("GetAllImportBatches", dutch.Body);
        Assert.AreNotEqual(ApiMessages.NotificationOperationIdRenameTitle, dutch.Title,
            "A key echoed back means the lookup missed and the notification would show a key to the user.");
    }

    /// <summary>
    /// #304's two producers resolve their own strings from the same locale files, including the changed
    /// file list substituted into every language rather than only the English one.
    /// </summary>
    [TestMethod]
    public void ReseedRecommended_TakesTitleAndBodyFromTheLocaleFiles()
    {
        ApiLocalizer localizer = new(I18nDir);

        IReadOnlyList<NotificationTranslation> contentChanged = NotificationTranslations.Build(
            localizer,
            NotificationMessageKeys.ReseedContentChangedTitle,
            NotificationMessageKeys.ReseedContentChangedBody,
            bodyArgs: ["vilaboim_movie-quotes.json"]);

        Assert.AreSequenceEqual(ExpectedTranslatedLanguages, [.. contentChanged.Select(t => t.Language).Order()]);

        foreach (NotificationTranslation translation in contentChanged)
        {
            Assert.AreNotEqual(NotificationMessageKeys.ReseedContentChangedTitle, translation.Title,
                $"A key echoed back for '{translation.Language}' means the lookup missed and the user would see a key.");
            Assert.Contains("vilaboim_movie-quotes.json", translation.Body,
                $"The changed file has to be substituted into '{translation.Language}' too, not only English.");
        }

        IReadOnlyList<NotificationTranslation> afterReset = NotificationTranslations.Build(
            localizer,
            NotificationMessageKeys.ReseedAfterResetTitle,
            NotificationMessageKeys.ReseedAfterResetBody);

        Assert.AreSequenceEqual(ExpectedTranslatedLanguages, [.. afterReset.Select(t => t.Language).Order()]);
        Assert.AreNotEqual(
            afterReset.Single(t => t.Language == "nl").Body,
            contentChanged.Single(t => t.Language == "nl").Body,
            "The two reasons are different notifications and must not share one body — identical text "
            + "would mean one of the two keys was never actually looked up.");
    }

    /// <summary>
    /// The original stored on the row is English regardless of the host's current culture — a startup
    /// producer has no request culture, and a stored original in the wrong language would be mislabelled
    /// by <c>OriginalLanguage</c> rather than translated.
    /// </summary>
    [TestMethod]
    public void ReseedRecommended_OriginalIsEnglish()
    {
        ApiLocalizer localizer = new(I18nDir);

        string title = NotificationTranslations.Original(localizer, NotificationMessageKeys.ReseedAfterResetTitle);

        Assert.AreEqual("The database holds no quotes", title);
    }

    // ── #81's producer (rows 14–16) ──────────────────────────────────────────

    private static ChangelogDocument DocumentIn(string language, string version, params string[] highlights) => new()
    {
        Language = language,
        Releases =
        [
            new ChangelogRelease
            {
                Version            = version,
                AudienceHighlights = new Dictionary<string, List<string>> { ["notification"] = [.. highlights] },
            },
        ],
    };

    /// <summary>The body comes from that language's own changelog rows — already translated at source.</summary>
    [TestMethod]
    public void WhatsNew_TakesBodyTranslationsFromThePerLanguageChangelog()
    {
        ApiLocalizer localizer = new(I18nDir);
        Dictionary<string, ChangelogDocument> translated = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nl"] = DocumentIn("nl", "1.9.0", "Nederlandse hoogtepunt"),
            ["de"] = DocumentIn("de", "1.9.0", "Deutscher Höhepunkt"),
        };

        List<WhatsNewNotification.Seed> seeds = WhatsNewNotification.BuildSeeds(
            DocumentIn("en", "1.9.0", "English highlight"), lastActiveVersion: null, currentVersion: "1.9.0",
            localizer, translated);

        WhatsNewNotification.Seed seed = seeds.Single();

        Assert.AreEqual("English highlight", seed.Body);
        Assert.AreEqual("Nederlandse hoogtepunt", seed.Translations.Single(t => t.Language == "nl").Body);
        Assert.AreEqual("Deutscher Höhepunkt", seed.Translations.Single(t => t.Language == "de").Body);
    }

    /// <summary>A language the changelog lacks contributes no row, so the read path reports it untranslated.</summary>
    [TestMethod]
    public void WhatsNew_LanguageWithNoChangelogContent_WritesNoTranslationRow()
    {
        ApiLocalizer localizer = new(I18nDir);
        Dictionary<string, ChangelogDocument> translated = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nl"] = DocumentIn("nl", "1.9.0", "Nederlandse hoogtepunt"),
        };

        List<WhatsNewNotification.Seed> seeds = WhatsNewNotification.BuildSeeds(
            DocumentIn("en", "1.9.0", "English highlight"), lastActiveVersion: null, currentVersion: "1.9.0",
            localizer, translated);

        IReadOnlyList<NotificationTranslation> translations = seeds.Single().Translations;

        Assert.HasCount(1, translations);
        Assert.AreEqual("nl", translations[0].Language,
            "German had no changelog content, so it must contribute nothing rather than English text labelled 'de'.");
    }

    /// <summary>The title is not in the changelog — it resolves per language from the locale files, version substituted.</summary>
    [TestMethod]
    public void WhatsNew_TitleResolvesPerLanguageWithTheVersionSubstituted()
    {
        ApiLocalizer localizer = new(I18nDir);
        Dictionary<string, ChangelogDocument> translated = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nl"] = DocumentIn("nl", "1.9.0", "Nederlandse hoogtepunt"),
        };

        WhatsNewNotification.Seed seed = WhatsNewNotification.BuildSeeds(
            DocumentIn("en", "1.9.0", "English highlight"), lastActiveVersion: null, currentVersion: "1.9.0",
            localizer, translated).Single();

        Assert.AreEqual("What's new in v1.9.0", seed.Title);
        Assert.AreEqual("Nieuw in v1.9.0", seed.Translations.Single(t => t.Language == "nl").Title);
    }

    /// <summary>
    /// #289's overshoot notification carries both recorded schema versions, and each language's own
    /// template receives them — not one language's prose with numbers baked in at write time
    /// (requirement 7).
    /// </summary>
    [TestMethod]
    public void Read_SchemaVersionOvershoot_SubstitutesBothVersionsIntoEachLanguage()
    {
        ApiLocalizer localizer = new(I18nDir);

        IReadOnlyList<NotificationTranslation> translations = NotificationTranslations.Build(
            localizer,
            ApiMessages.NotificationSchemaOvershootTitle,
            ApiMessages.NotificationSchemaOvershootBody,
            bodyArgs: [14, 5]);

        Assert.IsNotEmpty(translations, "The overshoot notification must have translations to substitute into.");

        foreach (NotificationTranslation translation in translations)
        {
            Assert.Contains("14", translation.Body,
                $"The {translation.Language} body lost the data schema version.");
            Assert.Contains("5", translation.Body,
                $"The {translation.Language} body lost the application schema version.");
            Assert.DoesNotContain("{0}", translation.Body,
                $"The {translation.Language} body still carries an unsubstituted placeholder.");
            Assert.DoesNotContain("{1}", translation.Body,
                $"The {translation.Language} body still carries an unsubstituted placeholder.");
        }
    }
    /// <summary>Positional arguments substitute into each language's own template, not only English's.</summary>
    [TestMethod]
    public void Build_WithArguments_SubstitutesIntoEveryLanguage()
    {
        ApiLocalizer localizer = new(I18nDir);

        IReadOnlyList<NotificationTranslation> translations = NotificationTranslations.Build(
            localizer,
            ApiMessages.NotificationWhatsNewReleasedTitle,
            ApiMessages.NotificationOperationIdRenameBody,
            titleArgs: ["9.9.9"]);

        foreach (NotificationTranslation translation in translations)
            Assert.Contains("9.9.9", translation.Title!,
                $"The {translation.Language} title dropped its substituted version.");
    }
}
