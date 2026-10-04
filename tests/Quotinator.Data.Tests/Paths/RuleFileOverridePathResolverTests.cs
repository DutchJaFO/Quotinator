using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Paths;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Paths;

[TestClass]
public class RuleFileOverridePathResolverTests
{
    private TempDirectory _root = null!;
    private string _internalDir = null!;
    private string _externalDir = null!;
    private RuleFileOverridePathResolver _resolver = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _root = new TempDirectory("quotinator_ruleoverride_test_");
        _internalDir = Path.Combine(_root.Path, "sources", "download");
        _externalDir = Path.Combine(_root.Path, "imports", "download");
        _resolver = new RuleFileOverridePathResolver(_internalDir, _externalDir);
    }

    [TestCleanup]
    public void TestCleanup() => _root.Dispose();

    [TestMethod]
    public void Resolve_Bundled_ResolvesUnderInternalDownloadDir()
    {
        var path = _resolver.Resolve("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled);

        Assert.AreEqual(Path.GetFullPath(Path.Combine(_internalDir, "vilaboim-conflict-rules.json")), path);
    }

    [TestMethod]
    public void Resolve_UserImports_ResolvesUnderExternalDownloadDir()
    {
        var path = _resolver.Resolve("my-source-conflict-rules.json", SeedBatchOrigin.UserImports);

        Assert.AreEqual(Path.GetFullPath(Path.Combine(_externalDir, "my-source-conflict-rules.json")), path);
    }

    [TestMethod]
    public void Resolve_EmptyFileName_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => _resolver.Resolve("", SeedBatchOrigin.Bundled));

    [TestMethod]
    public void Resolve_FileNameContainsForwardSlash_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => _resolver.Resolve("../secrets.json", SeedBatchOrigin.Bundled));

    [TestMethod]
    public void Resolve_FileNameContainsBackslash_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => _resolver.Resolve(@"..\secrets.json", SeedBatchOrigin.Bundled));

    [TestMethod]
    public void Resolve_FileNameIsBareDotDot_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => _resolver.Resolve("..", SeedBatchOrigin.Bundled));

    [TestMethod]
    public void Resolve_FileNameIsAbsolutePath_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => _resolver.Resolve(Path.Combine(Path.GetTempPath(), "evil.json"), SeedBatchOrigin.Bundled));

    [TestMethod]
    public void Resolve_PlainFileNameWithDots_Succeeds()
    {
        // A real filename with dots (e.g. "my.source.rules.json") must not be confused with a
        // directory-traversal attempt — only an exact ".." component is ever rejected.
        var path = _resolver.Resolve("my.source.rules.json", SeedBatchOrigin.Bundled);

        Assert.AreEqual(Path.GetFullPath(Path.Combine(_internalDir, "my.source.rules.json")), path);
    }
}
