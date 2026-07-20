using Quotinator.Data.Helpers;

namespace Quotinator.Data.Tests.Helpers;

/// <summary>
/// Verifies <see cref="EntityIdCanonicalizer"/> canonicalizes a raw externally-supplied id string to
/// this project's canonical form in both directions, is idempotent, and rejects malformed input via
/// both a throwing and a non-throwing form. See #209/#210.
/// </summary>
[TestClass]
public class EntityIdCanonicalizerTests
{
    [TestMethod]
    public void CanonicalizeUppercase_LowercaseGuid_ReturnsUppercaseD()
    {
        var result = EntityIdCanonicalizer.CanonicalizeUppercase("f0000190-0000-4000-8000-000000000001");

        Assert.AreEqual("F0000190-0000-4000-8000-000000000001", result);
    }

    [TestMethod]
    public void CanonicalizeUppercase_AlreadyCanonical_IsIdempotent()
    {
        var result = EntityIdCanonicalizer.CanonicalizeUppercase("F0000190-0000-4000-8000-000000000001");

        Assert.AreEqual("F0000190-0000-4000-8000-000000000001", result);
    }

    [TestMethod]
    public void CanonicalizeUppercase_Malformed_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => EntityIdCanonicalizer.CanonicalizeUppercase("not-a-guid"));
    }

    [TestMethod]
    public void TryCanonicalizeUppercase_ValidGuid_ReturnsTrueWithCanonicalForm()
    {
        var succeeded = EntityIdCanonicalizer.TryCanonicalizeUppercase("f0000190-0000-4000-8000-000000000001", out var canonical);

        Assert.IsTrue(succeeded);
        Assert.AreEqual("F0000190-0000-4000-8000-000000000001", canonical);
    }

    [TestMethod]
    public void TryCanonicalizeUppercase_Malformed_ReturnsFalse()
    {
        var succeeded = EntityIdCanonicalizer.TryCanonicalizeUppercase("not-a-guid", out var canonical);

        Assert.IsFalse(succeeded);
        Assert.IsNull(canonical);
    }

    [TestMethod]
    public void CanonicalizeLowercase_UppercaseGuid_ReturnsLowercaseD()
    {
        var result = EntityIdCanonicalizer.CanonicalizeLowercase("F0000210-0000-4000-8000-000000000001");

        Assert.AreEqual("f0000210-0000-4000-8000-000000000001", result);
    }

    [TestMethod]
    public void CanonicalizeLowercase_AlreadyCanonical_IsIdempotent()
    {
        var result = EntityIdCanonicalizer.CanonicalizeLowercase("f0000210-0000-4000-8000-000000000001");

        Assert.AreEqual("f0000210-0000-4000-8000-000000000001", result);
    }

    [TestMethod]
    public void CanonicalizeLowercase_Malformed_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => EntityIdCanonicalizer.CanonicalizeLowercase("not-a-guid"));
    }

    [TestMethod]
    public void TryCanonicalizeLowercase_ValidGuid_ReturnsTrueWithCanonicalForm()
    {
        var succeeded = EntityIdCanonicalizer.TryCanonicalizeLowercase("F0000210-0000-4000-8000-000000000001", out var canonical);

        Assert.IsTrue(succeeded);
        Assert.AreEqual("f0000210-0000-4000-8000-000000000001", canonical);
    }

    [TestMethod]
    public void TryCanonicalizeLowercase_Malformed_ReturnsFalse()
    {
        var succeeded = EntityIdCanonicalizer.TryCanonicalizeLowercase("not-a-guid", out var canonical);

        Assert.IsFalse(succeeded);
        Assert.IsNull(canonical);
    }
}
