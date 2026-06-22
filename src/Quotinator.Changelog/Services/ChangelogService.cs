using System.Text.Json;
using Microsoft.Extensions.Logging;
using Quotinator.Changelog.Models;

namespace Quotinator.Changelog.Services;

/// <summary>
/// Loads all <c>changelog.*.json</c> files from the resource directory at startup and provides
/// culture-resolved changelog documents.
/// </summary>
public sealed class ChangelogService : IChangelogService
{
    #region Public

    /// <inheritdoc/>
    public IReadOnlyList<string> AvailableLanguages { get; }

    /// <inheritdoc/>
    public ChangelogDocument? GetForCulture(string? culture)
    {
        var code = Normalise(culture);

        if (_documents.TryGetValue(code, out var document))
            return document;

        if (_documents.TryGetValue("en", out var english))
        {
            _logger.LogInformation(
                "[Changelog - Resolve] Language '{Requested}' not available — falling back to 'en'", code);
            return english;
        }

        _logger.LogWarning(
            "[Changelog - Resolve] Language '{Requested}' not available and no 'en' fallback found", code);
        return null;
    }

    #endregion

    #region Private

    private readonly Dictionary<string, ChangelogDocument> _documents;
    private readonly ILogger<ChangelogService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Initialises the service. Enumerates <c>changelog.*.json</c> in <paramref name="resourceDirectory"/>,
    /// parses each file, and keys the result by the JSON <c>language</c> property.
    /// Files that fail to parse or lack a <c>language</c> property are skipped with a warning.
    /// A warning is also logged when the filename disagrees with the <c>language</c> property —
    /// both could be wrong; investigation is required.
    /// </summary>
    public ChangelogService(string resourceDirectory, ILogger<ChangelogService> logger)
    {
        _logger = logger;
        _documents = new Dictionary<string, ChangelogDocument>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(resourceDirectory))
        {
            logger.LogWarning("[Changelog - Load] Resource directory not found: {Directory}", resourceDirectory);
            AvailableLanguages = [];
            return;
        }

        foreach (var file in Directory.GetFiles(resourceDirectory, "changelog.*.json"))
        {
            var filename = Path.GetFileName(file);
            ChangelogRoot? root;
            try
            {
                root = JsonSerializer.Deserialize<ChangelogRoot>(File.ReadAllText(file), JsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Changelog - Load] Failed to parse {File} — skipped", filename);
                continue;
            }

            if (root is null)
            {
                logger.LogWarning("[Changelog - Load] {File} deserialised to null — skipped", filename);
                continue;
            }

            if (string.IsNullOrWhiteSpace(root.Language))
            {
                logger.LogWarning("[Changelog - Load] {File} has no 'language' property — skipped", filename);
                continue;
            }

            var expectedFilename = $"changelog.{root.Language}.json";
            if (!string.Equals(filename, expectedFilename, StringComparison.OrdinalIgnoreCase))
                logger.LogWarning(
                    "[Changelog - Load] Filename '{Actual}' disagrees with language property '{Language}' — language property wins; investigation required",
                    filename, root.Language);

            var releases = (root.Releases ?? [])
                .Where(r => !string.IsNullOrEmpty(r.Version) && !string.IsNullOrEmpty(r.Date))
                .ToList();

            _documents[root.Language] = new ChangelogDocument
            {
                Language          = root.Language,
                MachineTranslated = root.MachineTranslated,
                Unreleased        = root.Unreleased,
                Releases          = releases,
                SectionHeaders    = root.SectionHeaders
            };

            logger.LogDebug("[Changelog - Load] Loaded {File} ({Language}, {Count} release(s))",
                filename, root.Language, releases.Count);
        }

        AvailableLanguages = [.. _documents.Keys];
        logger.LogInformation("[Changelog - Load] {Count} language file(s) loaded: {Languages}",
            AvailableLanguages.Count, string.Join(", ", AvailableLanguages));
    }

    private static string Normalise(string? culture) =>
        culture is { Length: > 2 } ? culture[..2] : culture ?? "en";

    #endregion
}
