using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Quotinator.Core.Services;

/// <summary>Looks up localised API error messages by key, using the current UI culture.</summary>
public interface IApiLocalizer
{
    /// <summary>Returns the localised message for <paramref name="key"/>, falling back through the culture hierarchy to the en-GB baseline.</summary>
    string this[string key] { get; }

    /// <summary>
    /// Returns the localised message for <paramref name="key"/> with its <c>{0}</c>/<c>{1}</c>-style
    /// placeholders substituted from <paramref name="args"/>, by position. Never throws regardless of
    /// a placeholder/argument-count mismatch — a placeholder with no matching argument is left as
    /// literal text rather than raising <see cref="FormatException"/> — since the resolved template's
    /// content depends on the request's own <c>Accept-Language</c> header (which of the 3 translation
    /// files gets consulted), so a translation-file typo must never be able to turn into a live 500.
    /// Use this instead of <c>string.Format(localizer[key], args)</c> everywhere a localised message
    /// needs substitution (CodeQL <c>cs/uncontrolled-format-string</c>, #229) — see <c>ImportEndpoints</c>
    /// and <c>EntityFilterParsing</c> for the call sites this replaces.
    /// </summary>
    string Format(string key, params object[] args);
}

/// <summary>
/// Shared, non-throwing <c>{0}</c>/<c>{1}</c>-style positional substitution for
/// <see cref="IApiLocalizer.Format"/> — single-pass regex substitution, not <c>string.Format</c>, so a
/// substituted argument's own value (e.g. <c>"{1}"</c>) is never re-matched, and a placeholder with no
/// matching argument is left as literal text instead of throwing. Every <see cref="IApiLocalizer"/>
/// implementation (including test fakes) calls this from its own <c>Format</c> method rather than
/// reimplementing the substitution logic.
/// </summary>
public static partial class ApiLocalizerFormatting
{
    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex PlaceholderPattern();

    /// <summary>Substitutes <c>{0}</c>/<c>{1}</c>-style placeholders in <paramref name="template"/> from <paramref name="args"/>, by position.</summary>
    public static string Substitute(string template, params object[] args)
        => PlaceholderPattern().Replace(template, m =>
        {
            var index = int.Parse(m.Groups[1].Value);
            return index < args.Length ? args[index]?.ToString() ?? string.Empty : m.Value;
        });
}

/// <summary>
/// Reads all <c>UI.*.json</c> localisation files at startup and resolves strings
/// against <see cref="CultureInfo.CurrentUICulture"/> at call time.
/// </summary>
/// <remarks>Initialises the localizer by loading all <c>UI.*.json</c> files from <paramref name="i18nTextDir"/>.</remarks>
/// <param name="i18nTextDir">Directory that contains the <c>UI.*.json</c> translation files.</param>
public sealed class ApiLocalizer(string i18nTextDir) : IApiLocalizer
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _tables = Directory
            .GetFiles(i18nTextDir, "UI.*.json")
            .ToDictionary(ExtractLang, LoadTable);

    /// <inheritdoc/>
    public string this[string key] => Resolve(key);

    /// <inheritdoc/>
    public string Format(string key, params object[] args) => ApiLocalizerFormatting.Substitute(Resolve(key), args);

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
