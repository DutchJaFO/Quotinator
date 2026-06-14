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
            """{"Greeting": "Hello", "OnlyInEnglish": "English only"}""");
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
}
