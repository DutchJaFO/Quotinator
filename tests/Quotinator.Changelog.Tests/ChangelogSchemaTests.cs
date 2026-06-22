using System.Text.Json;
using System.Text.RegularExpressions;

namespace Quotinator.Changelog.Tests;

[TestClass]
public sealed class ChangelogSchemaTests
{
    private static readonly Regex CvePattern = new(@"^CVE-\d{4}-\d{4,}$", RegexOptions.Compiled);

    private static List<(string Filename, JsonDocument Doc)> _docs = [];

    [ClassInitialize]
    public static void LoadFiles(TestContext _)
    {
        foreach (var path in FindChangelogFiles())
        {
            var doc = JsonDocument.Parse(File.ReadAllText(path));
            _docs.Add((Path.GetFileName(path), doc));
        }
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        foreach (var (_, doc) in _docs)
            doc.Dispose();
        _docs.Clear();
    }

    [TestMethod]
    public void AtLeastOneChangelogFile_IsLoaded()
    {
        Assert.IsTrue(_docs.Count > 0,
            "No changelog.*.json files found in src/Quotinator.Api/resources/ — expected at least changelog.en.json.");
    }

    [TestMethod]
    public void AllFiles_HaveRequiredRootFields()
    {
        foreach (var (filename, doc) in _docs)
        {
            var root = doc.RootElement;

            Assert.IsTrue(root.TryGetProperty("language", out var lang) &&
                          lang.ValueKind == JsonValueKind.String &&
                          !string.IsNullOrWhiteSpace(lang.GetString()),
                $"{filename}: missing or empty 'language' property");

            Assert.IsTrue(root.TryGetProperty("sourceLanguage", out var sourceLang) &&
                          sourceLang.ValueKind == JsonValueKind.String &&
                          !string.IsNullOrWhiteSpace(sourceLang.GetString()),
                $"{filename}: missing or empty 'sourceLanguage' property");

            Assert.IsTrue(root.TryGetProperty("machineTranslated", out var mt) &&
                          (mt.ValueKind == JsonValueKind.True || mt.ValueKind == JsonValueKind.False),
                $"{filename}: missing or non-boolean 'machineTranslated' property");
        }
    }

    [TestMethod]
    public void AllReleases_HaveNonEmptyVersionAndDate()
    {
        foreach (var (filename, r) in Releases())
        {
            var version = r.GetProperty("version").GetString();
            var date    = r.GetProperty("date").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(version), $"{filename}: version is missing in entry: {r}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(date),    $"{filename}: date is missing in version {version}");
        }
    }

    [TestMethod]
    public void AllReleases_StringArrays_ContainNoNullEntries()
    {
        var arrayNames = new[] { "highlights", "added", "changed", "fixed", "removed" };
        foreach (var (filename, r) in Releases())
        {
            var version = r.GetProperty("version").GetString();
            foreach (var name in arrayNames)
            {
                if (!r.TryGetProperty(name, out var arr)) continue;
                foreach (var item in arr.EnumerateArray())
                    Assert.AreNotEqual(JsonValueKind.Null, item.ValueKind,
                        $"{filename}: null entry in '{name}' array for version {version}");
            }
        }
    }

    [TestMethod]
    public void AllReleases_CveEntries_MatchExpectedFormat()
    {
        foreach (var (filename, r) in Releases())
        {
            var version = r.GetProperty("version").GetString();
            if (!r.TryGetProperty("cves", out var cves)) continue;
            foreach (var cve in cves.EnumerateArray())
            {
                var id = cve.GetString();
                Assert.IsTrue(CvePattern.IsMatch(id ?? string.Empty),
                    $"{filename}: CVE ID '{id}' in version {version} does not match CVE-YYYY-NNNNN+ format");
            }
        }
    }

    [TestMethod]
    public void AllReleases_AudienceHighlights_ContainNoNullEntries()
    {
        foreach (var (filename, r) in Releases())
        {
            var version = r.GetProperty("version").GetString();
            if (!r.TryGetProperty("audienceHighlights", out var audienceHighlights)) continue;
            foreach (var audience in audienceHighlights.EnumerateObject())
            {
                foreach (var item in audience.Value.EnumerateArray())
                    Assert.AreNotEqual(JsonValueKind.Null, item.ValueKind,
                        $"{filename}: null entry in audienceHighlights.{audience.Name} for version {version}");
            }
        }
    }

    [TestMethod]
    public void SectionHeaders_WhenPresent_HaveNonEmptyValues()
    {
        var expectedKeys = new[] { "highlights", "added", "changed", "fixed", "removed" };
        foreach (var (filename, doc) in _docs)
        {
            if (!doc.RootElement.TryGetProperty("sectionHeaders", out var sectionHeaders)) continue;
            foreach (var key in expectedKeys)
            {
                if (!sectionHeaders.TryGetProperty(key, out var value)) continue;
                Assert.IsFalse(string.IsNullOrWhiteSpace(value.GetString()),
                    $"{filename}: sectionHeaders.{key} must not be empty when present");
            }
        }
    }

    private static IEnumerable<(string Filename, JsonElement Release)> Releases()
    {
        foreach (var (filename, doc) in _docs)
            foreach (var release in doc.RootElement.GetProperty("releases").EnumerateArray())
                yield return (filename, release);
    }

    private static IEnumerable<string> FindChangelogFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var resourceDir = Path.Combine(dir.FullName, "src", "Quotinator.Api", "resources");
            if (Directory.Exists(resourceDir))
            {
                var files = Directory.GetFiles(resourceDir, "changelog.*.json");
                if (files.Length > 0) return files;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "changelog.*.json not found — expected at src/Quotinator.Api/resources/ under the repo root.");
    }
}
