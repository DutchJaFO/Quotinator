using System.Reflection;
using Quotinator.Data.Diagnostics;
using Quotinator.Engine.Queries;
using Quotinator.Engine.Services;

namespace Quotinator.Engine.Tests.Security;

[TestClass]
public class SqlQueryGuardTests
{
    /// <summary>
    /// Reflects over every string constant in <see cref="Sql"/> and its nested classes,
    /// and asserts that none match the CVE-2025-6965 vulnerable aggregate pattern
    /// (aggregate function + GROUP BY or HAVING).
    /// Adding a new constant to <see cref="Sql"/> automatically adds it to this test.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(AllNamedSqlConstants))]
    public void SqlConstant_PassesAggregateGuard(string name, string sql)
    {
        Assert.IsFalse(
            SqlAggregateGuard.IsVulnerablePattern(sql),
            $"Sql.{name} contains a vulnerable aggregate pattern (aggregate + GROUP BY/HAVING). " +
            "Review the query and consult docs/sql-safety.md before suppressing.");
    }

    /// <summary>
    /// Verifies that every dynamically-assembled query produced by the <c>Sql.Quotes</c> factory
    /// methods passes the aggregate guard for all clause combinations.
    /// Covers the complete WHERE clause matrix from <c>SqliteQuoteService.BuildFilterWhere</c>
    /// and all field-filter variants from <c>Sql.SearchField</c>.
    /// Repository factory methods are covered in <c>Quotinator.Data.Tests.Security.SqlQueryGuardTests</c>.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(AssembledQueryCases))]
    public void AssembledQuery_PassesAggregateGuard(string label, string fullSql)
    {
        Assert.IsFalse(
            SqlAggregateGuard.IsVulnerablePattern(fullSql),
            $"Assembled query '{label}' contains a vulnerable aggregate pattern. " +
            "Review BuildFilterWhere or Sql.Quotes factory methods and consult docs/sql-safety.md.");
    }

    /// <summary>
    /// Asserts that the set of SQL constants containing aggregate functions exactly matches the
    /// documented inventory. If a new aggregate query is added, this test fails — update the list
    /// and confirm the query has been reviewed against docs/sql-safety.md.
    /// </summary>
    [TestMethod]
    public void AggregateQueries_MatchDocumentedInventory()
    {
        // These are the only SQL constants permitted to contain aggregate function calls.
        // All were reviewed when the CVE-2025-6965 guard was implemented (2026-06-19).
        // None use GROUP BY or HAVING, so none trigger the vulnerability.
        var documented = new HashSet<string>
        {
            "Quotes.CountAll",                    // COUNT(*)
            "Quotes.CountActive",                 // COUNT(*)
            "Quotes.CountForRandomBase",          // COUNT(*) — private base for CountRandom factory
            "Quotes.CountForGetAllBase",          // COUNT(*) — private base for CountGetAll factory
            "QuoteGenres.CountAll",               // COUNT(*)
            "SourceTranslations.CountForSource",  // COUNT(*)
            "Characters.CountActive",             // COUNT(*)
            "People.CountActive",                 // COUNT(*)
            "Sources.CountActive",                // COUNT(*)
            "Characters.CountActiveReferences",   // COUNT(*) — #59 reversal reference check
            "People.CountActiveReferences",       // COUNT(*) — #59 reversal reference check
            "Sources.CountActiveReferences",      // COUNT(*) x2 (subqueries) — #59 reversal reference check
            "StageDirections.CountActiveReferences", // COUNT(*) — #68 reversal reference check
            "SoundCues.CountActiveReferences",       // COUNT(*) — #68 reversal reference check
            "ConversationLines.SelectMembershipForQuote", // COUNT(*) subquery, no GROUP BY — #69 conversation-membership lookup
            "Series.CountActive",                 // COUNT(*) — #180
            "Series.CountActiveReferences",        // COUNT(*) — #180 reversal reference check
            "Universe.CountActive",               // COUNT(*) — #180
            "Universe.CountActiveReferences",      // COUNT(*) — #180 reversal reference check
        };

        var actual = EnumerateSqlConstants()
            .Where(x => SqlAggregateGuard.HasAggregateFunction(x.Sql))
            .Select(x => x.Name)
            .ToHashSet();

        CollectionAssert.AreEquivalent(
            documented.ToList(),
            actual.ToList(),
            "The set of SQL constants containing aggregate functions has changed. " +
            "Review any new or removed aggregate queries against docs/sql-safety.md " +
            "and update the documented list in this test.");
    }

