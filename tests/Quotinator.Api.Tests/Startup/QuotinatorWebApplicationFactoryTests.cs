using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// What every host <see cref="QuotinatorWebApplicationFactory"/> builds is given, before any test's own
/// configuration (#424).
/// <para>
/// A test depends on nothing outside the project unless that is what it tests
/// (<c>docs/testing-policy.md</c>). Each of these holds one part of that for every host at once, so no
/// test has to remember it: the network, the bundled sources, and the build output's data folder with
/// whatever an earlier run left in it.
/// </para>
/// </summary>
[TestClass]
public class QuotinatorWebApplicationFactoryTests
{
    /// <summary>
    /// Left at its default, every host refreshed the bundled sources from GitHub at startup once the cached
    /// copy was a day old: found 2026-09-27, when two downloads 69 seconds apart held startup past the
    /// factory's 30-second wait and two unrelated tests timed out.
    /// </summary>
    [TestMethod]
    public void EveryHost_HasSourceAutoUpdateTurnedOff()
    {
        using QuotinatorWebApplicationFactory factory = new();

        Assert.AreEqual("false", factory.Services.GetRequiredService<IConfiguration>()["Quotinator:AutoUpdateSources"]);
    }

    /// <summary>
    /// The bundled sources change when the outside world does; a test that needs content is given its own.
    /// </summary>
    [TestMethod]
    public void EveryHost_HasBundledSourcesTurnedOff()
    {
        using QuotinatorWebApplicationFactory factory = new();

        Assert.AreEqual("false", factory.Services.GetRequiredService<IConfiguration>()["Quotinator:IncludeDefaultSources"]);
    }

    /// <summary>Read from the options the application resolved, not from the setting it was given.</summary>
    [TestMethod]
    public void EveryHost_HasItsOwnDataDirectory()
    {
        using QuotinatorWebApplicationFactory first = new();
        using QuotinatorWebApplicationFactory second = new();

        Assert.AreNotEqual(DataDirectoryOf(first), DataDirectoryOf(second));
    }

    /// <summary>
    /// The build output's data folder outlives every run, so whatever one run writes there, the next reads.
    /// </summary>
    [TestMethod]
    public void EveryHost_DataDirectoryIsNotTheBuildOutput()
    {
        using QuotinatorWebApplicationFactory factory = new();

        Assert.AreNotEqual(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data")).TrimEnd(Path.DirectorySeparatorChar),
            DataDirectoryOf(factory));
    }

    [TestMethod]
    public void EveryHost_DataDirectoryIsRemovedWithTheHost()
    {
        string directory;
        using (QuotinatorWebApplicationFactory factory = new())
            directory = DataDirectoryOf(factory);

        SqliteConnection.ClearAllPools();

        Assert.IsFalse(Directory.Exists(directory), $"{directory} outlived the host that used it");
    }

    /// <summary>
    /// A host whose initializer creates nothing still has the schema when it is given the prepared
    /// database: the table checked is the one the notification endpoint tests were missing.
    /// </summary>
    [TestMethod]
    public void PreparedDatabase_IsWhatTheHostStartsFrom()
    {
        using WebApplicationFactory<Program> factory = new QuotinatorWebApplicationFactory(preparedDatabase: true)
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<IDatabaseInitializer>(NoOpDatabaseInitializer.Instance)));

        using SqliteConnection connection = new(
            $"Data Source={factory.Services.GetRequiredService<DatabaseOptions>().DbPath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'System_AppVersion'";

        Assert.AreEqual(1L, (long)command.ExecuteScalar()!);
    }

    private static string DataDirectoryOf(QuotinatorWebApplicationFactory factory) =>
        Path.GetFullPath(Path.GetDirectoryName(factory.Services.GetRequiredService<DatabaseOptions>().DbPath)!)
            .TrimEnd(Path.DirectorySeparatorChar);
}
