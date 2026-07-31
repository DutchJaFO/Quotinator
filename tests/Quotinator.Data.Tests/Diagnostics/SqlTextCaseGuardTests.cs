using Quotinator.Data.Diagnostics;

namespace Quotinator.Data.Tests.Diagnostics;

/// <summary>
/// Verifies <see cref="SqlTextCaseGuard"/> flags a case-sensitive comparison between a known non-id
/// text column and a caller-or-file-supplied parameter, does not false-positive on already-protected
/// comparisons or columns outside the supplied known-column set, and that
/// <see cref="SqlTextCaseGuard.DiscoverTextColumnNames"/> correctly identifies plain string
/// properties while excluding enum-backed and id-suffixed ones. See #211.
/// </summary>
[TestClass]
public class SqlTextCaseGuardTests
{
    private static readonly HashSet<string> KnownColumns = new(StringComparer.OrdinalIgnoreCase) { "Name", "Title" };

    // ── FindViolations: patterns that must be flagged ───────────────────────────

    [TestMethod]
    public void FindViolations_BareUnwrappedEquality_ReturnsMatch()
        => Assert.HasCount(1, SqlTextCaseGuard.FindViolations(
            "SELECT * FROM Series WHERE Name = @name;", KnownColumns));

    [TestMethod]
    public void FindViolations_AliasedUnwrappedEquality_ReturnsMatch()
        => Assert.HasCount(1, SqlTextCaseGuard.FindViolations(
            "SELECT s.Id FROM Sources s WHERE s.Title = @title;", KnownColumns));

    [TestMethod]
    public void FindViolations_HalfProtected_ColumnOnlyWrapped_ReturnsMatch()
        => Assert.HasCount(1, SqlTextCaseGuard.FindViolations(
            "SELECT * FROM Series WHERE LOWER(Name) = @name;", KnownColumns));

    [TestMethod]
    public void FindViolations_HalfProtected_ParamOnlyWrapped_ReturnsMatch()
        => Assert.HasCount(1, SqlTextCaseGuard.FindViolations(
            "SELECT * FROM Series WHERE Name = LOWER(@name);", KnownColumns));

    [TestMethod]
    public void FindViolations_MultipleUnprotectedComparisons_ReturnsBothMatches()
        => Assert.HasCount(2, SqlTextCaseGuard.FindViolations(
            "SELECT * FROM Sources WHERE Title = @title AND Name = @name;", KnownColumns));

    /// <summary>
    /// <c>Status</c> (SystemImportActions) needs guard coverage despite being enum-backed on its
    /// entity — its query parameter is raw external text with no enum round-trip of its own. See
    /// <see cref="SqlTextCaseGuard.AdditionalColumnNames"/>'s own remarks.
    /// </summary>
    [TestMethod]
    public void FindViolations_AdditionalColumnName_Status_ReturnsMatchEvenWithoutBeingInKnownSet()
        => Assert.HasCount(1, SqlTextCaseGuard.FindViolations(
            "SELECT * FROM System_ImportActions WHERE Status = @status;", []));

    // ── FindViolations: patterns that must NOT be flagged ───────────────────────

    [TestMethod]
    public void FindViolations_FullyProtectedEquality_ReturnsEmpty()
        => Assert.IsEmpty(SqlTextCaseGuard.FindViolations(
            "SELECT * FROM Series WHERE LOWER(Name) = LOWER(@name);", KnownColumns));

    [TestMethod]
    public void FindViolations_FullyProtectedAliasedEquality_ReturnsEmpty()
        => Assert.IsEmpty(SqlTextCaseGuard.FindViolations(
            "SELECT s.Id FROM Sources s WHERE LOWER(s.Title) = LOWER(@title);", KnownColumns));

    [TestMethod]
    public void FindViolations_ColumnNotInKnownSet_ReturnsEmpty()
        => Assert.IsEmpty(SqlTextCaseGuard.FindViolations(
            "SELECT * FROM Widgets WHERE Label = @label;", KnownColumns));

    [TestMethod]
    public void FindViolations_NoComparisonAtAll_ReturnsEmpty()
        => Assert.IsEmpty(SqlTextCaseGuard.FindViolations(
            "SELECT COUNT(*) FROM Sources WHERE IsDeleted = 0;", KnownColumns));

    [TestMethod]
    public void FindViolations_EmptyKnownColumnSet_NoAdditionalMatch_ReturnsEmpty()
        => Assert.IsEmpty(SqlTextCaseGuard.FindViolations(
            "SELECT * FROM Series WHERE Name = @name;", []));

    /// <summary>
    /// An UPDATE ... SET assignment is a write, not a comparison — must never be flagged. Found live
    /// while verifying this guard: every real <c>UpdateFieldsById</c>-style query in the codebase
    /// false-positived on its own SET clause until this was added, mirroring
    /// <see cref="SqlIdCaseGuard"/>'s own <c>StripUpdateSetClause</c> handling exactly.
    /// </summary>
    [TestMethod]
    public void FindViolations_UpdateSetClauseAssignment_ReturnsEmpty()
        => Assert.IsEmpty(SqlTextCaseGuard.FindViolations(
            "UPDATE Series SET Name = @name, DateModified = @dateModified WHERE LOWER(Id) = LOWER(@id);", KnownColumns));

    [TestMethod]
    public void FindViolations_UpdateSetClauseAssignment_WhereClauseStillScanned_ReturnsMatch()
        => Assert.HasCount(1, SqlTextCaseGuard.FindViolations(
            "UPDATE Sources SET Type = @type WHERE Title = @title;", KnownColumns));

    // ── DiscoverTextColumnNames ──────────────────────────────────────────────────

    private sealed class FakeEntity
    {
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public Guid RecordId { get; init; }
        public string SomeId { get; init; } = string.Empty;
        public FakeEnum? Status { get; init; }
    }

    private enum FakeEnum { A, B }

    [TestMethod]
    public void DiscoverTextColumnNames_PlainStringProperties_AreIncluded()
    {
        var names = SqlTextCaseGuard.DiscoverTextColumnNames(typeof(FakeEntity));

        Assert.Contains("Name", names.ToList());
        Assert.Contains("Description", names.ToList());
    }

    [TestMethod]
    public void DiscoverTextColumnNames_IdSuffixedProperty_IsExcluded()
        => Assert.DoesNotContain(
"SomeId", SqlTextCaseGuard.DiscoverTextColumnNames(typeof(FakeEntity)).ToList());

    [TestMethod]
    public void DiscoverTextColumnNames_NonStringProperties_AreExcluded()
    {
        var names = SqlTextCaseGuard.DiscoverTextColumnNames(typeof(FakeEntity)).ToList();

        Assert.DoesNotContain("RecordId", names);
        Assert.DoesNotContain("Status", names, "An enum-typed property (even nullable) is not a string property and must be skipped automatically.");
    }
}
