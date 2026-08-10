namespace Quotinator.Core.Queries;

/// <summary>Read model for a single translation-resolved SoundCue, used by a Conversation line lookup.</summary>
public sealed record SoundCueLineRow(string Id, string Text, string? SoundFileUrl, string? ImageUrl, string EffectiveLanguage);
