using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

/// <summary>
/// Tests for <see cref="ManifestPolicy.Resolve(ManifestPolicy?, ManifestPolicy)"/>'s cascade —
/// called twice to resolve all three tiers (file entry, manifest, application configuration), each
/// tier winning wholesale over the next when present.
/// </summary>
[TestClass]
public class ManifestPolicyTests
{
    [TestMethod]
    public void Resolve_HigherTierPresent_WinsWholesale()
    {
        var higher = new ManifestPolicy(DuplicateResolutionPolicy.MergeTheirs);
        var lower  = new ManifestPolicy(DuplicateResolutionPolicy.Skip);

        var result = ManifestPolicy.Resolve(higher, lower);

        Assert.AreEqual(higher, result);
    }

    [TestMethod]
    public void Resolve_HigherTierAbsent_FallsBackToLowerTier()
    {
        var lower = new ManifestPolicy(DuplicateResolutionPolicy.Skip);

        var result = ManifestPolicy.Resolve(null, lower);

        Assert.AreEqual(lower, result);
    }

    [TestMethod]
    public void Resolve_ThreeTierCascade_FileWinsOverManifestWinsOverConfig()
    {
        var fromFile     = new ManifestPolicy(DuplicateResolutionPolicy.MergeTheirs);
        var fromManifest = new ManifestPolicy(DuplicateResolutionPolicy.NewestWins);
        var fromConfig   = new ManifestPolicy(DuplicateResolutionPolicy.Skip);

        var manifestOrConfig = ManifestPolicy.Resolve(fromManifest, fromConfig);
        var effective        = ManifestPolicy.Resolve(fromFile, manifestOrConfig);

        Assert.AreEqual(fromFile, effective, "File entry's own policy wins over both lower tiers");
    }

    [TestMethod]
    public void Resolve_ThreeTierCascade_ManifestWinsWhenFileAbsent()
    {
        var fromManifest = new ManifestPolicy(DuplicateResolutionPolicy.NewestWins);
        var fromConfig   = new ManifestPolicy(DuplicateResolutionPolicy.Skip);

        var manifestOrConfig = ManifestPolicy.Resolve(fromManifest, fromConfig);
        var effective        = ManifestPolicy.Resolve(null, manifestOrConfig);

        Assert.AreEqual(fromManifest, effective, "No file-level override — manifest wins over config");
    }

    [TestMethod]
    public void Resolve_ThreeTierCascade_ConfigWinsWhenBothFileAndManifestAbsent()
    {
        var fromConfig = new ManifestPolicy(DuplicateResolutionPolicy.Skip);

        var manifestOrConfig = ManifestPolicy.Resolve(null, fromConfig);
        var effective        = ManifestPolicy.Resolve(null, manifestOrConfig);

        Assert.AreEqual(fromConfig, effective, "Neither file nor manifest override present — configuration applies");
    }
}
