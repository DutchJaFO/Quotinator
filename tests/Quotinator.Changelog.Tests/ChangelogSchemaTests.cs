using System.Text.Json;
using System.Text.RegularExpressions;

namespace Quotinator.Changelog.Tests;

[TestClass]
public sealed class ChangelogSchemaTests
{
    private static readonly Regex CvePattern = new(@"^CVE-\d{4}-\d{4,}$", RegexOptions.Compiled);

    private static JsonDocument _doc = null!;
    private static JsonElement.ArrayEnumerator _releases;

    [ClassInitialize]
    public static void LoadFile(TestContext _)
    {
        var path = FindChangelogJson();
        _doc = JsonDocument.Parse(File.ReadAllText(path));
        _releases = _doc.RootElement.GetProperty("releases").EnumerateArray();
    }

    [ClassCleanup]
    public static void Cleanup() => _doc.Dispose();

    [TestMethod]
    public void AllReleases_HaveNonEmptyVersionAndDate()
    {
        foreach (var r in Releases())
        {
            var version = r.GetProperty("version").GetString();
            var date    = r.GetProperty("date").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(version), $"version is missing in entry: {r}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(date),    $"date is missing in version {version}");
        }
    }

    [TestMethod]
    public void AllReleases_StringArrays_ContainNoNullEntries()
    {
        var arrayNames = new[] { "highlights", "added", "changed", "fixed", "removed" };
        foreach (var r in Releases())
        {
            var version = r.GetProperty("version").GetString();
            foreach (var name in arrayNames)
            {
                if (!r.TryGetProperty(name, out var arr)) continue;
                foreach (var item in arr.EnumerateArray())
                    Assert.AreNotEqual(JsonValueKind.Null, item.ValueKind,
                        $"null entry in '{name}' array for version {version}");
            }
        }
    }

    [TestMethod]
    public void AllReleases_CveEntries_MatchExpectedFormat()
    {
        foreach (var r in Releases())
        {
            var version = r.GetProperty("version").GetString();
            if (!r.TryGetProperty("cves", out var cves)) continue;
            foreach (var cve in cves.EnumerateArray())
            {
                var id = cve.GetString();
                Assert.IsTrue(CvePattern.IsMatch(id ?? string.Empty),
                    $"CVE ID '{id}' in version {version} does not match CVE-YYYY-NNNNN+ format");
            }
        }
    }

    [TestMethod]
    public void AllReleases_TranslationItems_HaveNonEmptyText()
    {
        var sectionNames = new[] { "highlights", "added", "changed", "fixed", "removed" };
        foreach (var r in Releases())
        {
            var version = r.GetProperty("version").GetString();
            if (!r.TryGetProperty("translations", out var translations)) continue;
            foreach (var lang in translations.EnumerateObject())
            {
                foreach (var section in sectionNames)
                {
                    if (!lang.Value.TryGetProperty(section, out var items)) continue;
                    foreach (var item in items.EnumerateArray())
                    {
                        Assert.AreEqual(JsonValueKind.Object, item.ValueKind,
                            $"translations.{lang.Name}.{section} item in version {version} must be an object with a 'text' property");
                        Assert.IsTrue(item.TryGetProperty("text", out var text),
                            $"translations.{lang.Name}.{section} item in version {version} is missing required 'text' property");
                        Assert.IsFalse(string.IsNullOrWhiteSpace(text.GetString()),
                            $"translations.{lang.Name}.{section} item in version {version} has empty 'text'");
                        if (item.TryGetProperty("machineTranslated", out var mt))
                            Assert.IsTrue(mt.ValueKind is JsonValueKind.True or JsonValueKind.False,
                                $"translations.{lang.Name}.{section} item in version {version}: machineTranslated must be a boolean");
                    }
                }
            }
        }
    }

    [TestMethod]
    public void SectionHeaders_WhenPresent_HaveNonEmptyValues()
    {
        if (!_doc.RootElement.TryGetProperty("sectionHeaders", out var sectionHeaders)) return;
        var expectedKeys = new[] { "highlights", "added", "changed", "fixed", "removed" };
        foreach (var lang in sectionHeaders.EnumerateObject())
        {
            foreach (var key in expectedKeys)
            {
                if (!lang.Value.TryGetProperty(key, out var value)) continue;
                Assert.IsFalse(string.IsNullOrWhiteSpace(value.GetString()),
                    $"sectionHeaders.{lang.Name}.{key} must not be empty when present");
            }
        }
    }

    private static IEnumerable<JsonElement> Releases()
        => _doc.RootElement.GetProperty("releases").EnumerateArray().ToList();

    private static string FindChangelogJson()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Quotinator.Api", "changelog.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "changelog.json not found — expected at src/Quotinator.Api/changelog.json under the repo root.");
    }
}
