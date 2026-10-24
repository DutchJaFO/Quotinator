using Quotinator.Data.Paths;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Paths;

[TestClass]
public class DirectoryWriteProbeTests
{
    [TestMethod]
    public void CanCreateFileIn_AWritableDirectory_IsTrue()
    {
        using TempDirectory directory = new("quotinator_probe_");

        Assert.IsTrue(DirectoryWriteProbe.CanCreateFileIn(directory.Path));
    }

    /// <summary>The probe is removed again, so it never accumulates in a folder something else enumerates.</summary>
    [TestMethod]
    public void CanCreateFileIn_AWritableDirectory_LeavesNothingBehind()
    {
        using TempDirectory directory = new("quotinator_probe_");

        DirectoryWriteProbe.CanCreateFileIn(directory.Path);

        Assert.IsEmpty(Directory.GetFileSystemEntries(directory.Path));
    }

    [TestMethod]
    public void CanCreateFileIn_ADirectoryThatDoesNotExist_IsFalse()
    {
        using TempDirectory directory = new("quotinator_probe_");

        Assert.IsFalse(DirectoryWriteProbe.CanCreateFileIn(Path.Combine(directory.Path, "missing")));
    }

    /// <summary>A path naming a file rather than a directory cannot hold one.</summary>
    [TestMethod]
    public void CanCreateFileIn_APathThatIsAFile_IsFalse()
    {
        using TempDirectory directory = new("quotinator_probe_");
        string file = Path.Combine(directory.Path, "keys");
        File.WriteAllText(file, "not a directory");

        Assert.IsFalse(DirectoryWriteProbe.CanCreateFileIn(file));
    }
}
