using Quotinator.Data.Enums;

namespace Quotinator.Data.Tests.Enums;

/// <summary>Exercises <see cref="ImportActionKindExtensions"/> (#389).</summary>
[TestClass]
public class ImportActionKindExtensionsTests
{
    /// <summary>
    /// The declared classification, or <see langword="null"/> where none is declared — so an unclassified
    /// kind fails an assertion rather than escaping as an exception.
    /// </summary>
    private static bool? Classify(ImportActionKind kind)
    {
        try
        {
            return kind.IsPlanTimeNoOp();
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// #389, the guard the developer asked for: every kind must say which side it is on. A kind added
    /// later fails here until someone decides whether the planner stages it as a no-op — rather than
    /// silently counting as applied work and making discard refuse again.
    /// </summary>
    [TestMethod]
    public void EveryKind_IsClassified()
    {
        List<ImportActionKind> unclassified = [.. Enum.GetValues<ImportActionKind>().Where(kind => Classify(kind) is null)];

        Assert.IsEmpty(unclassified,
            "These import action kinds have no plan-time classification: " + string.Join(", ", unclassified));
    }

    /// <summary>
    /// #389: exactly the kinds the planner stages straight to <c>Applied</c> — <c>Unchanged</c> (#373),
    /// <c>ResolvedToExisting</c> (#377) and <c>AlreadyReported</c> (#376). <c>Add</c> and <c>Modify</c>
    /// are real work, and an applied one must still refuse a discard.
    /// </summary>
    [TestMethod]
    public void IsPlanTimeNoOp_IsExactlyTheKindsStagedAsApplied()
    {
        List<ImportActionKind> noOps = [.. Enum.GetValues<ImportActionKind>().Where(kind => Classify(kind) == true)];

        Assert.AreSequenceEqual(
            [ImportActionKind.Unchanged, ImportActionKind.ResolvedToExisting, ImportActionKind.AlreadyReported], noOps);
        Assert.IsFalse(Classify(ImportActionKind.Add), "An applied Add is real work.");
        Assert.IsFalse(Classify(ImportActionKind.Modify), "An applied Modify is real work.");
    }
}
