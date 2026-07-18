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
    public const string PageBeyondLastPage   = "ErrorPageBeyondLastPage";
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
    public const string ImportActionNotFound             = "ErrorImportActionNotFound";
    public const string ImportActionAlreadyResolved      = "ErrorImportActionAlreadyResolved";
    public const string ImportActionNotDecided           = "ErrorImportActionNotDecided";
    public const string ImportActionNotDecidable         = "ErrorImportActionNotDecidable";
    public const string ImportActionAmbiguousFieldsUnresolved = "ErrorImportActionAmbiguousFieldsUnresolved";
    public const string ImportActionBatchNotFullyDecided = "ErrorImportActionBatchNotFullyDecided";
    public const string ImportActionBatchInvalidState    = "ErrorImportActionBatchInvalidState";
    public const string ImportBatchNotFound              = "ErrorImportBatchNotFound";
    public const string ImportFileOrBatchIdRequired      = "ErrorImportFileOrBatchIdRequired";
    public const string ImportActionBatchNotReversible   = "ErrorImportActionBatchNotReversible";
    public const string ConversationNotFound             = "ErrorConversationNotFound";
    public const string MutuallyExclusiveEntityFilter    = "ErrorMutuallyExclusiveEntityFilter";
    public const string InvalidEntityFilterId            = "ErrorInvalidEntityFilterId";
    public const string EntityFilterNoMatch              = "InfoEntityFilterNoMatch";
    public const string SourceNotFound                   = "ErrorSourceNotFound";
    public const string CharacterNotFound                = "ErrorCharacterNotFound";
}
