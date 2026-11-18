using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// #457, decision D: whether a Reset could set the database file aside is one answer, which a Reset and the degraded
/// reason both read, so the reason never offers a Reset that would be refused.
/// </summary>
[TestClass]
public class SetAsideReadinessTests
{
    private TempDirectory _tempDir = null!;

    [TestInitialize]
    public void Setup() => _tempDir = new("set-aside-readiness");

    [TestCleanup]
    public void Cleanup() => _tempDir.Dispose();

    /// <summary>
    /// A file where the reserved folder belongs, so creating it throws, the same technique #482's Reset tests use,
    /// deterministic on Windows and Linux alike.
    /// </summary>
    [TestMethod]
    public void CheckSetAsideReadiness_FolderCannotBeCreated_ReportsTheObstacle()
    {
        string backups = Path.Combine(_tempDir.Path, "backups");
        Directory.CreateDirectory(backups);
        File.WriteAllText(Path.Combine(backups, BackupFileNames.UnreadableFolderName), "not a directory");

        Assert.AreEqual(ResetObstacle.SetAsideFolderNotWritable, Readiness(backups).CheckSetAsideReadiness());
    }

    /// <summary>Control: a writable data directory with no reserved folder yet has nothing in the way.</summary>
    [TestMethod]
    public void CheckSetAsideReadiness_WritableDirectory_ReportsNone()
    {
        string backups = Path.Combine(_tempDir.Path, "backups");

        Assert.IsNull(Readiness(backups).CheckSetAsideReadiness());
        Assert.IsTrue(Directory.Exists(Path.Combine(backups, BackupFileNames.UnreadableFolderName)), "The reserved folder was not created, as a Reset would.");
    }

    private SetAsideReadiness Readiness(string backups) =>
        new(new DatabaseOptions { DbPath = Path.Combine(_tempDir.Path, "quotinatordata.db"), BackupsPath = backups },
            NullLogger<SetAsideReadiness>.Instance);
}
