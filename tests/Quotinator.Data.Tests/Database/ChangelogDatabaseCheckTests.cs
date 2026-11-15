using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// #490, ADR 028: the changelog database is checked at startup as the main database is (decision F), and a
/// check that stops it is returned as an outcome, never thrown, so the caller falls back to the JSON files.
/// </summary>
[TestClass]
public class ChangelogDatabaseCheckTests
{
    private TempDirectory _directory = null!;
    private string _dbPath = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _directory = new TempDirectory("quotinator_changelog_check_");
        _dbPath = Path.Combine(_directory.Path, "changelog.db");
    }

    [TestCleanup]
    public void TestCleanup() => _directory.Dispose();

    /// <summary>A file that is not a database stops initialisation at Format, and nothing is written to it.</summary>
    [TestMethod]
    public async Task InitialiseAsync_NotADatabase_StopsAtFormatWithoutWriting()
    {
        DocumentedFormatFixtures.WriteNotADatabase(_dbPath);
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_dbPath)));

        DatabaseOperationResult result = await CreateInitializer().InitialiseAsync();

        Assert.IsFalse(result.Succeeded, "Initialisation succeeded over a file that is not a database.");
        Assert.AreEqual(DatabaseCheckLayer.Format, result.Check?.FirstFailure?.Layer);
        SqliteConnection.ClearAllPools();
        Assert.AreEqual(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_dbPath))), "The file was written to.");
    }

    /// <summary>Decision F, no database: the database is checked once it has been created.</summary>
    [TestMethod]
    public async Task InitialiseAsync_NoDatabase_ChecksTheCreatedDatabase()
    {
        DatabaseOperationResult result = await CreateInitializer().InitialiseAsync();

        Assert.IsTrue(result.Succeeded, "Initialisation failed to create the changelog database.");
        Assert.IsNotNull(result.Check, "The result does not carry a check.");
        Assert.IsTrue(result.Check.IsSound, $"First failure: {result.Check.FirstFailure?.Layer} ({result.Check.FirstFailure?.Finding})");
        Assert.DoesNotContain(l => l.Finding == LayerResults.NoFileYet, result.Check.Layers,
            "The kept check is the one taken before the database existed.");
    }

    /// <summary>The positive control: a restart over an up-to-date changelog database succeeds with a passed check.</summary>
    [TestMethod]
    public async Task InitialiseAsync_UpToDateDatabase_SucceedsCarryingAPassedCheck()
    {
        await CreateInitializer().InitialiseAsync();
        SqliteConnection.ClearAllPools();

        DatabaseOperationResult result = await CreateInitializer().InitialiseAsync();

        Assert.IsTrue(result.Succeeded, "Initialisation failed over a healthy changelog database.");
        Assert.IsNotNull(result.Check, "The result does not carry a check.");
        Assert.IsTrue(result.Check.IsSound, $"First failure: {result.Check.FirstFailure?.Layer} ({result.Check.FirstFailure?.Finding})");
    }

    private ChangelogDatabaseInitializer CreateInitializer() =>
        new(
            new SqliteConnectionFactory(_dbPath),
            NullLogger<ChangelogDatabaseInitializer>.Instance,
            new SqliteDatabaseCheck(
                DatabaseNames.Changelog, _dbPath, Path.Combine(_directory.Path, SqliteDatabaseCheck.WorkFolderName),
                ChangelogDatabaseInitializer.ExpectedSchemaScripts()));
}