    public static IEnumerable<object[]> AssembledQueryCases()
    {
        // Full matrix of filter combinations — exercises every branch in BuildFilterWhere.
        var filterCases = new (string Label, string[]? Types, string[]? Genres, string? Lang, string? Character, string? Author, string? Source, int? YearFrom, int? YearTo)[]
        {
            ("no filters",      null,               null,               null, null,       null,    null,    null, null),
            ("type",            ["movie"],          null,               null, null,       null,    null,    null, null),
            ("genre",           null,               ["drama"],          null, null,       null,    null,    null, null),
            ("lang",            null,               null,               "nl", null,       null,    null,    null, null),
            ("character",       null,               null,               null, "Hannibal", null,    null,    null, null),
            ("author",          null,               null,               null, null,       "Twain", null,    null, null),
            ("source",          null,               null,               null, null,       null,    "Matrix",null, null),
            ("yearFrom",        null,               null,               null, null,       null,    null,    1990, null),
            ("yearTo",          null,               null,               null, null,       null,    null,    null, 2000),
            ("all filters",     ["tv"],             ["comedy"],         "de", "Sherlock", "Doyle", "BBC",   1900, 2020),
            ("multi-type",      ["movie", "book"],  null,               null, null,       null,    null,    null, null),
            ("multi-genre",     null,               ["sci-fi", "drama"],null, null,       null,    null,    null, null),
        };

        foreach (var (label, types, genres, lang, character, author, source, yearFrom, yearTo) in filterCases)
        {
            var (whereClause, _) = SqliteQuoteService.BuildFilterWhere(
                types, genres, lang, character, author, source, yearFrom, yearTo);

            yield return [$"CountRandom({label})",    Sql.Quotes.CountRandom(whereClause)];
            yield return [$"CountGetAll({label})",    Sql.Quotes.CountGetAll(whereClause)];
            yield return [$"SelectRandom({label})",   Sql.Quotes.SelectRandom(whereClause)];
            yield return [$"SelectPaged({label})",    Sql.Quotes.SelectPaged(whereClause)];
        }

        // SelectById has no dynamic clauses — one case covers it.
        yield return ["SelectById()", Sql.Quotes.SelectById()];

        // SelectRawById has no dynamic clauses — one case covers it.
        yield return ["SelectRawById()", Sql.Quotes.SelectRawById()];

        // SelectSearch: one case per field-filter constant × a representative where clause.
        var (baseWhere, _) = SqliteQuoteService.BuildFilterWhere(["movie"], ["drama"], null, null, null, null, null, null);
        foreach (var (fieldName, fieldFilter) in new[]
        {
            (nameof(Sql.SearchField.Quote),     Sql.SearchField.Quote),
            (nameof(Sql.SearchField.Source),    Sql.SearchField.Source),
            (nameof(Sql.SearchField.Character), Sql.SearchField.Character),
            (nameof(Sql.SearchField.Author),    Sql.SearchField.Author),
            (nameof(Sql.SearchField.All),       Sql.SearchField.All),
        })
        {
            yield return [$"SelectSearch(field={fieldName})", Sql.Quotes.SelectSearch(baseWhere, fieldFilter)];
        }
    }

    /// <summary>Enumerates all string constants in <see cref="Sql"/> and its nested classes.</summary>
    public static IEnumerable<object[]> AllNamedSqlConstants()
        => EnumerateSqlConstants().Select(x => new object[] { x.Name, x.Sql });

    private static IEnumerable<(string Name, string Sql)> EnumerateSqlConstants()
        => typeof(Sql)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static)
            .SelectMany(t => t
                .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => ($"{t.Name}.{f.Name}", (string)f.GetValue(null)!)));
}
