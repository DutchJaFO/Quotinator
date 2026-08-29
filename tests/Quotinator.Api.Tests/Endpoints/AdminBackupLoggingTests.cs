using Microsoft.Extensions.Logging;
using Quotinator.Api.Logging;
using Quotinator.Api.Tests.Fakes;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>
/// The backup endpoints' log output (#349).
/// <para>
/// These exist because the first version of these endpoints logged <em>nothing at all</em> — 42
/// endpoint tests, a 34-row verification checklist and two live passes, and not one of them asked
/// whether an operator could see a backup being created or removed. The gap was upstream of the tests:
/// logging was never made a requirement, so nothing was written to catch its absence.
/// </para>
/// <para>
/// Asserted through a real Serilog pipeline via <see cref="CaptureSink"/>, not a MEL test double, per
/// <c>docs/logging.md</c>'s "unit tests must use Serilog's actual rendering" rule — a plain double does
/// not apply Serilog's string quoting, so it cannot prove a <c>{:l}</c> specifier is present.
/// </para>
/// </summary>
[TestClass]
public class AdminBackupLoggingTests
{
    /// <summary>Reads are Debug — invisible at the production default level.</summary>
    [TestMethod]
    public void Reads_AreLoggedAtDebug_AndNotAtInformation()
    {
        (Microsoft.Extensions.Logging.ILogger logger, CaptureSink sink) = Build(LogEventLevel.Debug);
        logger.LogBackupListRead("[Api - GetAllBackups]", "2", "20");
        logger.LogBackupRead("[Api - GetBackupStatus]");
        logger.LogBackupReadByName("[Api - GetBackupContent]", "quotinatordata_v5.db");

        Assert.HasCount(3, sink.Lines);

        (Microsoft.Extensions.Logging.ILogger quiet, CaptureSink quietSink) = Build(LogEventLevel.Information);
        quiet.LogBackupListRead("[Api - GetAllBackups]", "2", "20");
        quiet.LogBackupRead("[Api - GetBackupStatus]");
        quiet.LogBackupReadByName("[Api - GetBackupContent]", "quotinatordata_v5.db");

        Assert.IsEmpty(quietSink.Lines,
            "the status endpoint is called on every render of the degraded UI — at Information it would "
            + "bury the two lines that matter");
    }

    /// <summary>
    /// An action that creates or destroys a restore point is Information — visible at the default
    /// level, because it is what an operator reads when a backup they expected is not there.
    /// </summary>
    [TestMethod]
    public void CreateAndDelete_AreLoggedAtInformation()
    {
        (Microsoft.Extensions.Logging.ILogger logger, CaptureSink sink) = Build(LogEventLevel.Information);

        logger.LogBackupAction("[Api - CreateBackup]", "created", "quotinatordata_v11_20260829T153452565Z.db");
        logger.LogBackupAction("[Api - DeleteBackup]", "removed", "quotinatordata_v11_20260829T153452565Z.db");

        Assert.HasCount(2, sink.Lines);
        Assert.Contains("[Api - CreateBackup] created quotinatordata_v11_20260829T153452565Z.db", sink.Lines[0]);
        Assert.Contains("[Api - DeleteBackup] removed quotinatordata_v11_20260829T153452565Z.db", sink.Lines[1]);
    }

    /// <summary>A refusal is a Warning, and never demoted — per docs/logging.md's demotion rule.</summary>
    [TestMethod]
    public void ARefusal_IsAWarning_NamingTheReason()
    {
        (Microsoft.Extensions.Logging.ILogger logger, CaptureSink sink) = Build(LogEventLevel.Warning);

        logger.LogBackupRefused("[Api - DeleteBackup]", "NotRemovable");

        Assert.HasCount(1, sink.Lines);
        Assert.Contains("NotRemovable", sink.Lines[0]);
    }

    /// <summary>
    /// Every string property carries the <c>{:l}</c> literal specifier, so nothing renders quoted.
    /// <para>
    /// This is the assertion a MEL test double cannot make, and the reason `docs/logging.md` requires a
    /// real Serilog pipeline here: the double's formatter never adds quotes, so a missing specifier
    /// passes the test and still ships quoted output.
    /// </para>
    /// </summary>
    [TestMethod]
    public void NoStringProperty_RendersQuoted()
    {
        (Microsoft.Extensions.Logging.ILogger logger, CaptureSink sink) = Build(LogEventLevel.Debug);

        logger.LogBackupReadByName("[Api - GetBackupContent]", "backup.db");
        logger.LogBackupAction("[Api - CreateBackup]", "created", "backup.db");
        logger.LogBackupRefused("[Api - DeleteBackup]", "NotRemovable");

        foreach (string line in sink.Lines)
            Assert.DoesNotContain("\"", line, $"Serilog quoted a string property: {line}");
    }

    /// <summary>
    /// Every tag used is one `docs/logging.md` declares.
    /// <para>
    /// The document requires a new subsystem to register its prefix "before their log lines land in a
    /// PR" — a rule with nothing enforcing it, which is how these endpoints shipped five unregistered
    /// tags. Read from the document itself rather than from a list here, so a tag added to code and
    /// nowhere else fails on its own.
    /// </para>
    /// </summary>
    [TestMethod]
    public void EveryBackupTag_IsRegisteredInTheLoggingDocument()
    {
        string doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "logging.md"));

        string[] tags =
        [
            "[Api - GetAllBackups]", "[Api - GetBackupStatus]", "[Api - GetBackupContent]",
            "[Api - CreateBackup]", "[Api - DeleteBackup]",
        ];

        List<string> missing = [.. tags.Where(t => !doc.Contains($"`{t}`", StringComparison.Ordinal))];

        Assert.IsEmpty(missing,
            "docs/logging.md's prefix table must declare every tag before it appears in code:\n"
            + string.Join("\n", missing));
    }

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static (Microsoft.Extensions.Logging.ILogger Logger, CaptureSink Sink) Build(LogEventLevel minimumLevel)
    {
        CaptureSink sink = new CaptureSink();
        Serilog.Core.Logger serilog = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new SerilogLoggerFactory(serilog).CreateLogger("BackupEndpoints"), sink);
    }
}
