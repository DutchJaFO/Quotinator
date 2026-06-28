using Quotinator.Engine.Database;

namespace Quotinator.Core.Tests.Data;

[TestClass]
public class QuotinatorMigrationsTests
{
    /// <summary>Versions must start at 1, increment by 1, and have no gaps or duplicates.</summary>
    [TestMethod]
    public void All_Versions_AreSequentialStartingAtOne()
    {
        var versions = QuotinatorMigrations.All.Select(m => m.Version).ToList();

        for (var i = 0; i < versions.Count; i++)
            Assert.AreEqual(i + 1, versions[i],
                $"Migration at index {i} has Version={versions[i]}; expected {i + 1}. " +
                "Migrations must be sequential starting at 1 with no gaps.");
    }

    /// <summary>Every migration must carry a non-empty SQL string.</summary>
    [TestMethod]
    public void All_SqlStrings_AreNonEmpty()
    {
        foreach (var migration in QuotinatorMigrations.All)
            Assert.IsFalse(string.IsNullOrWhiteSpace(migration.Sql),
                $"Migration v{migration.Version} has an empty Sql string.");
    }
}
