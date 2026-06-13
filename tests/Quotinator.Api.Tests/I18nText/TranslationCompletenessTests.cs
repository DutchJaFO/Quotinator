using System.Text.Json;

namespace Quotinator.Api.Tests.I18nText;

/// <summary>Verifies that every language file contains exactly the same keys as the English baseline.</summary>
/// <remarks>
/// A failing test here means a string was added to UI.en.json (or another file) without
/// updating all other language files. Fix by adding the missing key(s) to the reported file.
/// </remarks>
[TestClass]
public class TranslationCompletenessTests
{
    private static readonly string I18nTextDir =
        Path.Combine(AppContext.BaseDirectory, "i18ntext");

    private static readonly string BaselineFile = Path.Combine(I18nTextDir, "UI.en.json");

    private static IReadOnlySet<string> LoadKeys(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
    }

    /// <summary>Every language file must have exactly the same keys as <c>UI.en.json</c>.</summary>
    [TestMethod]
    public void AllLanguageFiles_HaveExactlyTheSameKeysAsBaseline()
    {
        var baselineKeys = LoadKeys(BaselineFile);
        var languageFiles = Directory.GetFiles(I18nTextDir, "UI.*.json")
            .Where(f => f != BaselineFile)
            .ToList();

        Assert.IsNotEmpty(languageFiles, "No language files found — did the i18ntext files get copied to the output directory?");

        var failures = new List<string>();

        foreach (var file in languageFiles)
        {
            var lang = Path.GetFileName(file);
            var keys = LoadKeys(file);

            var missing = baselineKeys.Except(keys).OrderBy(k => k).ToList();
            var extra   = keys.Except(baselineKeys).OrderBy(k => k).ToList();

            if (missing.Count > 0)
                failures.Add($"{lang}: missing keys: {string.Join(", ", missing)}");
            if (extra.Count > 0)
                failures.Add($"{lang}: unexpected extra keys: {string.Join(", ", extra)}");
        }

        Assert.IsEmpty(failures,
            $"Translation file(s) have key mismatches:\n{string.Join("\n", failures)}");
    }

    /// <summary>No translation value may be blank.</summary>
    [TestMethod]
    public void AllLanguageFiles_HaveNoEmptyValues()
    {
        var files = Directory.GetFiles(I18nTextDir, "UI.*.json");
        var failures = new List<string>();

        foreach (var file in files)
        {
            var lang = Path.GetFileName(file);
            using var doc = JsonDocument.Parse(File.ReadAllText(file));

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                    failures.Add($"{lang}: key '{prop.Name}' has an empty value");
            }
        }

        Assert.IsEmpty(failures,
            $"Translation file(s) contain empty values:\n{string.Join("\n", failures)}");
    }
}
