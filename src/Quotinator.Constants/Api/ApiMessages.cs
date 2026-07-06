namespace Quotinator.Constants.Api;

/// <summary>Keys for localised API error messages stored in i18ntext/UI.*.json.</summary>
public static class ApiMessages
{
    public const string SearchQueryRequired  = "ErrorSearchQueryRequired";
    public const string SearchQueryTooLong   = "ErrorSearchQueryTooLong";
    public const string LimitOutOfRange      = "ErrorLimitOutOfRange";
    public const string RandomNOutOfRange    = "ErrorRandomNOutOfRange";
    public const string PageOutOfRange       = "ErrorPageOutOfRange";
    public const string PageSizeOutOfRange   = "ErrorPageSizeOutOfRange";
    public const string QuoteNotFound        = "ErrorQuoteNotFound";
    public const string LangInvalid          = "ErrorLangInvalid";
    public const string TypeInvalid          = "ErrorTypeInvalid";
    public const string TooManyRequests      = "ErrorTooManyRequests";
    public const string FieldInvalid         = "ErrorFieldInvalid";
    public const string GenreInvalid         = "ErrorGenreInvalid";
    public const string NoQuotesMatchFilters = "InfoNoQuotesMatchFilters";
    public const string FilterInputTooLong   = "ErrorFilterInputTooLong";
    public const string FilterInputInvalid   = "ErrorFilterInputInvalid";
    public const string DecadeInvalid              = "ErrorDecadeInvalid";
    public const string YearRangeInvalid           = "ErrorYearRangeInvalid";
    public const string NumericParameterInvalid    = "ErrorNumericParameterInvalid";
    public const string YearParamNotInteger        = "ErrorYearParamNotInteger";
    public const string SeedFileMissing            = "ErrorSeedFileMissing";
    public const string SeedFileInvalidJson        = "ErrorSeedFileInvalidJson";
    public const string ImportFileMissing          = "ErrorImportFileMissing";
    public const string ImportSettingsInvalid      = "ErrorImportSettingsInvalid";
    public const string ImportUnknownConverter     = "ErrorImportUnknownConverter";
    public const string ImportFileInvalid          = "ErrorImportFileInvalid";
    public const string ImportEnrichNotImplemented = "ErrorImportEnrichNotImplemented";
    public const string ConflictNotFound             = "ErrorConflictNotFound";
    public const string ConflictAlreadyResolved      = "ErrorConflictAlreadyResolved";
    public const string ConflictNotDecided           = "ErrorConflictNotDecided";
    public const string ConflictAmbiguousFieldsUnresolved = "ErrorConflictAmbiguousFieldsUnresolved";
    public const string ConflictBatchNotFullyDecided = "ErrorConflictBatchNotFullyDecided";
}
