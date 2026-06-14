namespace Quotinator.Core.Data.Enums;

/// <summary>The media category of a quote's source.</summary>
public enum QuoteType
{
    /// <summary>Unrecognised or absent value; used as a safe fallback when the stored string cannot be parsed.</summary>
    Unknown = 0,
    /// <summary>A feature film.</summary>
    Movie,
    /// <summary>A television series.</summary>
    Tv,
    /// <summary>An animated series or film.</summary>
    Anime,
    /// <summary>A book or other literary work.</summary>
    Book,
    /// <summary>A real person's direct quote or speech, not from a fictional character.</summary>
    Person
}
