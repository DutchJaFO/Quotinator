using System.Globalization;
using System.Text.Json;

namespace Quotinator.Core.Services;

/// <summary>Looks up localised API error messages by key, using the current UI culture.</summary>
public interface IApiLocalizer
{
    /// <summary>Returns the localised message for <paramref name="key"/>, falling back through the culture hierarchy to the en-GB baseline.</summary>
    string this[string key] { get; }
}

/// <summary>
/// Reads all <c>UI.*.json</c> localisation files at startup and resolves strings
/// against <see cref="CultureInfo.CurrentUICulture"/> at call time.
/// </summary>
public sealed class ApiLocalizer : IApiLocalizer
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _tables;

    /// <summary>Initialises the localizer by loading all <c>UI.*.json</c> files from <paramref name="i18nTextDir"/>.</summary>
    /// <param name="i18nTextDir">Directory that contains the <c>UI.*.json</c> translation files.</param>
    public ApiLocalizer(string i18nTextDir)
    {
        _tables = Directory
            .GetFiles(i18nTextDir, "UI.*.json")
            .ToDictionary(ExtractLang, LoadTable);
    }

    /// <inheritdoc/>
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
