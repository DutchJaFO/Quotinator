using Microsoft.Extensions.Logging;
using Quotinator.Api.Tests.Fakes;
using Serilog;
using Serilog.Extensions.Logging;
using ApiLog = Quotinator.Api.Logging.LogMessages;
using DataLog = Quotinator.Data.Logging.LogMessages;

namespace Quotinator.Api.Tests.Logging;

/// <summary>
/// #348: each line naming a backup obstacle renders it unquoted, as the rest of the log does.
/// <para>
/// Rendered through Serilog, per <c>docs/logging.md</c>: Serilog quotes a string property unless its
/// template marks it literal, and a plain test logger does not, so a test through one passes while the
/// real log reads <c>("BudgetExceeded")</c>. Found by <c>backup/06</c>'s first run, whose log search
/// matched nothing for exactly that reason.
/// </para>
/// </summary>
[TestClass]
public class BackupObstacleLogRenderingTests
{
    /// <summary>Every log line that names an obstacle, by the message it writes.</summary>
    public static IEnumerable<object[]> ObstacleLines =>
    [
        [nameof(DataLog.LogSeedRefusedNoBackup)],
        [nameof(DataLog.LogMigrationRefusedNoBackup)],
        [nameof(DataLog.LogResetRefusedNoBackup)],
        [nameof(DataLog.LogResetProceedingWithoutBackup)],
        [nameof(ApiLog.LogReseedProceedingWithoutBackup)],
    ];

    [TestMethod]
    [DynamicData(nameof(ObstacleLines))]
    public void ObstacleLine_RendersTheObstacleUnquoted(string line)
    {
        CaptureSink sink = new();
        using Serilog.Core.Logger serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        Microsoft.Extensions.Logging.ILogger logger = new SerilogLoggerFactory(serilog).CreateLogger("Test");

        Write(logger, line, "BudgetExceeded");

        Assert.Contains("(BudgetExceeded)", sink.Lines.Single(), StringComparison.Ordinal);
    }

    private static void Write(Microsoft.Extensions.Logging.ILogger logger, string line, string obstacle)
    {
        switch (line)
        {
            case nameof(DataLog.LogSeedRefusedNoBackup):           DataLog.LogSeedRefusedNoBackup(logger, obstacle); break;
            case nameof(DataLog.LogMigrationRefusedNoBackup):      DataLog.LogMigrationRefusedNoBackup(logger, obstacle); break;
            case nameof(DataLog.LogResetRefusedNoBackup):          DataLog.LogResetRefusedNoBackup(logger, obstacle); break;
            case nameof(DataLog.LogResetProceedingWithoutBackup):  DataLog.LogResetProceedingWithoutBackup(logger, obstacle); break;
            case nameof(ApiLog.LogReseedProceedingWithoutBackup):  ApiLog.LogReseedProceedingWithoutBackup(logger, obstacle); break;
            default: throw new ArgumentOutOfRangeException(nameof(line), line, "Not an obstacle line this test knows how to write.");
        }
    }
}
