using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Models;

namespace Quotinator.Core.Tests.Data;

/// <summary>
/// Verifies that Dapper type handlers are registered before any test runs.
/// These tests are red if AssemblySetup.Initialize is removed or if
/// DapperConfiguration.Configure is called in a [ClassInitialize] that
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
}
