namespace Quotinator.Core.Models;

/// <summary>
/// One line of a <see cref="ConversationResponse"/>. Exactly one of <see cref="Quote"/> or
/// <see cref="Text"/> is populated, matching <see cref="Type"/> (<c>"quote"</c> /
/// <c>"stage_direction"</c> / <c>"sound_cue"</c>).
/// </summary>
public sealed class ConversationLineResponse
{
    /// <summary>1-based position of this line within the conversation.</summary>
    public required int Order { get; init; }

    /// <summary>Which of <see cref="Quote"/>/<see cref="Text"/> is populated: <c>"quote"</c>, <c>"stage_direction"</c>, or <c>"sound_cue"</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Populated when <see cref="Type"/> is <c>"quote"</c>. Its own <see cref="QuoteResponse.Conversations"/> is always <c>null</c> — no recursive expansion.</summary>
    public QuoteResponse? Quote { get; init; }

    /// <summary>Populated when <see cref="Type"/> is <c>"stage_direction"</c> or <c>"sound_cue"</c>, in the language identified by <see cref="Language"/>.</summary>
    public string? Text { get; init; }

    /// <summary>Populated when <see cref="Type"/> is <c>"sound_cue"</c> and the cue has an audio file.</summary>
    public string? SoundFileUrl { get; init; }

    /// <summary>Populated when <see cref="Type"/> is <c>"stage_direction"</c> or <c>"sound_cue"</c> and the entry has an image.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>ISO 639-1 code of the language <see cref="Text"/> is in. Only set for <c>"stage_direction"</c>/<c>"sound_cue"</c> lines.</summary>
    public string? Language { get; init; }

    /// <summary><c>true</c> when <see cref="Text"/> is a translation rather than the original. Only set for <c>"stage_direction"</c>/<c>"sound_cue"</c> lines.</summary>
    public bool? IsTranslated { get; init; }
}
