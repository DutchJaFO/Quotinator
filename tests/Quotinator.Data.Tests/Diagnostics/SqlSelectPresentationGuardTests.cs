using Quotinator.Data.Diagnostics;

namespace Quotinator.Data.Tests.Diagnostics;

/// <summary>
/// Verifies <see cref="SqlSelectPresentationGuard"/> flags any unwrapped <c>*Id</c>-suffixed column
/// in a SELECT column list — primary key or foreign key, bare or alias-qualified, aliased or not —
/// and does not false-positive on an already-wrapped column, an alias name that happens to end in
/// "Id", a column that never appears in the query, a comparison-only (non-SELECT) usage, or the one
/// documented exemption (<c>InitiatedById</c>). See ADR 012 and #210.
/// </summary>
[TestClass]
public class SqlSelectPresentationGuardTests
{
    // ── Patterns that must be flagged ─────────────────────────────────────────

    [TestMethod]
    public void IsMissingPresentationNormalization_BareUnwrappedColumn_ReturnsTrue()
        => Assert.IsTrue(SqlSelectPresentationGuard.IsMissingPresentationNormalization(
            "SELECT Id, TableName, RecordId, Operation FROM System_AuditEntries;"));

    [TestMethod]
    public void IsMissingPresentationNormalization_AliasQualifiedUnwrappedColumn_ReturnsTrue()
        => Assert.IsTrue(SqlSelectPresentationGuard.IsMissingPresentationNormalization(
            "SELECT q.Id, q.QuoteText FROM Quotes q;"));

    /// <summary>
    /// Found live (#210): a Guid-typed PK/FK column selected with an explicit alias
    /// (<c>ser.Id AS SeriesId</c>) was previously assumed safe because the C# destination is
    /// Guid-typed — this guard makes no such assumption.
    /// </summary>
    [TestMethod]
    public void IsMissingPresentationNormalization_AliasedForeignKeyColumn_ReturnsTrue()
        => Assert.IsTrue(SqlSelectPresentationGuard.IsMissingPresentationNormalization(
            "SELECT q.Id, ser.Id AS SeriesId, uni.Id AS UniverseId FROM Quotes q " +
            "JOIN Series ser ON LOWER(ser.Id) = LOWER(q.SeriesId) " +
            "JOIN Universe uni ON LOWER(uni.Id) = LOWER(q.UniverseId);"));

    [TestMethod]
    public void IsMissingPresentationNormalization_MultipleUnwrappedColumns_FindsAll()
    {
        var violations = SqlSelectPresentationGuard.FindUnwrappedSelectColumns(
            "SELECT Id, BatchId, EntityId, ExistingBatchId FROM System_ImportActions;");

        CollectionAssert.AreEquivalent(
            new[] { "Id", "BatchId", "EntityId", "ExistingBatchId" }, violations.ToList());
    }

    [TestMethod]
    public void IsMissingPresentationNormalization_OneWrappedOneUnwrapped_FlagsOnlyUnwrapped()
    {
        var violations = SqlSelectPresentationGuard.FindUnwrappedSelectColumns(
            "SELECT Id, LOWER(BatchId) AS BatchId, EntityId FROM System_ImportActions;");

        CollectionAssert.AreEquivalent(new[] { "Id", "EntityId" }, violations.ToList());
    }

    [TestMethod]
    public void IsMissingPresentationNormalization_BracketQuotedColumn_ReturnsTrue()
        => Assert.IsTrue(SqlSelectPresentationGuard.IsMissingPresentationNormalization(
            "SELECT [Id], [Name] FROM [TestWidgets];"));

    // ── Patterns that must NOT be flagged ──────────────────────────────────────

    [TestMethod]
    public void IsMissingPresentationNormalization_WrappedWithAlias_ReturnsFalse()
        => Assert.IsFalse(SqlSelectPresentationGuard.IsMissingPresentationNormalization(
            "SELECT LOWER(Id) AS Id, LOWER(RecordId) AS RecordId, Operation FROM System_AuditEntries;"));

    [TestMethod]
    public void IsMissingPresentationNormalization_AllIdColumnsWrapped_ReturnsFalse()
        => Assert.IsFalse(SqlSelectPresentationGuard.IsMissingPresentationNormalization(
            "SELECT LOWER(q.Id) AS Id, LOWER(ser.Id) AS SeriesId, LOWER(uni.Id) AS UniverseId " +
            "FROM Quotes q " +
            "JOIN Series ser ON LOWER(ser.Id) = LOWER(q.SeriesId) " +
            "JOIN Universe uni ON LOWER(uni.Id) = LOWER(q.UniverseId);"));

    [TestMethod]
    public void IsMissingPresentationNormalization_ColumnNotSelected_ReturnsFalse()
        => Assert.IsFalse(SqlSelectPresentationGuard.IsMissingPresentationNormalization(
            "SELECT QuoteText, OriginalLanguage FROM Quotes;"));

    [TestMethod]
    public void IsMissingPresentationNormalization_ColumnOnlyInWhereClause_ReturnsFalse()
        => Assert.IsFalse(SqlSelectPresentationGuard.IsMissingPresentationNormalization(
            "SELECT QuoteText FROM System_ImportActions WHERE LOWER(BatchId) = LOWER(@batchId);"));

    /// <summary>
    /// The sole documented exemption — <c>InitiatedById</c> is polymorphic (an import batch UUID, an
    /// HTTP route, or an enrichment provider name), so forcing it lowercase would corrupt legitimate
    /// mixed-case content. Confirms the guard genuinely leaves it alone.
    /// </summary>
    [TestMethod]
    public void IsMissingPresentationNormalization_ExemptInitiatedById_ReturnsFalse()
        => Assert.IsFalse(SqlSelectPresentationGuard.IsMissingPresentationNormalization(
            "SELECT LOWER(Id) AS Id, LOWER(EntityId) AS EntityId, InitiatedById FROM System_ChangeLog;"));

    [TestMethod]
    public void IsMissingPresentationNormalization_EmptyString_ReturnsFalse()
        => Assert.IsFalse(SqlSelectPresentationGuard.IsMissingPresentationNormalization(string.Empty));

    [TestMethod]
    public void IsMissingPresentationNormalization_NoSelectClause_ReturnsFalse()
        => Assert.IsFalse(SqlSelectPresentationGuard.IsMissingPresentationNormalization(
            "UPDATE System_ImportActions SET Status = @status WHERE Id = @id;"));

    /// <summary>
    /// Found live (#210): a lookbehind-based first attempt at this guard couldn't reliably skip an
    /// arbitrary bracket-quoted table alias inside <c>LOWER(...)</c>, letting a bare "Id" match slip
    /// through even though the whole reference was wrapped. Confirms the strip-then-scan rewrite
    /// handles this exact shape.
    /// </summary>
    [TestMethod]
    public void IsMissingPresentationNormalization_WrappedBracketAliasQualifiedColumn_ReturnsFalse()
        => Assert.IsFalse(SqlSelectPresentationGuard.IsMissingPresentationNormalization(
            "SELECT LOWER([w].[Id]) AS WidgetId, [w].[Label] FROM [Widgets] [w];"));

    [TestMethod]
    public void FindUnwrappedSelectColumns_NoViolations_ReturnsEmpty()
        => Assert.IsEmpty(SqlSelectPresentationGuard.FindUnwrappedSelectColumns(
            "SELECT LOWER(Id) AS Id, LOWER(RecordId) AS RecordId FROM System_AuditEntries;"));
}
