using Microsoft.Extensions.Logging;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class LegacyConfigWarningsTests
{
    [TestMethod]
    public void WarnIfDataPathStillSet_ValueSet_LogsWarning()
    {
        var logger = new RecordingLogger();

        LegacyConfigWarnings.WarnIfDataPathStillSet("/data/quotes.json", logger);

        Assert.AreEqual(1, logger.Entries.Count);
        Assert.AreEqual(LogLevel.Warning, logger.Entries[0].Level);
        StringAssert.Contains(logger.Entries[0].Message, "Quotinator__DataPath");
        StringAssert.Contains(logger.Entries[0].Message, "Quotinator__DataDir");
    }

    [TestMethod]
    public void WarnIfDataPathStillSet_ValueNull_DoesNotLog()
    {
        var logger = new RecordingLogger();

        LegacyConfigWarnings.WarnIfDataPathStillSet(null, logger);

        Assert.AreEqual(0, logger.Entries.Count);
    }

    [TestMethod]
    public void WarnIfDataPathStillSet_ValueEmptyString_DoesNotLog()
    {
        var logger = new RecordingLogger();

        LegacyConfigWarnings.WarnIfDataPathStillSet(string.Empty, logger);

        Assert.AreEqual(0, logger.Entries.Count);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
