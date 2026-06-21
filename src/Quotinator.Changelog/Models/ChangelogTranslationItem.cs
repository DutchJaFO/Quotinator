namespace Quotinator.Changelog.Models;

/// <summary>A single translated string within a release translation block.</summary>
public sealed class ChangelogTranslationItem
{
    /// <summary>The translated text.</summary>
    public string? Text { get; init; }

    /// <summary>
    /// Whether the translation was machine-generated.
    /// <see langword="null"/> when not specified — the caller applies a context-specific default.
    /// <see langword="true"/> when machine-generated. <see langword="false"/> when verified by a native speaker.
    /// </summary>
    public bool? MachineTranslated { get; init; }
}
