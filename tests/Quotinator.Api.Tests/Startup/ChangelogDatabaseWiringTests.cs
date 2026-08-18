using System.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Data.Connections;
using Quotinator.Data.Paths;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #309 step 14 — the changelog database must survive the life of the process.
/// <para>
/// It was originally wired as a shared-cache in-memory database
/// (<c>file:quotinatorchangelog?mode=memory&amp;cache=shared</c>) kept alive by one deliberately-held
/// open connection. Found live during #309's own T2 run: thirteen minutes after a clean import of 126
/// entries, every read failed with <c>no such table: Changelog_Entry</c> and fell back to the JSON
/// service permanently, with no process restart in between. Nothing was user-visible precisely because
/// the fallback works — which is why it went unnoticed.
/// </para>
/// <para>
/// These assert the real registration from <c>Program.cs</c> through the live DI container. A test that
/// built its own connection factory would prove only that SQLite persists files, not that this
/// application asked it to.
/// </para>
/// </summary>
[TestClass]
public class ChangelogDatabaseWiringTests
{
    [TestMethod]
    public void ChangelogDatabase_IsNotAnInMemoryDatabase()
    {
        using WebApplicationFactory<Program> factory = new QuotinatorWebApplicationFactory();

        string connectionString = ResolveChangelogConnectionString(factory);

        Assert.IsFalse(
            connectionString.Contains("mode=memory", StringComparison.OrdinalIgnoreCase),
            "the changelog database is in-memory, so it ceases to exist the moment its last connection "
            + $"closes — the database-backed read path then silently dies for the life of the process. "
            + $"Connection string was: {connectionString}");
    }

    [TestMethod]
    public void ChangelogDatabase_IsAFileNamedAlongsideTheMainDatabase()
    {
        using WebApplicationFactory<Program> factory = new QuotinatorWebApplicationFactory();

        string connectionString = ResolveChangelogConnectionString(factory);

        Assert.IsTrue(
            connectionString.Contains(DataPaths.ChangelogDatabaseFile, StringComparison.OrdinalIgnoreCase),
            $"expected the changelog database to be the file '{DataPaths.ChangelogDatabaseFile}' in the "
            + $"data directory, alongside '{DataPaths.DatabaseFile}'. Connection string was: {connectionString}");
    }

    private static string ResolveChangelogConnectionString(WebApplicationFactory<Program> factory)
    {
        IDbConnectionFactory connectionFactory = factory.Services
            .GetRequiredKeyedService<IDbConnectionFactory>(DatabaseConnectionKeys.Changelog);

        using IDbConnection connection = connectionFactory.CreateConnection();
        return connection.ConnectionString;
    }
}
