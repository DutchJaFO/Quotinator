namespace Quotinator.Changelog.Models;

/// <summary>A single translated string within a release translation block.</summary>
/// <param name="Text">The translated text.</param>
/// <param name="MachineTranslated">
/// Whether the translation was machine-generated.
/// <see langword="null"/> when not specified in the source JSON — the caller applies a context-specific default.
/// <see langword="true"/> when machine-generated. <see langword="false"/> when verified by a native speaker.
/// </param>
public sealed record ChangelogTranslationItem(string Text, bool? MachineTranslated);
