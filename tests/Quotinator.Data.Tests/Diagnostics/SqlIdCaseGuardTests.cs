using Quotinator.Data.Diagnostics;

namespace Quotinator.Data.Tests.Diagnostics;

/// <summary>
/// Verifies <see cref="SqlIdCaseGuard"/> flags a case-sensitive comparison between an id-named
/// column and a caller-or-file-supplied parameter, or between two id-named columns (a JOIN/
/// correlated-subquery condition), and does not false-positive on already-protected comparisons,
/// write-side assignments, or non-id columns. See ADR 012 and #210.
/// </summary>
[TestClass]
public class SqlIdCaseGuardTests
{
    // ── Patterns that must be flagged ─────────────────────────────────────────

    [TestMethod]
    public void IsCaseSensitiveIdComparison_BareIdEquality_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM Quotes WHERE Id = @id;"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_PrefixedIdColumnEquality_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "DELETE FROM QuoteGenres WHERE QuoteId = @id;"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_AliasedColumnEquality_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT s.Title FROM Sources s WHERE s.Id = @sourceId AND s.IsDeleted = 0;"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_BracketQuotedColumnEquality_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM [Widgets] WHERE [Id] = @id AND [IsDeleted] = 0"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_InClause_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM [Widgets] WHERE [Id] IN @ids AND [IsDeleted] = 0"));

    /// <summary>
    /// Found live during #210's IdClauses refactor: the operator alternation originally covered only
    /// "=" and "IN", so "q.Id NOT IN @excludedIds" (SqliteQuoteService.GetRandom's dedup exclusion)
    /// silently evaded detection — "IN" alone doesn't match starting mid-way through "NOT IN".
    /// </summary>
    [TestMethod]
    public void IsCaseSensitiveIdComparison_NotInClause_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM Quotes WHERE Id NOT IN @excludedIds"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_UpdateWhereClauseUnprotected_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "UPDATE Sources SET Title = @title, SourceId = @sid WHERE Id = @id;"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_HalfProtectedEquality_ColumnOnlyWrapped_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM Sources WHERE UPPER(Id) = @id;"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_HalfProtectedEquality_ParamOnlyWrapped_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM Sources WHERE Id = UPPER(@id);"));

    /// <summary>
    /// #210's scope expansion: an unwrapped id-to-id JOIN condition must be flagged too, per the
    /// developer's "wrap joins too" decision — both sides are already canonical by construction, but
    /// the guard no longer exempts column-to-column comparisons the way it originally did.
    /// </summary>
    [TestMethod]
    public void IsCaseSensitiveIdComparison_UnwrappedColumnToColumnJoin_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM Quotes q JOIN Sources s ON s.Id = q.SourceId AND s.IsDeleted = 0;"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_HalfProtectedJoin_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM Quotes q JOIN Sources s ON UPPER(s.Id) = q.SourceId AND s.IsDeleted = 0;"));

    /// <summary>A correlated-subquery id-to-id predicate is the same shape as a JOIN condition and must be flagged the same way.</summary>
    [TestMethod]
    public void IsCaseSensitiveIdComparison_UnwrappedCorrelatedSubqueryPredicate_ReturnsTrue()
        => Assert.IsTrue(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT (SELECT COUNT(*) FROM ConversationLines cl2 WHERE cl2.ConversationId = cl.ConversationId) FROM ConversationLines cl;"));

    // ── Patterns that must NOT be flagged ──────────────────────────────────────

    [TestMethod]
    public void IsCaseSensitiveIdComparison_FullyProtectedEquality_ReturnsFalse()
        => Assert.IsFalse(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM Sources WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 0;"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_FullyProtectedAliasedEquality_ReturnsFalse()
        => Assert.IsFalse(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM Sources s WHERE UPPER(s.SourceId) = UPPER(@sourceId);"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_ProtectedInClause_ReturnsFalse()
        => Assert.IsFalse(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM [Widgets] WHERE UPPER([Id]) IN @ids AND [IsDeleted] = 0"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_ProtectedNotInClause_ReturnsFalse()
        => Assert.IsFalse(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM Quotes WHERE UPPER(Id) NOT IN @excludedIds"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_SetClauseAssignmentOnly_WhereClauseProtected_ReturnsFalse()
        => Assert.IsFalse(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "UPDATE Sources SET Title = @title, SourceId = @sid, DateModified = @now WHERE UPPER(Id) = UPPER(@id);"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_NonIdColumnEquality_ReturnsFalse()
        => Assert.IsFalse(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "UPDATE Conversations SET Description = @description WHERE UPPER(Id) = UPPER(@id);"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_FullyProtectedJoin_ReturnsFalse()
        => Assert.IsFalse(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT * FROM Quotes q JOIN Sources s ON UPPER(s.Id) = UPPER(q.SourceId) AND s.IsDeleted = 0;"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_FullyProtectedCorrelatedSubqueryPredicate_ReturnsFalse()
        => Assert.IsFalse(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT (SELECT COUNT(*) FROM ConversationLines cl2 WHERE UPPER(cl2.ConversationId) = UPPER(cl.ConversationId)) FROM ConversationLines cl;"));

    [TestMethod]
    public void IsCaseSensitiveIdComparison_NoIdReferenceAtAll_ReturnsFalse()
        => Assert.IsFalse(SqlIdCaseGuard.IsCaseSensitiveIdComparison(
            "SELECT COUNT(*) FROM Sources WHERE IsDeleted = 0;"));

    [TestMethod]
    public void FindViolations_MultipleUnprotectedComparisons_ReturnsBothMatches()
    {
        var violations = SqlIdCaseGuard.FindViolations(
            "SELECT * FROM ConversationLines WHERE StageDirectionId = @id OR SoundCueId = @id;");

        Assert.HasCount(2, violations);
    }

    [TestMethod]
    public void FindViolations_NoViolations_ReturnsEmpty()
        => Assert.IsEmpty(SqlIdCaseGuard.FindViolations(
            "SELECT * FROM Sources WHERE UPPER(Id) = UPPER(@id);"));
}
