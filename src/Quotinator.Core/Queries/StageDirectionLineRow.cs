namespace Quotinator.Core.Queries;

/// <summary>Read model for a single translation-resolved StageDirection, used by a Conversation line lookup.</summary>
public sealed record StageDirectionLineRow(string Id, string Text, string? ImageUrl, string EffectiveLanguage);
