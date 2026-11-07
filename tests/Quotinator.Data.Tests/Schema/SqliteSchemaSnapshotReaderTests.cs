using Microsoft.Data.Sqlite;
using Quotinator.Data.Schema;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Schema;

/// <summary>
/// #305: <see cref="SqliteSchemaSnapshotReader"/> captures every aspect the integrity check compares from a
/// real database, and nothing the engine creates for its own bookkeeping.
/// </summary>
[TestClass]
public class SqliteSchemaSnapshotReaderTests
{
    [TestCleanup]
    public void TestCleanup() => SqliteConnection.ClearAllPools();

    [TestMethod]
    public async Task ReadsTablesColumnsForeignKeysIndexesViewsAndTriggers()
    {
        using TempDatabase database = new(
        [
            "CREATE TABLE Parent (Id TEXT NOT NULL PRIMARY KEY);",
            "CREATE TABLE Child (" +
                "Id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "ParentId TEXT NOT NULL REFERENCES Parent(Id) ON DELETE CASCADE, " +
                "Name TEXT DEFAULT 'x', " +
                "UNIQUE (Name));",
            "CREATE INDEX IX_Child_ParentId ON Child(ParentId) WHERE ParentId IS NOT NULL;",
            "CREATE VIEW ChildNames AS SELECT Name FROM Child;",
            "CREATE TRIGGER TR_Child_Touch AFTER INSERT ON Child BEGIN SELECT 1; END;",
        ]);

        using SqliteConnection connection = new($"Data Source={database.DbPath}");
        await connection.OpenAsync(TestContext.CancellationToken);

        SchemaSnapshot snapshot = await new SqliteSchemaSnapshotReader().ReadAsync(connection);

        Assert.AreSequenceEqual(["Child", "Parent"], [.. snapshot.Tables.Select(t => t.Name)], SequenceOrder.InAnyOrder,
            "user tables only: sqlite_sequence, which AUTOINCREMENT creates, is the engine's");

        SchemaTable child = snapshot.Tables.Single(t => t.Name == "Child");

        Assert.AreSequenceEqual(["Id", "ParentId", "Name"], [.. child.Columns.Select(c => c.Name)]);
        SchemaColumn id = child.Columns[0];
        Assert.AreEqual("INTEGER", id.DeclaredType);
        Assert.AreEqual(1, id.PrimaryKeyPosition);
        SchemaColumn parentId = child.Columns[1];
        Assert.IsTrue(parentId.NotNull);
        Assert.AreEqual(0, parentId.PrimaryKeyPosition);
        SchemaColumn name = child.Columns[2];
        Assert.AreEqual("TEXT", name.DeclaredType);
        Assert.IsFalse(name.NotNull);
        Assert.AreEqual("'x'", name.DefaultValue);

        SchemaForeignKey foreignKey = child.ForeignKeys.Single();
        Assert.AreSequenceEqual(["ParentId"], foreignKey.Columns);
        Assert.AreEqual("Parent", foreignKey.ReferencedTable);
        Assert.AreSequenceEqual(["Id"], foreignKey.ReferencedColumns);
        Assert.AreEqual("CASCADE", foreignKey.OnDelete);
        Assert.AreEqual("NO ACTION", foreignKey.OnUpdate);

        SchemaIndex named = child.Indexes.Single(i => i.Name == "IX_Child_ParentId");
        Assert.IsFalse(named.IsUnique);
        Assert.IsFalse(named.IsAutomatic);
        Assert.AreSequenceEqual(["ParentId"], named.Columns);
        Assert.AreEqual("ParentId IS NOT NULL", named.Filter);

        SchemaIndex automatic = child.Indexes.Single(i => i.IsAutomatic);
        Assert.IsTrue(automatic.IsUnique);
        Assert.AreSequenceEqual(["Name"], automatic.Columns);
        Assert.IsNull(automatic.Filter);

        SchemaTable parent = snapshot.Tables.Single(t => t.Name == "Parent");
        Assert.IsTrue(parent.Indexes.Single().IsAutomatic, "a TEXT primary key is backed by an automatic unique index");

        Assert.AreEqual(new SchemaObject("ChildNames", "ChildNames"), snapshot.Views.Single(),
            "a view belongs to itself, as the engine records it");
        Assert.AreEqual(new SchemaObject("TR_Child_Touch", "Child"), snapshot.Triggers.Single());
    }

    public TestContext TestContext { get; set; }
}
