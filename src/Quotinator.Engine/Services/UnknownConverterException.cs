namespace Quotinator.Engine.Services;

/// <summary>
/// Thrown by <see cref="IQuoteImportService"/> when a request names a <see cref="ConverterName"/>
/// that is not registered in this build. A distinct subtype (rather than a generic
/// <see cref="QuoteImportValidationException"/>) so the endpoint handler can build a precise error
/// message without inspecting exception text.
/// </summary>
public sealed class UnknownConverterException : QuoteImportValidationException
{
    /// <summary>Initialises the exception with the unrecognised converter name.</summary>
    public UnknownConverterException(string converterName)
        : base($"'{converterName}' is not a recognised converter.")
    {
        ConverterName = converterName;
    }

    /// <summary>The converter name that was requested but not found in the registered converter set.</summary>
    public string ConverterName { get; }
}
