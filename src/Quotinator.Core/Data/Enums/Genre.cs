namespace Quotinator.Core.Data.Enums;

/// <summary>Genre tags used to classify and filter quotes.</summary>
public enum Genre
{
    /// <summary>Unrecognised or absent value; used as a safe fallback when the stored string cannot be parsed.</summary>
    Unknown = 0,
    /// <summary>Action.</summary>
    Action,
    /// <summary>Adventure.</summary>
    Adventure,
    /// <summary>Animation.</summary>
    Animation,
    /// <summary>Comedy.</summary>
    Comedy,
    /// <summary>Drama.</summary>
    Drama,
    /// <summary>Fantasy.</summary>
    Fantasy,
    /// <summary>Fiction.</summary>
    Fiction,
    /// <summary>Horror.</summary>
    Horror,
    /// <summary>Mystery.</summary>
    Mystery,
    /// <summary>Non-fiction.</summary>
    NonFiction,
    /// <summary>Romance.</summary>
    Romance,
    /// <summary>Science fiction.</summary>
    SciFi,
    /// <summary>Thriller.</summary>
    Thriller
}
