using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Changelog.Services;

namespace Quotinator.Changelog.Tests;

[TestClass]
public sealed class ChangelogServiceTests
{
    private static IChangelogService Build(string dir) =>
        new ChangelogService(dir, NullLogger<ChangelogService>.Instance);

    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string dir, string language, string sourceLanguage = "en", bool machineTranslated = false)
    {
        var json = $$"""
            {
              "language": "{{language}}",
              "sourceLanguage": "{{sourceLanguage}}",
              "machineTranslated": {{(machineTranslated ? "true" : "false")}},
              "releases": [
                {
                  "version": "1.0.0",
                  "date": "2024-01-01",
                  "highlights": ["Test release."]
                }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(dir, $"changelog.{language}.json"), json);
    }

    [TestMethod]
    public void GetForCulture_En_ReturnsEnDocument()
    {
        var dir = TempDir();
        try
        {
            WriteFile(dir, "en");
            var doc = Build(dir).GetForCulture("en");
            Assert.IsNotNull(doc);
            Assert.AreEqual("en", doc.Language);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void GetForCulture_Null_ReturnsEnDocument()
    {
        var dir = TempDir();
        try
        {
            WriteFile(dir, "en");
            var doc = Build(dir).GetForCulture(null);
            Assert.IsNotNull(doc);
            Assert.AreEqual("en", doc.Language);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void GetForCulture_LongCulture_NormalisesToBaseCode()
    {
        var dir = TempDir();
        try
        {
            WriteFile(dir, "en");
            var doc = Build(dir).GetForCulture("en-GB");
            Assert.IsNotNull(doc);
            Assert.AreEqual("en", doc.Language);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void GetForCulture_RequestedLanguageExists_ReturnsCorrectDocument()
    {
        var dir = TempDir();
        try
        {
            WriteFile(dir, "en");
            WriteFile(dir, "nl", sourceLanguage: "en", machineTranslated: true);
            var doc = Build(dir).GetForCulture("nl");
            Assert.IsNotNull(doc);
            Assert.AreEqual("nl", doc.Language);
            Assert.IsTrue(doc.MachineTranslated);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void GetForCulture_RequestedLanguageAbsent_FallsBackToEn()
    {
        var dir = TempDir();
        try
        {
            WriteFile(dir, "en");
            var doc = Build(dir).GetForCulture("nl");
            Assert.IsNotNull(doc, "Expected en fallback when nl is absent.");
            Assert.AreEqual("en", doc.Language);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void GetForCulture_NeitherRequestedNorEnExists_ReturnsNull()
    {
        var dir = TempDir();
        try
        {
            WriteFile(dir, "nl");
            var doc = Build(dir).GetForCulture("fr");
            Assert.IsNull(doc, "Expected null when neither requested language nor en fallback exists.");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void AvailableLanguages_ReflectsLoadedFileCount()
    {
        var dir = TempDir();
        try
        {
            WriteFile(dir, "en");
            WriteFile(dir, "nl");
            Assert.AreEqual(2, Build(dir).AvailableLanguages.Count);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void Constructor_NonExistentDirectory_AvailableLanguagesEmpty()
    {
        var service = Build(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.AreEqual(0, service.AvailableLanguages.Count);
    }

    [TestMethod]
    public void Constructor_NonExistentDirectory_GetForCultureReturnsNull()
    {
        var service = Build(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.IsNull(service.GetForCulture("en"));
    }
}
