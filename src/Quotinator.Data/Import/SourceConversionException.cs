namespace Quotinator.Data.Import;

/// <summary>Thrown by an <see cref="IQuoteSourceConverter"/> when its input cannot be converted at all — e.g. the raw format could not be parsed, or zero entries converted successfully.</summary>
public sealed class SourceConversionException : Exception
{
    /// <summary>Initialises the exception with a message describing why conversion failed.</summary>
    public SourceConversionException(string message) : base(message) { }

    /// <summary>Initialises the exception with a message and an inner exception describing why conversion failed.</summary>
    public SourceConversionException(string message, Exception innerException) : base(message, innerException) { }
}
