using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;

namespace Quotinator.Data.Tests.Helpers;

/// <summary>
/// Verifies that Dapper type handlers are registered before any test runs.
/// These tests are red if <see cref="AssemblySetup.Initialize"/> is removed or if
/// <c>DapperConfiguration.Configure</c> is called in a <c>[ClassInitialize]</c> that
/// races with another class initializer under parallel execution.
/// </summary>
[TestClass]
public class DapperSetupTests
{
    /// <summary>
    /// GuidHandler must be registered by [AssemblyInitialize] before this test runs.
    /// Without it, Dapper cannot map a TEXT column to Guid and throws InvalidCastException.
    /// </summary>
    [TestMethod]
    public async Task GuidHandler_RegisteredByAssemblySetup_DapperMapsGuidCorrectly()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        await conn.ExecuteAsync("CREATE TABLE T (Id TEXT NOT NULL PRIMARY KEY)");

        var id = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO T VALUES (@Id)", new { Id = id });

        var result = await conn.QuerySingleAsync<Guid>("SELECT Id FROM T");
        Assert.AreEqual(id, result);
    }

    /// <summary>
    /// SafeDateHandler must be registered by [AssemblyInitialize].
    /// Without it, Dapper cannot map a TEXT column to SafeValue&lt;DateTime?&gt; and throws InvalidCastException.
    /// </summary>
    [TestMethod]
    public async Task SafeDateHandler_RegisteredByAssemblySetup_DapperMapsDateCorrectly()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        await conn.ExecuteAsync("CREATE TABLE T (CreatedAt TEXT)");
        await conn.ExecuteAsync("INSERT INTO T VALUES ('2024-01-15 12:00:00')");

        var result = await conn.QuerySingleAsync<SafeValue<DateTime?>>("SELECT CreatedAt FROM T");
        Assert.IsTrue(result.Parsed.HasValue);
        Assert.AreEqual(2024, result.Parsed!.Value.Year);
    }

    /// <summary>
    /// JsonHandler&lt;IReadOnlyList&lt;string&gt;&gt; must be registered by [AssemblyInitialize].
    /// Without it, Dapper cannot map a JSON-array TEXT column to <see cref="IReadOnlyList{T}">IReadOnlyList&lt;string&gt;</see>.
    /// </summary>
    [TestMethod]
    public async Task JsonHandler_RegisteredByAssemblySetup_RoundTripsListThroughJsonColumn()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        await conn.ExecuteAsync("CREATE TABLE T (NoValueKnown TEXT NOT NULL DEFAULT '[]')");
        await conn.ExecuteAsync("INSERT INTO T (NoValueKnown) VALUES (@v)",
            new { v = (IReadOnlyList<string>)new List<string> { "date", "character" } });

        var result = await conn.QuerySingleAsync<IReadOnlyList<string>>("SELECT NoValueKnown FROM T");

        CollectionAssert.AreEqual(new[] { "date", "character" }, result.ToList());
    }

    /// <summary>An empty list must round-trip as the column's <c>'[]'</c> default, not as <c>NULL</c> or an error.</summary>
    [TestMethod]
    public async Task JsonHandler_EmptyList_RoundTripsAsEmptyJsonArray()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        await conn.ExecuteAsync("CREATE TABLE T (NoValueKnown TEXT NOT NULL DEFAULT '[]')");
        await conn.ExecuteAsync("INSERT INTO T (NoValueKnown) VALUES ('[]')");

        var result = await conn.QuerySingleAsync<IReadOnlyList<string>>("SELECT NoValueKnown FROM T");

        Assert.AreEqual(0, result.Count);
    }

    /// <summary>
    /// JsonHandler&lt;T&gt; is a reusable open generic, not a one-off for string lists — registering it
    /// for a different closed generic type (a string dictionary, the shape a future typed read of
    /// <c>System_ImportConflicts.MergedFields</c> would need) works the same way, proving it isn't
    /// hardcoded to any one JSON shape.
    /// </summary>
    [TestMethod]
    public async Task JsonHandler_RegisteredForDictionaryShape_RoundTripsThroughJsonColumn()
    {
        SqlMapper.AddTypeHandler(new JsonHandler<IReadOnlyDictionary<string, string>>());

        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        await conn.ExecuteAsync("CREATE TABLE T (MergedFields TEXT)");
        await conn.ExecuteAsync("INSERT INTO T (MergedFields) VALUES (@v)",
            new { v = (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["quoteText"] = "theirs", ["character"] = "ours" } });

        var result = await conn.QuerySingleAsync<IReadOnlyDictionary<string, string>>("SELECT MergedFields FROM T");

        Assert.AreEqual("theirs", result["quoteText"]);
        Assert.AreEqual("ours", result["character"]);
    }

    /// <summary>A NULL JSON column must round-trip as <c>null</c>, not throw or coerce to an empty instance — the correct behaviour for a nullable column like <c>MergedFields</c>, which is genuinely absent for non-merge policies.</summary>
    [TestMethod]
    public async Task JsonHandler_NullColumnValue_RoundTripsAsNull()
    {
        SqlMapper.AddTypeHandler(new JsonHandler<IReadOnlyDictionary<string, string>>());

        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        await conn.ExecuteAsync("CREATE TABLE T (MergedFields TEXT)");
        await conn.ExecuteAsync("INSERT INTO T (MergedFields) VALUES (NULL)");

        var result = await conn.QuerySingleAsync<IReadOnlyDictionary<string, string>?>("SELECT MergedFields FROM T");

        Assert.IsNull(result);
    }
}
