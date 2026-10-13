using Quotinator.Core.Database;
using Quotinator.Data.Database;

namespace Quotinator.Core.Tests.Database;

[TestClass]
public class QuotinatorMigrationsTests
{
    /// <summary>Versions must start at 1, increment by 1, and have no gaps or duplicates.</summary>
    [TestMethod]
    public void All_Versions_AreSequentialStartingAtOne()
    {
        List<int> versions = [.. QuotinatorMigrations.All.Select(m => m.Version)];

        for (int i = 0; i < versions.Count; i++)
            Assert.AreEqual(i + 1, versions[i],
                $"Migration at index {i} has Version={versions[i]}; expected {i + 1}. " +
                "Migrations must be sequential starting at 1 with no gaps.");
    }

    /// <summary>Every migration must carry a non-empty SQL string.</summary>
    [TestMethod]
    public void All_SqlStrings_AreNonEmpty()
    {
        foreach (SchemaMigration migration in QuotinatorMigrations.All)
            Assert.IsFalse(string.IsNullOrWhiteSpace(migration.Sql),
                $"Migration v{migration.Version} has an empty Sql string.");
    }

    /// <summary>
    /// #371: every migration must say what it did, so a notification can name it rather than only its
    /// number. Derived from the list, so a future migration added without a description fails here.
    /// </summary>
    [TestMethod]
    public void All_Descriptions_AreNonEmpty()
    {
        foreach (SchemaMigration migration in QuotinatorMigrations.All)
            Assert.IsFalse(string.IsNullOrWhiteSpace(migration.Description),
                $"Migration v{migration.Version} has an empty Description.");
    }
}
