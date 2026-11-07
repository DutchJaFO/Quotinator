using Quotinator.Data.Enums;
using Quotinator.Data.Schema;

namespace Quotinator.Data.Tests.Schema;

/// <summary>
/// #305: <see cref="SchemaIntegrityComparer"/> lists every way an actual schema differs from the one its
/// migration path defines. Pure and engine-neutral, so every aspect is proven here without a database.
/// <para>
/// Object names in a difference follow one convention, which these tests fix: a table by its name, a column
/// as <c>Table.Column</c>, a named index as <c>Table.Index</c>, an automatic index as
/// <c>Table(columns)</c>, a foreign key as <c>Table(columns)-&gt;Referenced(columns)</c>, and a view or
/// trigger by its name. A differing property is named by its <see cref="SchemaDifference.Aspect"/>, the
/// property's own name on the model.
/// </para>
/// </summary>
[TestClass]
public class SchemaIntegrityComparerTests
{
    [TestMethod]
    public void IdenticalSnapshots_AreIntact()
    {
        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), Sample());

        Assert.IsTrue(result.IsIntact);
        Assert.IsEmpty(result.Differences);
    }

    [TestMethod]
    public void TwoEmptySnapshots_AreIntact()
    {
        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(SchemaSnapshot.Empty, SchemaSnapshot.Empty);

        Assert.IsTrue(result.IsIntact);
        Assert.IsEmpty(result.Differences);
    }

    [TestMethod]
    public void MissingTable_IsADifference()
    {
        SchemaSnapshot actual = Sample() with { Tables = [.. Sample().Tables.Where(t => t.Name != "Child")] };

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        Assert.IsFalse(result.IsIntact);
        SchemaDifference only = OnlyDifference(result);
        Assert.AreEqual(SchemaDifferenceKind.Missing, only.Kind);
        Assert.AreEqual(SchemaObjectKind.Table, only.ObjectKind);
        Assert.AreEqual("Child", only.ObjectName);
        Assert.IsNull(only.Aspect, "a missing table is one difference, not one per column it held");
    }

    [TestMethod]
    public void MissingColumn_IsADifference()
    {
        SchemaSnapshot actual = WithChild(c => c with { Columns = [.. c.Columns.Where(col => col.Name != "Name")] });

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        Assert.IsFalse(result.IsIntact);
        SchemaDifference only = OnlyDifference(result);
        Assert.AreEqual(SchemaDifferenceKind.Missing, only.Kind);
        Assert.AreEqual(SchemaObjectKind.Column, only.ObjectKind);
        Assert.AreEqual("Child.Name", only.ObjectName);
    }

    [TestMethod]
    [DataRow(nameof(SchemaColumn.DeclaredType))]
    [DataRow(nameof(SchemaColumn.NotNull))]
    [DataRow(nameof(SchemaColumn.DefaultValue))]
    [DataRow(nameof(SchemaColumn.PrimaryKeyPosition))]
    public void ColumnPropertyDiffers_IsADifference(string aspect)
    {
        SchemaSnapshot actual = WithChild(c => c with
        {
            Columns = [.. c.Columns.Select(col => col.Name != "Name" ? col : aspect switch
            {
                nameof(SchemaColumn.DeclaredType)       => col with { DeclaredType = "INTEGER" },
                nameof(SchemaColumn.NotNull)            => col with { NotNull = true },
                nameof(SchemaColumn.DefaultValue)       => col with { DefaultValue = "'other'" },
                nameof(SchemaColumn.PrimaryKeyPosition) => col with { PrimaryKeyPosition = 2 },
                _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, null),
            })],
        });

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        Assert.IsFalse(result.IsIntact);
        SchemaDifference only = OnlyDifference(result);
        Assert.AreEqual(SchemaDifferenceKind.Different, only.Kind);
        Assert.AreEqual(SchemaObjectKind.Column, only.ObjectKind);
        Assert.AreEqual("Child.Name", only.ObjectName);
        Assert.AreEqual(aspect, only.Aspect);
    }

    [TestMethod]
    public void MissingForeignKey_IsADifference()
    {
        SchemaSnapshot actual = WithChild(c => c with { ForeignKeys = [] });

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        Assert.IsFalse(result.IsIntact);
        SchemaDifference only = OnlyDifference(result);
        Assert.AreEqual(SchemaDifferenceKind.Missing, only.Kind);
        Assert.AreEqual(SchemaObjectKind.ForeignKey, only.ObjectKind);
        Assert.AreEqual("Child(ParentId)->Parent(Id)", only.ObjectName);
    }

    [TestMethod]
    public void ForeignKeyActionDiffers_IsADifference()
    {
        SchemaSnapshot actual = WithChild(c => c with { ForeignKeys = [c.ForeignKeys[0] with { OnDelete = "NO ACTION" }] });

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        Assert.IsFalse(result.IsIntact);
        SchemaDifference only = OnlyDifference(result);
        Assert.AreEqual(SchemaDifferenceKind.Different, only.Kind);
        Assert.AreEqual(SchemaObjectKind.ForeignKey, only.ObjectKind);
        Assert.AreEqual(nameof(SchemaForeignKey.OnDelete), only.Aspect);
        Assert.AreEqual("CASCADE", only.Expected);
        Assert.AreEqual("NO ACTION", only.Actual);
    }

    [TestMethod]
    public void MissingIndex_IsADifference()
    {
        SchemaSnapshot actual = WithChild(c => c with { Indexes = [.. c.Indexes.Where(i => i.IsAutomatic)] });

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        Assert.IsFalse(result.IsIntact);
        SchemaDifference only = OnlyDifference(result);
        Assert.AreEqual(SchemaDifferenceKind.Missing, only.Kind);
        Assert.AreEqual(SchemaObjectKind.Index, only.ObjectKind);
        Assert.AreEqual("Child.IX_Child_ParentId", only.ObjectName);
    }

    [TestMethod]
    public void IndexUniquenessDiffers_IsADifference()
    {
        SchemaSnapshot actual = WithChild(c => c with
        {
            Indexes = [.. c.Indexes.Select(i => i.IsAutomatic ? i : i with { IsUnique = true })],
        });

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        Assert.IsFalse(result.IsIntact);
        SchemaDifference only = OnlyDifference(result);
        Assert.AreEqual(SchemaDifferenceKind.Different, only.Kind);
        Assert.AreEqual(SchemaObjectKind.Index, only.ObjectKind);
        Assert.AreEqual(nameof(SchemaIndex.IsUnique), only.Aspect);
    }

    /// <summary>
    /// An automatic index's name is the engine's, not the schema's: the same <c>UNIQUE</c> constraint gets
    /// a different generated name when a table is rebuilt by a migration. It is matched by its columns.
    /// </summary>
    [TestMethod]
    public void AutomaticIndexNameDiffers_IsNotADifference()
    {
        SchemaSnapshot actual = WithChild(c => c with
        {
            Indexes = [.. c.Indexes.Select(i => i.IsAutomatic ? i with { Name = "sqlite_autoindex_Child_7" } : i)],
        });

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        Assert.IsTrue(result.IsIntact);
        Assert.IsEmpty(result.Differences);

        SchemaSnapshot uniquenessLost = WithChild(c => c with
        {
            Indexes = [.. c.Indexes.Select(i => i.IsAutomatic ? i with { Name = "sqlite_autoindex_Child_7", Columns = ["Id"] } : i)],
        });
        Assert.IsFalse(SchemaIntegrityComparer.Compare(Sample(), uniquenessLost).IsIntact,
            "control: an automatic index on different columns is still a difference");
    }

    [TestMethod]
    public void MissingViewOrTrigger_IsADifference()
    {
        SchemaSnapshot actual = Sample() with { Views = [], Triggers = [] };

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        Assert.IsFalse(result.IsIntact);
        Assert.HasCount(2, result.Differences);
        Assert.Contains(
            d => d.Kind == SchemaDifferenceKind.Missing && d.ObjectKind == SchemaObjectKind.View && d.ObjectName == "ChildNames",
            result.Differences);
        Assert.Contains(
            d => d.Kind == SchemaDifferenceKind.Missing && d.ObjectKind == SchemaObjectKind.Trigger && d.ObjectName == "TR_Child_Touch",
            result.Differences);
    }

    [TestMethod]
    public void ExtraObjects_AreReportedButIntact()
    {
        SchemaTable extraTable = new("Stray", [new SchemaColumn("Id", "TEXT", true, null, 1)], [], []);
        SchemaSnapshot actual = WithChild(c => c with
        {
            Columns     = [.. c.Columns, new SchemaColumn("Added", "TEXT", false, null, 0)],
            ForeignKeys = [.. c.ForeignKeys, new SchemaForeignKey(["Added"], "Parent", ["Id"], "NO ACTION", "NO ACTION")],
            Indexes     = [.. c.Indexes, new SchemaIndex("IX_Child_Added", false, ["Added"], null, false)],
        });
        actual = actual with { Tables = [.. actual.Tables, extraTable] };

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        Assert.IsTrue(result.IsIntact, "an extra object is reported, never a failure");
        Assert.HasCount(4, result.Differences);
        Assert.IsTrue(result.Differences.All(d => d.Kind == SchemaDifferenceKind.Extra));
        Assert.AreSequenceEqual(
            [SchemaObjectKind.Table, SchemaObjectKind.Column, SchemaObjectKind.ForeignKey, SchemaObjectKind.Index],
            [.. result.Differences.Select(d => d.ObjectKind)], SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public void SeveralDifferences_AreAllReportedInOrder()
    {
        SchemaSnapshot actual = Sample() with
        {
            Tables =
            [
                .. Sample().Tables.Where(t => t.Name != "Parent").Select(t => t with
                {
                    Columns = [.. t.Columns.Where(c => c.Name != "Name")],
                }),
            ],
            Views = [],
        };

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        string[] reported = [.. result.Differences.Select(d => $"{d.Kind} {d.ObjectKind} {d.ObjectName}")];
        Assert.AreSequenceEqual(
            ["Missing Column Child.Name", "Missing View ChildNames", "Missing Table Parent"],
            reported,
            "every difference is reported, sorted by object name, so a reason reads the same on every run");
    }

    [TestMethod]
    public void NameCasingDiffers_IsNotADifference()
    {
        SchemaSnapshot actual = Sample() with
        {
            Tables = [.. Sample().Tables.Select(t => t with
            {
                Name    = t.Name.ToUpperInvariant(),
                Columns = [.. t.Columns.Select(c => c with { Name = c.Name.ToLowerInvariant() })],
            })],
        };

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        Assert.IsTrue(result.IsIntact, "names are identifiers and compare case-insensitively (ADR 024)");
        Assert.IsEmpty(result.Differences);
    }

    [TestMethod]
    public void DeclaredTypeCasingDiffers_IsADifference()
    {
        SchemaSnapshot actual = WithChild(c => c with
        {
            Columns = [.. c.Columns.Select(col => col.Name == "Name" ? col with { DeclaredType = "text" } : col)],
        });

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(Sample(), actual);

        SchemaDifference only = OnlyDifference(result);
        Assert.AreEqual(nameof(SchemaColumn.DeclaredType), only.Aspect,
            "a declared type is content, compared as written (ADR 024)");
        Assert.AreEqual("TEXT", only.Expected);
        Assert.AreEqual("text", only.Actual);
    }

    private static SchemaDifference OnlyDifference(SchemaIntegrityResult result)
    {
        Assert.HasCount(1, result.Differences,
            "differences: " + string.Join("; ", result.Differences.Select(d => $"{d.Kind} {d.ObjectKind} {d.ObjectName} {d.Aspect}")));
        return result.Differences[0];
    }

    private static SchemaSnapshot WithChild(Func<SchemaTable, SchemaTable> change)
    {
        SchemaSnapshot sample = Sample();
        return sample with { Tables = [.. sample.Tables.Select(t => t.Name == "Child" ? change(t) : t)] };
    }

    /// <summary>A schema exercising every compared aspect. Built fresh on each call, so equality is by value.</summary>
    private static SchemaSnapshot Sample() => new(
        Tables:
        [
            new SchemaTable(
                "Parent",
                [new SchemaColumn("Id", "TEXT", true, null, 1)],
                [],
                [new SchemaIndex("sqlite_autoindex_Parent_1", true, ["Id"], null, true)]),
            new SchemaTable(
                "Child",
                [
                    new SchemaColumn("Id", "INTEGER", false, null, 1),
                    new SchemaColumn("ParentId", "TEXT", true, null, 0),
                    new SchemaColumn("Name", "TEXT", false, "'x'", 0),
                ],
                [new SchemaForeignKey(["ParentId"], "Parent", ["Id"], "CASCADE", "NO ACTION")],
                [
                    new SchemaIndex("sqlite_autoindex_Child_1", true, ["Name"], null, true),
                    new SchemaIndex("IX_Child_ParentId", false, ["ParentId"], "ParentId IS NOT NULL", false),
                ]),
        ],
        Views:    [new SchemaObject("ChildNames", "Child")],
        Triggers: [new SchemaObject("TR_Child_Touch", "Child")]);
}
