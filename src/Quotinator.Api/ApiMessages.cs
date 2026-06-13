namespace Quotinator.Api;

/// <summary>Localised user-facing strings for API error responses.</summary>
internal static class ApiMessages
{
    // Keys
    internal const string SearchQueryRequired   = "search.q.required";
    internal const string SearchQueryTooLong    = "search.q.tooLong";
    internal const string LimitOutOfRange       = "search.limit.invalid";
    internal const string RandomNOutOfRange     = "random.n.invalid";
    internal const string PageOutOfRange        = "quotes.page.invalid";
    internal const string PageSizeOutOfRange    = "quotes.pageSize.invalid";
    internal const string QuoteNotFound         = "quotes.notFound";
    internal const string LangInvalid           = "lang.invalid";
    internal const string TypeInvalid           = "type.invalid";
    internal const string TooManyRequests       = "ratelimit.exceeded";

    private static readonly Dictionary<string, Dictionary<string, string>> Messages = new()
    {
        [SearchQueryRequired] = new()
        {
            ["en"]    = "The q parameter is required. Provide a search term.",
            ["en-GB"] = "The q parameter is required. Provide a search term.",
            ["de"]    = "Der Parameter q ist erforderlich. Gib einen Suchbegriff ein.",
            ["nl"]    = "De parameter q is verplicht. Geef een zoekterm op.",
        },
        [SearchQueryTooLong] = new()
        {
            ["en"]    = "The search term must not exceed 200 characters.",
            ["en-GB"] = "The search term must not exceed 200 characters.",
            ["de"]    = "Der Suchbegriff darf nicht länger als 200 Zeichen sein.",
            ["nl"]    = "De zoekterm mag niet langer zijn dan 200 tekens.",
        },
        [LangInvalid] = new()
        {
            ["en"]    = "lang must be a valid ISO 639-1 language code (e.g. en, nl, de).",
            ["en-GB"] = "lang must be a valid ISO 639-1 language code (e.g. en, nl, de).",
            ["de"]    = "lang muss ein gültiger ISO-639-1-Sprachcode sein (z. B. en, nl, de).",
            ["nl"]    = "lang moet een geldige ISO 639-1-taalcode zijn (bijv. en, nl, de).",
        },
        [TypeInvalid] = new()
        {
            ["en"]    = "type must be one of: movie, tv, anime, book, person.",
            ["en-GB"] = "type must be one of: movie, tv, anime, book, person.",
            ["de"]    = "type muss einer der folgenden Werte sein: movie, tv, anime, book, person.",
            ["nl"]    = "type moet een van de volgende waarden zijn: movie, tv, anime, book, person.",
        },
        [TooManyRequests] = new()
        {
            ["en"]    = "Too many requests. Please slow down.",
            ["en-GB"] = "Too many requests. Please slow down.",
            ["de"]    = "Zu viele Anfragen. Bitte langsamer.",
            ["nl"]    = "Te veel verzoeken. Gelieve rustiger aan te doen.",
        },
        [LimitOutOfRange] = new()
        {
            ["en"]    = "limit must be a whole number between 1 and 100.",
            ["en-GB"] = "limit must be a whole number between 1 and 100.",
            ["de"]    = "limit muss eine ganze Zahl zwischen 1 und 100 sein.",
            ["nl"]    = "limit moet een geheel getal zijn tussen 1 en 100.",
        },
        [RandomNOutOfRange] = new()
        {
            ["en"]    = "n must be a whole number between 1 and 100.",
            ["en-GB"] = "n must be a whole number between 1 and 100.",
            ["de"]    = "n muss eine ganze Zahl zwischen 1 und 100 sein.",
            ["nl"]    = "n moet een geheel getal zijn tussen 1 en 100.",
        },
        [PageOutOfRange] = new()
        {
            ["en"]    = "page must be a whole number of 1 or greater.",
            ["en-GB"] = "page must be a whole number of 1 or greater.",
            ["de"]    = "page muss eine ganze Zahl von 1 oder größer sein.",
            ["nl"]    = "page moet een geheel getal zijn van 1 of groter.",
        },
        [PageSizeOutOfRange] = new()
        {
            ["en"]    = "pageSize must be a whole number between 1 and 100.",
            ["en-GB"] = "pageSize must be a whole number between 1 and 100.",
            ["de"]    = "pageSize muss eine ganze Zahl zwischen 1 und 100 sein.",
            ["nl"]    = "pageSize moet een geheel getal zijn tussen 1 en 100.",
        },
        [QuoteNotFound] = new()
        {
            ["en"]    = "No quote with the requested ID was found.",
            ["en-GB"] = "No quote with the requested ID was found.",
            ["de"]    = "Es wurde kein Zitat mit der angegebenen ID gefunden.",
            ["nl"]    = "Er is geen citaat gevonden met het opgegeven ID.",
        },
    };

    /// <summary>Returns the message for <paramref name="key"/> in the best available language.</summary>
    internal static string Get(string key, string? lang)
    {
        if (!Messages.TryGetValue(key, out var translations))
            return key;

        if (lang is not null)
        {
            if (translations.TryGetValue(lang, out var exact))
                return exact;

            // en-GB → try "en" prefix
            var prefix = lang.Split('-')[0];
            if (translations.TryGetValue(prefix, out var fallback))
                return fallback;
        }

        return translations.GetValueOrDefault("en") ?? key;
    }
}
