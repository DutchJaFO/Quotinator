using Dapper;
using Microsoft.Data.Sqlite;
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
    /// JsonStringListHandler must be registered by [AssemblyInitialize]. Without it, Dapper cannot
    /// map a JSON-array TEXT column to <see cref="IReadOnlyList{T}">IReadOnlyList&lt;string&gt;</see>.
    /// </summary>
    [TestMethod]
    public async Task JsonStringListHandler_RegisteredByAssemblySetup_RoundTripsListThroughJsonColumn()
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
    public async Task JsonStringListHandler_EmptyList_RoundTripsAsEmptyJsonArray()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        await conn.ExecuteAsync("CREATE TABLE T (NoValueKnown TEXT NOT NULL DEFAULT '[]')");
        await conn.ExecuteAsync("INSERT INTO T (NoValueKnown) VALUES ('[]')");

        var result = await conn.QuerySingleAsync<IReadOnlyList<string>>("SELECT NoValueKnown FROM T");

        Assert.AreEqual(0, result.Count);
    }
}
