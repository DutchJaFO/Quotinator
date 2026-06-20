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

    [TestMethod]
    public void GeneratedChangelog_MinusNotice_MatchesReference()
    {
        var repoRoot  = FindRepoRoot();
        var generated = File.ReadAllText(Path.Combine(repoRoot, "CHANGELOG.md"));
        var reference = File.ReadAllText(Path.Combine(repoRoot, "scripts", "changelog-reference", "CHANGELOG.md"));

        var withoutNotice = StripGeneratedNotice(generated);

        Assert.AreEqual(reference, withoutNotice,
            "CHANGELOG.md content differs from the reference snapshot. " +
            "If changelog.json was updated, re-run: dotnet-script scripts/changelog.csx -- --format keepachangelog --output CHANGELOG.md " +
            "then update the reference: tail -n +3 CHANGELOG.md > scripts/changelog-reference/CHANGELOG.md");
    }

    private static string StripGeneratedNotice(string content)
    {
        const string noticePrefix = "# GENERATED FILE";
        if (!content.StartsWith(noticePrefix, StringComparison.Ordinal)) return content;

        var afterNotice = content.IndexOf('\n');
        if (afterNotice < 0) return content;

        var rest = content[(afterNotice + 1)..];
        return rest.StartsWith("\r\n", StringComparison.Ordinal) ? rest[2..]
             : rest.StartsWith("\n",   StringComparison.Ordinal) ? rest[1..]
             : rest;
    }

    private static IEnumerable<JsonElement> Releases()
        => _doc.RootElement.GetProperty("releases").EnumerateArray().ToList();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Quotinator.Api", "changelog.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Repo root not found from " + AppContext.BaseDirectory);
    }

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
