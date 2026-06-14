using System.Globalization;
using System.Text.Json;

namespace Quotinator.Core.Services;

public interface IApiLocalizer
{
    string this[string key] { get; }
}

public sealed class ApiLocalizer : IApiLocalizer
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _tables;

    public ApiLocalizer(string i18nTextDir)
    {
        _tables = Directory
            .GetFiles(i18nTextDir, "UI.*.json")
            .ToDictionary(ExtractLang, LoadTable);
    }

    public string this[string key] => Resolve(key);

    private string Resolve(string key)
    {
        var culture = CultureInfo.CurrentUICulture;

        if (TryGet(culture.Name, key, out var v)) return v;
        if (TryGet(culture.TwoLetterISOLanguageName, key, out v)) return v;
        if (TryGet("en-GB", key, out v)) return v;
        return key;
    }

    private bool TryGet(string lang, string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
    {
        if (_tables.TryGetValue(lang, out var table) && table.TryGetValue(key, out var v))
        {
            value = v;
            return true;
        }
        value = null;
        return false;
    }

    private static string ExtractLang(string filePath) =>
        Path.GetFileNameWithoutExtension(filePath)[3..]; // "UI.en-GB" → "en-GB"

    private static IReadOnlyDictionary<string, string> LoadTable(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
    }
}
