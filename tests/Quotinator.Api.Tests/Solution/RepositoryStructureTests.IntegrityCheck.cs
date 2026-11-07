using System.Reflection;
using Quotinator.Core.Database;
using Quotinator.Data.Database;
using Quotinator.Data.Schema;

namespace Quotinator.Api.Tests.Solution;

public partial class RepositoryStructureTests
{
    /// <summary>
    /// #305: every database the application builds performs the integrity check, since each has its own
    /// migration path (ADR 018's reasoning, extended). A type that holds a migration list is a database's
    /// initializer, so it must implement <see cref="IIntegrityCheckedDatabase"/>; found by reflection, so
    /// the next database inherits the requirement without anyone remembering it.
    /// </summary>
    /// <remarks>
    /// A static class holding a list (<see cref="QuotinatorMigrations"/>) is exempt: it is the migrations,
    /// handed to an initializer, not a database of its own.
    /// </remarks>
    [TestMethod]
    public void EveryDatabaseWithMigrations_PerformsTheIntegrityCheck()
    {
        Assembly[] assemblies =
        [
            typeof(DatabaseInitializer).Assembly,
            typeof(QuotinatorDatabaseInitializer).Assembly,
            typeof(Program).Assembly,
        ];

        Type[] holders =
        [
            .. assemblies.SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !(t.IsAbstract && t.IsSealed))
                .Where(DeclaresAMigrationList),
        ];

        CollectionAssert.IsSubsetOf(
            new[] { typeof(DatabaseInitializer), typeof(ChangelogDatabaseInitializer) }, holders,
            "Positive control failed: the known databases were not found, so the search is broken.");

        string[] notChecking =
        [
            .. holders.Where(t => !typeof(IIntegrityCheckedDatabase).IsAssignableFrom(t)).Select(t => t.FullName ?? t.Name),
        ];

        Assert.IsEmpty(notChecking,
            "A database with its own migrations does not perform the integrity check (#305):\n  " + string.Join("\n  ", notChecking));
    }

    private static bool DeclaresAMigrationList(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Any(f => typeof(IEnumerable<SchemaMigration>).IsAssignableFrom(f.FieldType));
}
