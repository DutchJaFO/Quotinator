namespace Quotinator.Api;

/// <summary>Keys for localised API error messages stored in i18ntext/UI.*.json.</summary>
internal static class ApiMessages
{
    internal const string SearchQueryRequired = "ErrorSearchQueryRequired";
    internal const string SearchQueryTooLong  = "ErrorSearchQueryTooLong";
    internal const string LimitOutOfRange     = "ErrorLimitOutOfRange";
    internal const string RandomNOutOfRange   = "ErrorRandomNOutOfRange";
    internal const string PageOutOfRange      = "ErrorPageOutOfRange";
    internal const string PageSizeOutOfRange  = "ErrorPageSizeOutOfRange";
    internal const string QuoteNotFound       = "ErrorQuoteNotFound";
    internal const string LangInvalid         = "ErrorLangInvalid";
    internal const string TypeInvalid         = "ErrorTypeInvalid";
    internal const string TooManyRequests     = "ErrorTooManyRequests";
}
