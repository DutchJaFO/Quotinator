using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Testing.Tests.Database;

/// <summary>
/// <see cref="TempDirectory"/>: the one implementation of "clear the SQLite pools, then delete the
/// directory" the test projects share (#419).
/// <para>
/// It exists because that body was written out by hand in nineteen places, and the copy that ran from a
/// host's <c>ApplicationStopped</c> event deleted while handles were still closing, threw, and had its
/// throw swallowed where nobody could see it. A data directory then outlived its host, and the only
/// symptom was an unrelated assertion failing in a full-solution run.
/// </para>
/// </summary>
[TestClass]
public class TempDirectoryTests
{
    /// <summary>The directory exists from construction, so a test can put its fixture in it.</summary>
    [TestMethod]
    public void Construction_CreatesTheDirectory()
    {
        using TempDirectory directory = new("quotinator-tempdir-test-");

        Assert.IsTrue(Directory.Exists(directory.Path));
    }

    /// <summary>And disposing removes it, which is the whole point of owning one.</summary>
    [TestMethod]
    public void Dispose_RemovesTheDirectory()
    {
        TempDirectory directory = new("quotinator-tempdir-test-");
        string path = directory.Path;

        directory.Dispose();

        Assert.IsFalse(Directory.Exists(path));
    }

    /// <summary>
    /// A deletion that cannot happen is reported rather than swallowed. Deleting is best effort, because
    /// a test failing in teardown is worse than a leaked folder, but best effort must not mean invisible:
    /// the silent version of this cost a bisecting session to find.
    /// </summary>
    [TestMethod]
    public void Dispose_WhenTheDirectoryIsHeldOpen_ReportsTheFailure()
    {
        TempDirectory directory = new("quotinator-tempdir-test-");
        string path = directory.Path;
        int before = TempDirectory.CleanupFailures.Count;

        using (File.Create(Path.Combine(path, "held.bin")))
            directory.Dispose();

        Assert.HasCount(before + 1, TempDirectory.CleanupFailures);

        Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// And a deletion that worked reports nothing. Pairs with
    /// <see cref="Dispose_WhenTheDirectoryIsHeldOpen_ReportsTheFailure"/>: without this, that test passes
    /// against an implementation that records a failure every single time it disposes, which would make
    /// the report meaningless exactly when it matters.
    /// </summary>
    [TestMethod]
    public void Dispose_WhenTheDirectoryIsFree_ReportsNothing()
    {
        TempDirectory directory = new("quotinator-tempdir-test-");
        int before = TempDirectory.CleanupFailures.Count;

        directory.Dispose();

        Assert.HasCount(before, TempDirectory.CleanupFailures);
    }

    /// <summary>Disposing twice is harmless: the second call finds nothing and reports nothing.</summary>
    [TestMethod]
    public void Dispose_CalledTwice_ReportsNothingTheSecondTime()
    {
        TempDirectory directory = new("quotinator-tempdir-test-");
        directory.Dispose();
        int afterFirst = TempDirectory.CleanupFailures.Count;

        directory.Dispose();

        Assert.HasCount(afterFirst, TempDirectory.CleanupFailures);
    }
}
