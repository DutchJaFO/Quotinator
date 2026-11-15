using Quotinator.Data.Database;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// #490: the free space on the file system that holds a path. That a separate mount is measured as itself, and
/// not as the root file system, needs a Linux mount, which only a container provides; it is proven live by
/// <c>backup/03</c>, red on the build before this fix. These are its controls.
/// </summary>
[TestClass]
public class DiskSpaceProviderTests
{
    /// <summary>Free space changes while a test runs, so two readings are compared within this much.</summary>
    private const long Tolerance = 64L * 1024 * 1024;

    [TestMethod]
    public void GetAvailableFreeSpaceBytes_ExistingDirectory_IsPositive()
    {
        using TempDirectory directory = new("quotinator_space_");

        long free = new DiskSpaceProvider().GetAvailableFreeSpaceBytes(directory.Path);

        Assert.IsGreaterThan(0L, free);
    }

    /// <summary>
    /// A backups folder or the check's work folder may not exist yet; the room it will have is the room of the
    /// file system holding its nearest existing parent, not a failure.
    /// </summary>
    [TestMethod]
    public void GetAvailableFreeSpaceBytes_FolderNotCreatedYet_MeasuresItsNearestExistingParent()
    {
        using TempDirectory directory = new("quotinator_space_");
        DiskSpaceProvider provider = new();

        long parent = provider.GetAvailableFreeSpaceBytes(directory.Path);
        long notYetCreated = provider.GetAvailableFreeSpaceBytes(Path.Combine(directory.Path, "backups", "unreadable"));

        Assert.IsLessThanOrEqualTo(Tolerance, Math.Abs(parent - notYetCreated));
    }
}
