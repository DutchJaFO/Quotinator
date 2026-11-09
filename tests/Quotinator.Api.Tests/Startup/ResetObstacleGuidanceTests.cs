using Quotinator.Api.Startup;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #482: what an operator is told when a Reset of an unreadable database cannot set the file aside. Every
/// obstacle names its own cause and at least one remedy; enumerated from the enum, so an obstacle added
/// later is covered without being listed here.
/// </summary>
[TestClass]
public class ResetObstacleGuidanceTests
{
    /// <summary>Every reset obstacle.</summary>
    public static IEnumerable<object[]> Obstacles =>
        Enum.GetValues<ResetObstacle>().Select(o => new object[] { o });

    [TestMethod]
    [DynamicData(nameof(Obstacles))]
    public void EveryObstacle_HasACauseAndARemedy(ResetObstacle obstacle)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(ResetObstacleGuidance.Cause(obstacle)));
        Assert.IsNotEmpty(ResetObstacleGuidance.Remedies(obstacle));
    }

    /// <summary>A recognised obstacle is described as itself, not as the unrecognised fallback.</summary>
    [TestMethod]
    [DynamicData(nameof(Obstacles))]
    public void EveryObstacle_IsNotDescribedAsTheUnrecognisedFallback(ResetObstacle obstacle)
    {
        Assert.AreNotEqual(ResetObstacleGuidance.Cause((ResetObstacle)(-1)), ResetObstacleGuidance.Cause(obstacle));
    }
}
