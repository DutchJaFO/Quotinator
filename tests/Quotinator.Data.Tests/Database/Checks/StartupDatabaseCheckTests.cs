using Quotinator.Data.Database;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.Fakes;

namespace Quotinator.Data.Tests.Database.Checks;

/// <summary>
/// #490, found in T1: what startup logs about a check that passed. The log is the subject: it is what an
/// operator without a UI reads to confirm the server is sound, so it must not claim a layer it did not check.
/// </summary>
[TestClass]
public class StartupDatabaseCheckTests
{
    /// <summary>
    /// The second T1 run: the Schema layer was skipped before migrating, and the next line still said every
    /// layer passed. A check that skipped a layer says that the layers that ran passed, and names the skip.
    /// </summary>
    [TestMethod]
    public void Log_PassedWithASkippedLayer_DoesNotClaimEveryLayerAndNamesTheSkip()
    {
        CapturingLogger<StartupDatabaseCheckTests> logger = new();

        StartupDatabaseCheck.Log(logger, Check(DatabaseCheckLayer.Schema));

        Assert.DoesNotContain(m => m.Contains("every layer passed", StringComparison.Ordinal), logger.Messages,
            "A check that skipped a layer was logged as every layer passing.");
        Assert.Contains(m => m.Contains("every layer that ran passed", StringComparison.Ordinal) && m.Contains("skipped: Schema", StringComparison.Ordinal),
            logger.Messages, "The passed line does not name the skipped layer.");
    }

    /// <summary>At <c>Fast</c>, both layers it gives up are named.</summary>
    [TestMethod]
    public void Log_PassedAtFast_NamesBothSkippedLayers()
    {
        CapturingLogger<StartupDatabaseCheckTests> logger = new();

        StartupDatabaseCheck.Log(logger, Check(DatabaseCheckLayer.Indexes, DatabaseCheckLayer.References));

        Assert.Contains(m => m.Contains("skipped: Indexes, References", StringComparison.Ordinal), logger.Messages);
    }

    /// <summary>Control: a check that ran every layer still says so.</summary>
    [TestMethod]
    public void Log_PassedWithNothingSkipped_SaysEveryLayerPassed()
    {
        CapturingLogger<StartupDatabaseCheckTests> logger = new();

        StartupDatabaseCheck.Log(logger, Check());

        Assert.Contains(m => m.Contains("every layer passed", StringComparison.Ordinal), logger.Messages);
    }

    /// <summary>A check whose every layer passed except <paramref name="skipped"/>, which it did not run.</summary>
    private static DatabaseCheckResult Check(params DatabaseCheckLayer[] skipped) => new()
    {
        DatabaseName = DatabaseNames.Main,
        Depth        = DatabaseCheckDepth.Full,
        Layers       =
        [
            .. Enum.GetValues<DatabaseCheckLayer>().Select(layer => new DatabaseCheckLayerResult
            {
                Layer  = layer,
                Status = skipped.Contains(layer) ? DatabaseCheckLayerStatus.Skipped : DatabaseCheckLayerStatus.Passed,
            }),
        ],
    };
}
