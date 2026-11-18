using Quotinator.Api.Startup;
using Quotinator.Constants.Api;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #457: the reason for a database file the check finds is not a sound copy, or cannot open at all, says what is wrong
/// with the file and names remedies that work for it, instead of diagnosing a schema-version mismatch.
/// </summary>
[TestClass]
public class DatabaseUnsoundGuidanceTests
{
    private static readonly DatabaseCheckLayer[] FileLayers =
        [DatabaseCheckLayer.Format, DatabaseCheckLayer.Journal, DatabaseCheckLayer.Pages, DatabaseCheckLayer.Indexes];

    /// <summary>The file layers and a file that could not be opened are this reason's; nothing else is.</summary>
    [TestMethod]
    public void Covers_FileLayersAndAFileThatCannotBeOpened_AndNothingElse()
    {
        foreach (DatabaseCheckLayer layer in FileLayers)
            Assert.IsTrue(DatabaseUnsoundGuidance.Covers(Failed(layer)), $"{layer} is not covered.");
        Assert.IsTrue(DatabaseUnsoundGuidance.Covers(CouldNotBeOpened()), "A file that could not be opened is not covered.");

        Assert.IsFalse(DatabaseUnsoundGuidance.Covers(Failed(DatabaseCheckLayer.Storage, DatabaseStorageFault.DirectoryNotWritable)), "An unwritable directory is #326's reason.");
        Assert.IsFalse(DatabaseUnsoundGuidance.Covers(Failed(DatabaseCheckLayer.References)), "A dangling reference does not degrade (#490, decision E).");
        Assert.IsFalse(DatabaseUnsoundGuidance.Covers(Failed(DatabaseCheckLayer.Schema)), "A schema difference is #305's reason.");
    }

    /// <summary>Decision B: each file layer, and decision E's file that could not be opened, has a cause of its own.</summary>
    [TestMethod]
    public void Reason_EachLayer_NamesItsOwnCause()
    {
        DatabaseCheckLayerResult[] failures = [.. FileLayers.Select(l => Failed(l)), CouldNotBeOpened()];
        string[] causes = [.. failures.Select(DatabaseUnsoundGuidance.Cause)];

        Assert.HasCount(failures.Length, causes.Distinct(), "Two failures share a cause.");
        foreach (DatabaseCheckLayerResult failure in failures)
        {
            string cause = DatabaseUnsoundGuidance.Cause(failure);
            Assert.IsFalse(string.IsNullOrWhiteSpace(cause), $"{failure.Layer} has no cause.");
            Assert.StartsWith(cause, DatabaseUnsoundGuidance.Reason(failure, null), $"{failure.Layer}'s reason does not lead with its cause.");
        }
    }

