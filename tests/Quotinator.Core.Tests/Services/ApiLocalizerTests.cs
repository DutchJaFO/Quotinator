using System.Globalization;
using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Services;

[TestClass]
public class ApiLocalizerTests
{
    private string _dir = string.Empty;
    private CultureInfo _savedCulture = CultureInfo.CurrentUICulture;

    [TestInitialize]
    public void Setup()
    {
        _savedCulture = CultureInfo.CurrentUICulture;

        _dir = Path.Combine(Path.GetTempPath(), $"quotinator-localizer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);

        File.WriteAllText(Path.Combine(_dir, "UI.en-GB.json"),
            """{"Greeting": "Hello", "OnlyInEnglish": "English only", "Farewell": "Bye {0}, see you {1}"}""");
        File.WriteAllText(Path.Combine(_dir, "UI.nl.json"),
            """{"Greeting": "Hallo"}""");
        File.WriteAllText(Path.Combine(_dir, "UI.de.json"),
            """{"Greeting": "Hallo"}""");
    }

    [TestCleanup]
    public void Cleanup()
    {
        CultureInfo.CurrentUICulture = _savedCulture;
        Directory.Delete(_dir, recursive: true);
    }

    [TestMethod]
    public void Resolve_ExactCultureMatch_ReturnsTranslation()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("nl");
        var localizer = new ApiLocalizer(_dir);

        Assert.AreEqual("Hallo", localizer["Greeting"]);
    }

    [TestMethod]
    public void Resolve_TwoLetterFallback_ReturnsTranslation()
    {
        // "nl-BE" has no file → falls back to "nl"
        CultureInfo.CurrentUICulture = new CultureInfo("nl-BE");
        var localizer = new ApiLocalizer(_dir);

        Assert.AreEqual("Hallo", localizer["Greeting"]);
    }

    [TestMethod]
    public void Resolve_NoMatchForCulture_FallsBackToEnglish()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("fr");
        var localizer = new ApiLocalizer(_dir);

        Assert.AreEqual("Hello", localizer["Greeting"]);
    }

    [TestMethod]
    public void Resolve_KeyAbsentFromTranslation_FallsBackToEnglish()
    {
        // "OnlyInEnglish" key does not exist in nl.json → falls back to en-GB
        CultureInfo.CurrentUICulture = new CultureInfo("nl");
        var localizer = new ApiLocalizer(_dir);

        Assert.AreEqual("English only", localizer["OnlyInEnglish"]);
    }

    [TestMethod]
    public void Resolve_KeyNotFoundAnywhere_ReturnsKey()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");
        var localizer = new ApiLocalizer(_dir);

        Assert.AreEqual("NonExistentKey", localizer["NonExistentKey"]);
    }

    [TestMethod]
    public void Resolve_EnglishCulture_ReturnsEnglishValue()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");
        var localizer = new ApiLocalizer(_dir);

        Assert.AreEqual("Hello", localizer["Greeting"]);
    }

    [TestMethod]
    public void Format_ValidSubstitution_ReplacesPlaceholdersByPosition()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");
        var localizer = new ApiLocalizer(_dir);

        Assert.AreEqual("Bye Alice, see you Bob", localizer.Format("Farewell", "Alice", "Bob"));
    }

    /// <summary>
    /// #229: unlike <c>string.Format</c>, a placeholder with no matching argument must never throw
    /// <see cref="FormatException"/> — the resolved template's content depends on the request's own
    /// <c>Accept-Language</c> header, so a translation-file placeholder-count typo must never be able
    /// to turn into a live 500.
    /// </summary>
    [TestMethod]
    public void Format_FewerArgumentsThanPlaceholders_LeavesUnmatchedPlaceholderLiteralInsteadOfThrowing()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");
        var localizer = new ApiLocalizer(_dir);

        Assert.AreEqual("Bye Alice, see you {1}", localizer.Format("Farewell", "Alice"));
    }

    [TestMethod]
    public void Format_MoreArgumentsThanPlaceholders_IgnoresExtraArguments()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");
        var localizer = new ApiLocalizer(_dir);

        Assert.AreEqual("Bye Alice, see you Bob", localizer.Format("Farewell", "Alice", "Bob", "Carol"));
    }

    /// <summary>An argument whose own value looks like a placeholder (e.g. <c>"{1}"</c>) must never be re-substituted — substitution is a single pass over the original template, not a sequence of independent replacements.</summary>
    [TestMethod]
    public void Format_ArgumentValueLooksLikeAPlaceholder_IsNotRecursivelySubstituted()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");
        var localizer = new ApiLocalizer(_dir);

        Assert.AreEqual("Bye {1}, see you Bob", localizer.Format("Farewell", "{1}", "Bob"));
    }
}
