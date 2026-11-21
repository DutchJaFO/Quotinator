namespace Quotinator.Api.Tests.Solution;

/// <summary>
/// #503: a T2 result is reported as it read (ADR 029). No document excuses a red step, neither through a
/// header naming another issue nor through an expectation or failure note that tells the reader to carry on.
/// The rules themselves are established against supplied documents in <see cref="AutomatedTestingSuiteTests"/>;
/// these are their live half.
/// </summary>
public partial class RepositoryStructureTests
{
    /// <summary>
    /// No document under <c>docs/automated-testing/</c>, the index included, names the
    /// <c>Fully green after</c> header.
    /// </summary>
    /// <remarks>
    /// The header let #490 record a T2 pass with two failing steps in its scope. The index is read too,
    /// because its template carried the field as a sample line every new document copied.
    /// </remarks>
    [TestMethod]
    public void AutomatedTestDocuments_CarryNoFullyGreenAfterHeader()
    {
        Assert.IsTrue(Directory.Exists(AutomatedTestingDir),
            $"{AutomatedTestingRelativePath} does not exist.");

        List<string> failures = AutomatedTestingSuite.MentionsOfFullyGreenAfter(
            [.. LoadSuiteDocuments(), LoadSuiteIndex()]);

        Assert.IsEmpty(failures,
            "ADR 029: a step that cannot be green until another issue lands stays red, and its owner "
            + "becomes a dependency of every issue it blocks. No header excuses it:\n"
            + string.Join("\n", failures));
    }

    /// <summary>
    /// No document's <c>Expected</c> or <c>On failure</c> text excuses a failing step.
    /// </summary>
    /// <remarks>
    /// Two documents told the reader to record a known failure and carry on, and one printed a status it
    /// said it did not assert, and each was counted as a pass. See #503.
    /// </remarks>
    [TestMethod]
    public void AutomatedTestDocuments_NeverExcuseAFailingStep()
    {
        Assert.IsTrue(Directory.Exists(AutomatedTestingDir),
            $"{AutomatedTestingRelativePath} does not exist.");

        List<string> failures = AutomatedTestingSuite.StepsThatExcuseAFailure(LoadSuiteDocuments());

        Assert.IsEmpty(failures,
            "ADR 029: a T2 result is reported as it read. A failing step states what the failure means "
            + "and stops; it is never recorded as known and passed over:\n"
            + string.Join("\n", failures));
    }
}
