using Microsoft.Extensions.Logging;
using Quotinator.Logging.Tests.Fakes;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Quotinator.Logging.Tests;

[TestClass]
public class LogMessagesTests
{
    // -------------------------------------------------------------------------
    #region Helpers

    /// <summary>
    /// Builds a logger against a real Serilog pipeline via <see cref="CaptureSink"/> — a plain MEL
    /// test double's formatter callback does not apply Serilog's default string-quoting behaviour, so
    /// it cannot prove whether a `{:l}` literal specifier is present or absent (docs/logging.md's
    /// "unit tests must use Serilog's actual rendering" rule).
    /// </summary>
    private static (Microsoft.Extensions.Logging.ILogger Logger, CaptureSink Sink) Build()
    {
        var sink = new CaptureSink();
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .WriteTo.Sink(sink)
            .CreateLogger();
        var logger = new SerilogLoggerFactory(serilog).CreateLogger("Test");
        return (logger, sink);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region LogPageQuery

    /// <summary>
    /// The tag is baked into the message template as a plain <c>{Tag:l}</c> literal-specifier
    /// argument, matching how it appeared as unquoted literal text before this method existed.
    /// <c>page</c>/<c>pageSize</c> deliberately still render quoted — they carried no <c>:l</c> at
    /// any of the 15 call sites this method replaces, and this conversion preserves that rendering
    /// exactly rather than silently changing it.
    /// </summary>
    [TestMethod]
    public void LogPageQuery_RendersTagUnquoted_PageAndPageSizeQuoted()
    {
        var (logger, sink) = Build();
        logger.LogPageQuery("[Api - GetAllSources]", "2", "20");

        Assert.HasCount(1, sink.Lines);
        Assert.AreEqual("[Api - GetAllSources] page=\"2\" pageSize=\"20\"", sink.Lines[0]);
    }

    [TestMethod]
    public void LogPageQuery_NullPageAndPageSize_RendersNull()
    {
        var (logger, sink) = Build();
        logger.LogPageQuery("[Api - GetAllSources]", null, null);

        Assert.AreEqual("[Api - GetAllSources] page=null pageSize=null", sink.Lines[0]);
    }

    [TestMethod]
    public void LogPageQuery_LogsAtInformationLevel()
    {
        var (logger, sink) = Build();
        logger.LogPageQuery("[Api - GetAllSources]", "1", "20");

        Assert.AreEqual(LogEventLevel.Information, sink.Events[0].Level);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region LogIdQuery

    /// <summary>
    /// The tag renders unquoted (<c>:l</c>, matching its prior literal-text form); <c>id</c>
    /// deliberately still renders quoted, matching the un-specified <c>{Id}</c> at every one of the
    /// call sites this method replaces.
    /// </summary>
    [TestMethod]
    public void LogIdQuery_RendersTagUnquoted_IdQuoted()
    {
        var (logger, sink) = Build();
        logger.LogIdQuery("[Api - GetSourceById]", "aabbccdd-1234-4abc-8def-1234567890ab");

        Assert.HasCount(1, sink.Lines);
        Assert.AreEqual("[Api - GetSourceById] id=\"aabbccdd-1234-4abc-8def-1234567890ab\"", sink.Lines[0]);
    }

    [TestMethod]
    public void LogIdQuery_LogsAtInformationLevel()
    {
        var (logger, sink) = Build();
        logger.LogIdQuery("[Api - GetSourceById]", "aabbccdd-1234-4abc-8def-1234567890ab");

        Assert.AreEqual(LogEventLevel.Information, sink.Events[0].Level);
    }

    #endregion
}
