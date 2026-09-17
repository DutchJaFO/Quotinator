using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Middleware;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Constants.Api;
using Quotinator.Core.Services;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Quotinator.Api.Tests.Middleware;

/// <summary>
/// The one handler that today declares an exception as handled, and therefore has to log it itself
/// (#397). Since .NET 10 the middleware writes nothing for an exception a handler reports as handled,
/// so before this issue a parameter-binding failure turned into a <c>422</c> left no log line anywhere.
/// </summary>
[TestClass]
public class BadRequestExceptionHandlerTests
{
    /// <summary>
    /// The response is unchanged — still a <c>422</c> with the localised message — and the exception is
    /// now logged as handled, carrying the same id its thrown line already has.
    /// </summary>
    [TestMethod]
    public async Task BindingFailure_Returns422AndLogsHandledLineWithTheThrownId()
    {
        (ILogger<BadRequestExceptionHandler> logger, CaptureSink sink) = BuildLogger();
        BadRequestExceptionHandler subject = new(new FakeLocalizer(), logger);
        DefaultHttpContext context = BuildContext();
        BadHttpRequestException exception = new("Failed to read parameter from the request body as JSON.");

        bool handled = await subject.TryHandleAsync(context, exception, TestContext.CancellationToken);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);

        Assert.HasCount(1, sink.Events, "a handled exception is logged exactly once, by whoever handled it");
        Assert.AreEqual(LogEventLevel.Error, sink.Events[0].Level);
        Assert.Contains("[Runtime - Exception]", sink.Lines[0]);
        Assert.IsTrue(
            new Regex(@"\[Runtime - Exception\] [0-9a-f]{8}", RegexOptions.None, TimeSpan.FromSeconds(1)).IsMatch(sink.Lines[0]),
            $"the handled line must carry the exception's id so it pairs with its thrown line: {sink.Lines[0]}");
    }

    /// <summary>An exception this handler does not own is declined and not logged by it.</summary>
    [TestMethod]
    public async Task AnotherException_IsDeclinedAndNotLogged()
    {
        (ILogger<BadRequestExceptionHandler> logger, CaptureSink sink) = BuildLogger();
        BadRequestExceptionHandler subject = new(new FakeLocalizer(), logger);

        bool handled = await subject.TryHandleAsync(
            BuildContext(), new InvalidOperationException("not mine"), TestContext.CancellationToken);

        Assert.IsFalse(handled);
        Assert.IsEmpty(sink.Events, "declining means some other handler, or the middleware, reports it");
    }

    private static DefaultHttpContext BuildContext()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddProblemDetails();

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
    }

    private static (ILogger<BadRequestExceptionHandler> Logger, CaptureSink Sink) BuildLogger()
    {
        CaptureSink sink = new();
        Serilog.Core.Logger serilog = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Error)
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new SerilogLoggerFactory(serilog).CreateLogger<BadRequestExceptionHandler>(), sink);
    }

    /// <summary>MSTest's per-test context, used for the cancellation token the handler signature takes.</summary>
    public TestContext TestContext { get; set; } = default!;

    private sealed class FakeLocalizer : IApiLocalizer
    {
        public string this[string key] => key == ApiMessages.NumericParameterInvalid
            ? "A numeric parameter was not a number."
            : key;

        public string Format(string key, params object[] args) => this[key];

        public IReadOnlyDictionary<string, string> ForEveryLanguage(string key, params object[] args) =>
            new Dictionary<string, string> { ["en-GB"] = this[key] };
    }
}
