using System.Globalization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Schema;

/// <summary>
/// Compares a database's actual schema with the one its migration path defines (#305). Pure and
/// engine-neutral (ADR 003, design goal 5): it sees only <see cref="SchemaSnapshot"/> values.
/// </summary>
/// <remarks>
/// Everything compares as written: no official engine documentation states how a schema's text is cased,
/// so none is assumed, and a casing difference between the migration scripts is reported as soon as it
/// starts. A name whose casing differs is therefore one object missing and another extra. An automatic
/// index is matched by its columns, since its name is the engine's rather than the schema's. A missing or
/// extra table is one difference, not one per object it held.
/// </remarks>
public static class SchemaIntegrityComparer
{
    private static readonly StringComparer Name = StringComparer.Ordinal;

    /// <summary>Lists every way <paramref name="actual"/> differs from <paramref name="expected"/>.</summary>
    /// <param name="expected">The schema the database's migration path defines.</param>
    /// <param name="actual">The schema the database holds.</param>
    /// <returns>Every missing, different and extra object, sorted.</returns>
    public static SchemaIntegrityResult Compare(SchemaSnapshot expected, SchemaSnapshot actual)
    {
        List<SchemaDifference> differences = [];

        Match(expected.Tables, actual.Tables, t => t.Name, t => t.Name, SchemaObjectKind.Table, differences,
            (e, a) => CompareTable(e, a, differences));
        Match(expected.Views, actual.Views, v => v.Name, v => v.Name, SchemaObjectKind.View, differences,
            (e, a) => CompareOwner(e, a, SchemaObjectKind.View, differences));
        Match(expected.Triggers, actual.Triggers, t => t.Name, t => t.Name, SchemaObjectKind.Trigger, differences,
            (e, a) => CompareOwner(e, a, SchemaObjectKind.Trigger, differences));

        return new SchemaIntegrityResult
        {
            Differences =
            [
                .. differences
                    .OrderBy(d => d.ObjectName, Name)
                    .ThenBy(d => d.ObjectKind)
                    .ThenBy(d => d.Aspect, StringComparer.Ordinal),
            ],
        };
    }

    private static void CompareTable(SchemaTable expected, SchemaTable actual, List<SchemaDifference> differences)
    {
        string table = expected.Name;

        Match(expected.Columns, actual.Columns, c => c.Name, c => $"{table}.{c.Name}", SchemaObjectKind.Column, differences,
            (e, a) =>
            {
                string name = $"{table}.{e.Name}";
                AddIfDifferent(differences, SchemaObjectKind.Column, name, nameof(SchemaColumn.DeclaredType), e.DeclaredType, a.DeclaredType, StringComparer.Ordinal);
                AddIfDifferent(differences, SchemaObjectKind.Column, name, nameof(SchemaColumn.NotNull), Text(e.NotNull), Text(a.NotNull), StringComparer.Ordinal);
                AddIfDifferent(differences, SchemaObjectKind.Column, name, nameof(SchemaColumn.DefaultValue), e.DefaultValue, a.DefaultValue, StringComparer.Ordinal);
                AddIfDifferent(differences, SchemaObjectKind.Column, name, nameof(SchemaColumn.PrimaryKeyPosition), Text(e.PrimaryKeyPosition), Text(a.PrimaryKeyPosition), StringComparer.Ordinal);
            });

        Match(expected.ForeignKeys, actual.ForeignKeys, ForeignKeyName(table), ForeignKeyName(table), SchemaObjectKind.ForeignKey, differences,
            (e, a) =>
            {
                string name = ForeignKeyName(table)(e);
                AddIfDifferent(differences, SchemaObjectKind.ForeignKey, name, nameof(SchemaForeignKey.OnDelete), e.OnDelete, a.OnDelete, Name);
                AddIfDifferent(differences, SchemaObjectKind.ForeignKey, name, nameof(SchemaForeignKey.OnUpdate), e.OnUpdate, a.OnUpdate, Name);
            });

        Match(expected.Indexes.Where(i => !i.IsAutomatic), actual.Indexes.Where(i => !i.IsAutomatic),
            i => i.Name, i => $"{table}.{i.Name}", SchemaObjectKind.Index, differences,
            (e, a) =>
            {
                string name = $"{table}.{e.Name}";
                AddIfDifferent(differences, SchemaObjectKind.Index, name, nameof(SchemaIndex.IsUnique), Text(e.IsUnique), Text(a.IsUnique), StringComparer.Ordinal);
                AddIfDifferent(differences, SchemaObjectKind.Index, name, nameof(SchemaIndex.Columns), Columns(e.Columns), Columns(a.Columns), Name);
                AddIfDifferent(differences, SchemaObjectKind.Index, name, nameof(SchemaIndex.Filter), e.Filter, a.Filter, StringComparer.Ordinal);
            });

        Match(expected.Indexes.Where(i => i.IsAutomatic), actual.Indexes.Where(i => i.IsAutomatic),
            i => Columns(i.Columns), i => $"{table}({Columns(i.Columns)})", SchemaObjectKind.Index, differences,
            (e, a) => AddIfDifferent(differences, SchemaObjectKind.Index, $"{table}({Columns(e.Columns)})",
                nameof(SchemaIndex.IsUnique), Text(e.IsUnique), Text(a.IsUnique), StringComparer.Ordinal));
    }

    private static void CompareOwner(SchemaObject expected, SchemaObject actual, SchemaObjectKind kind, List<SchemaDifference> differences) =>
        AddIfDifferent(differences, kind, expected.Name, nameof(SchemaObject.Table), expected.Table, actual.Table, Name);

    /// <summary>
    /// Pairs expected and actual objects by key: an unpaired expected object is missing, an unpaired actual
    /// one is extra, and a pair is handed to <paramref name="compareMatched"/>.
    /// </summary>
    private static void Match<T>(
        IEnumerable<T> expected, IEnumerable<T> actual,
        Func<T, string> key, Func<T, string> displayName, SchemaObjectKind kind,
        List<SchemaDifference> differences, Action<T, T> compareMatched)
    {
        Dictionary<string, T> actualByKey = new(Name);
        foreach (T item in actual)
            actualByKey.TryAdd(key(item), item);

        HashSet<string> matched = new(Name);
        foreach (T item in expected)
        {
            string itemKey = key(item);
            if (actualByKey.TryGetValue(itemKey, out T? counterpart))
            {
                matched.Add(itemKey);
                compareMatched(item, counterpart);
            }
            else
            {
                differences.Add(new SchemaDifference(SchemaDifferenceKind.Missing, kind, displayName(item), null, null, null));
            }
        }

        foreach ((string itemKey, T item) in actualByKey)
        {
            if (!matched.Contains(itemKey))
                differences.Add(new SchemaDifference(SchemaDifferenceKind.Extra, kind, displayName(item), null, null, null));
        }
    }

    private static void AddIfDifferent(
        List<SchemaDifference> differences, SchemaObjectKind kind, string name, string aspect,
        string? expected, string? actual, StringComparer comparer)
    {
        bool same = expected is null || actual is null ? expected == actual : comparer.Equals(expected, actual);
        if (!same)
            differences.Add(new SchemaDifference(SchemaDifferenceKind.Different, kind, name, aspect, expected, actual));
    }

    private static Func<SchemaForeignKey, string> ForeignKeyName(string table) =>
        fk => $"{table}({Columns(fk.Columns)})->{fk.ReferencedTable}({Columns(fk.ReferencedColumns)})";

    private static string Columns(IReadOnlyList<string> columns) => string.Join(",", columns);

    private static string Text(bool value) => value ? "true" : "false";

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