    /// <summary>The issue's subject: no reason this class gives claims the schema version is the problem.</summary>
    [TestMethod]
    public void Reason_NeverClaimsASchemaVersionMismatch()
    {
        foreach (DatabaseCheckLayerResult failure in AllFailures())
            Assert.DoesNotContain("schema version", DatabaseUnsoundGuidance.Reason(failure, null), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Decision 2: for a damaged file, the Reset, then a backup, then deleting the file.</summary>
    [TestMethod]
    public void Reason_NamesTheResetThenABackupThenDeletingTheFile()
    {
        foreach (DatabaseCheckLayer layer in FileLayers)
        {
            string reason = DatabaseUnsoundGuidance.Reason(Failed(layer), null);

            int reset = reason.IndexOf(BackupObstacleGuidance.ResetSetsTheFileAside, StringComparison.Ordinal);
            int backup = reason.IndexOf(BackupObstacleGuidance.RestoreAnOlderBackup, StringComparison.Ordinal);
            int delete = reason.IndexOf("deleting the database file", StringComparison.Ordinal);

            Assert.IsTrue(reset >= 0 && backup > reset && delete > backup, $"{layer}: the remedies are missing or out of order: {reason}");
        }
    }

    /// <summary>
    /// Decision E: a file that could not be opened may be intact, so access comes first, then a backup, and the Reset
    /// last, saying the file it sets aside may be intact.
    /// </summary>
    [TestMethod]
    public void Reason_FileCouldNotBeOpened_RestoresAccessFirstAndResetsLast()
    {
        string reason = DatabaseUnsoundGuidance.Reason(CouldNotBeOpened(), null);

        int access = reason.IndexOf("Restore read access", StringComparison.Ordinal);
        int backup = reason.IndexOf(BackupObstacleGuidance.RestoreAnOlderBackup, StringComparison.Ordinal);
        int reset = reason.IndexOf(BackupObstacleGuidance.ResetSetsTheFileAside, StringComparison.Ordinal);

        Assert.IsTrue(access >= 0 && backup > access && reset > backup, $"The remedies are missing or out of order: {reason}");
        Assert.Contains("may be intact", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("deleting the database file", reason, StringComparison.Ordinal);
    }

    /// <summary>Decision 5: every reason ends with the Knowledgebase entry.</summary>
    [TestMethod]
    public void Reason_EndsWithTheKnowledgebaseLink()
    {
        foreach (DatabaseCheckLayerResult failure in AllFailures())
            Assert.EndsWith(KnowledgebaseLinks.TheDatabaseFileCannotBeRead, DatabaseUnsoundGuidance.Reason(failure, null));
    }

    /// <summary>
    /// Decision B: a file whose index disagrees with its table can be read, so its reason never says otherwise, not
    /// even through a sentence it shares with another reason.
    /// </summary>
    [TestMethod]
    public void Reason_Indexes_DoesNotSayTheFileCannotBeRead()
    {
        foreach (ResetObstacle? obstacle in new ResetObstacle?[] { null, ResetObstacle.SetAsideFolderNotWritable })
        {
            string reason = DatabaseUnsoundGuidance.Reason(Failed(DatabaseCheckLayer.Indexes), obstacle);

            Assert.DoesNotContain("cannot be read", reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unreadable file", reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Decision 3: when the reserved folder cannot be used, the reason does not offer the Reset, which would be
    /// refused, and names what stops it instead.
    /// </summary>
    [TestMethod]
    public void Reason_WithAResetObstacle_LeavesTheResetOut()
    {
        foreach (DatabaseCheckLayerResult failure in AllFailures())
        {
            string reason = DatabaseUnsoundGuidance.Reason(failure, ResetObstacle.SetAsideFolderNotWritable);

            Assert.DoesNotContain(BackupObstacleGuidance.ResetSetsTheFileAside, reason, StringComparison.Ordinal);
            Assert.Contains(ResetObstacleGuidance.FreeTheSetAsideFolder, reason, StringComparison.Ordinal);
            Assert.Contains(BackupObstacleGuidance.RestoreAnOlderBackup, reason, StringComparison.Ordinal);
        }
    }

    /// <summary>The reason speaks in layers, never the engine's name (#490, decision A).</summary>
    [TestMethod]
    public void Reason_NeverNamesTheEngine()
    {
        foreach (DatabaseCheckLayerResult failure in AllFailures())
            Assert.DoesNotContain("SQLite", DatabaseUnsoundGuidance.Reason(failure, null), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<DatabaseCheckLayerResult> AllFailures() => [.. FileLayers.Select(l => Failed(l)), CouldNotBeOpened()];

    private static DatabaseCheckLayerResult CouldNotBeOpened() => Failed(DatabaseCheckLayer.Storage, DatabaseStorageFault.FileUnreadable);

    private static DatabaseCheckLayerResult Failed(DatabaseCheckLayer layer, DatabaseStorageFault? fault = null) =>
        new() { Layer = layer, Status = DatabaseCheckLayerStatus.Failed, StorageFault = fault };
}
