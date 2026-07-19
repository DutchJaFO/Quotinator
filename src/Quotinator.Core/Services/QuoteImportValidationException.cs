namespace Quotinator.Core.Services;

/// <summary>
/// Thrown by <see cref="IQuoteImportService"/> when a request cannot proceed at all — an unrecognised
/// converter name, or file content that could not be parsed/converted into at least one valid quote.
/// The endpoint handler catches this and returns <c>422</c>, never an unhandled <c>500</c>.
/// </summary>
public class QuoteImportValidationException : Exception
{
    /// <summary>Initialises the exception with a message describing why the import request is invalid.</summary>
    public QuoteImportValidationException(string message) : base(message) { }

    /// <summary>Initialises the exception with a message and an inner exception describing why the import request is invalid.</summary>
    public QuoteImportValidationException(string message, Exception innerException) : base(message, innerException) { }
}
