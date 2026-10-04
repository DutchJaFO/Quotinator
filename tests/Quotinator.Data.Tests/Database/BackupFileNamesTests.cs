using Quotinator.Data.Database;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// The one guard every <c>{name}</c> backup route runs (#349).
/// <para>
/// Tested here as well as through the endpoints because this is the piece that decides whether a
/// caller-supplied string can reach the filesystem at all, and the endpoint tests can only exercise
/// the shapes an HTTP client can express — a route parameter cannot carry every string this function
/// must refuse.
/// </para>
/// </summary>
[TestClass]
public class BackupFileNamesTests
{
    private TempDirectory _backups = null!;

    [TestInitialize]
    public void TestInitialize() => _backups = new TempDirectory("quotinator_349_names_");

    [TestCleanup]
    public void TestCleanup() => _backups.Dispose();

    /// <summary>An ordinary backup file name resolves inside the folder — the positive control.</summary>
    [TestMethod]
    public void PlainFileName_Resolves_InsideTheBackupsFolder()
    {
        Assert.IsTrue(BackupFileNames.TryResolve(_backups.Path, "quotinatordata_v5_20260101T101010101Z.db", out string resolved));
        Assert.AreEqual(Path.Combine(_backups.Path, "quotinatordata_v5_20260101T101010101Z.db"), resolved);
    }

    /// <summary>Every shape that is a path rather than a name is refused.</summary>
    [TestMethod]
    [DataRow("../escape.db",           DisplayName = "parent segment")]
    [DataRow("..\\escape.db",          DisplayName = "parent segment, Windows separator")]
    [DataRow("sub/child.db",           DisplayName = "forward-slash separator")]
    [DataRow("sub\\child.db",          DisplayName = "backslash separator")]
    [DataRow("/etc/passwd",            DisplayName = "absolute POSIX path")]
    [DataRow("C:\\Windows\\win.ini",   DisplayName = "absolute Windows path")]
    [DataRow("..",                     DisplayName = "bare parent")]
    [DataRow(".",                      DisplayName = "bare current")]
    [DataRow("",                       DisplayName = "empty")]
    [DataRow("   ",                    DisplayName = "whitespace")]
    public void PathsAndTraversals_AreRefused(string name)
    {
        Assert.IsFalse(BackupFileNames.TryResolve(_backups.Path, name, out string resolved));
        Assert.AreEqual(string.Empty, resolved);
    }

    /// <summary>A null name is refused rather than throwing — this runs on unvalidated request input.</summary>
    [TestMethod]
    public void NullName_IsRefused_WithoutThrowing()
    {
        Assert.IsFalse(BackupFileNames.TryResolve(_backups.Path, null, out string resolved));
        Assert.AreEqual(string.Empty, resolved);
    }

    /// <summary>
    /// A sibling folder whose name merely starts with the backups folder's own is not inside it. The
    /// separator is what makes the containment check mean containment rather than string prefixing.
    /// </summary>
    [TestMethod]
    public void SiblingFolderSharingAPrefix_IsNotInsideTheBackupsFolder()
    {
        string sibling = _backups.Path + "-other";
        Directory.CreateDirectory(sibling);

        try
        {
            Assert.IsFalse(BackupFileNames.TryResolve(_backups.Path, $"..{Path.DirectorySeparatorChar}{Path.GetFileName(sibling)}{Path.DirectorySeparatorChar}x.db", out _));
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }
}
