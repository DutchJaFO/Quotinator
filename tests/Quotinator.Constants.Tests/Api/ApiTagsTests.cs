using System.Reflection;
using Quotinator.Constants.Api;

namespace Quotinator.Constants.Tests.Api;

[TestClass]
public class ApiTagsTests
{
    /// <summary>
    /// Reflects over every `public const string` field on <see cref="ApiTags"/> rather than listing them
    /// by hand (same technique as <c>SqlQueryGuardTests</c>'s <c>GetFields</c> scan) — a hardcoded list
    /// silently stops covering "all" tags the moment someone adds one and forgets to update it. A newly
    /// added tag is automatically checked with no further maintenance here.
    /// </summary>
    private static List<string> AllTagValues() =>
        typeof(ApiTags)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

    [TestMethod]
    public void ApiTags_ReflectionFindsDeclaredConstants()
        => Assert.IsTrue(AllTagValues().Count > 0, "reflection found zero ApiTags constants — check the BindingFlags");

    [TestMethod]
    public void ApiTags_AllValues_AreDistinct()
        => CollectionAssert.AllItemsAreUnique(AllTagValues());
}
