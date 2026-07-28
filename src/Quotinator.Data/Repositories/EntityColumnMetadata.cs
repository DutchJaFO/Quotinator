using System.Reflection;
using Dapper.Contrib.Extensions;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Column metadata <see cref="RepositorySql"/>'s generic query builders need for one entity type:
/// every persisted column (used to validate a caller-supplied <c>ORDER BY</c> column, and to build an
/// explicit <c>SELECT</c> column list instead of <c>SELECT *</c>) and, among those, every id column
/// (primary key or foreign key) that must be read through <c>LOWER(...)</c> for canonical presentation
/// — see ADR 012.
/// </summary>
/// <remarks>
/// An interface, not a bare property, specifically so a future entity whose foreign key doesn't follow
/// this codebase's universal <c>*Id</c> naming convention can supply its own <see cref="IdColumnNames"/>
/// instead of relying on <see cref="ReflectedColumnMetadata"/>'s naming-convention inference — no such
/// entity exists today (confirmed: every persisted property across every domain entity in
/// <c>Quotinator.Core.Entities</c> and <c>Quotinator.Data.Entities</c> that is an id/FK follows the
/// convention), but the indirection means that exception, if one is ever needed, doesn't require
/// changing <see cref="RepositorySql"/> itself.
/// </remarks>
public interface IEntityColumnMetadata
{
    /// <summary>Every column the entity actually persists — used to validate a caller-supplied sort column before it reaches SQL, and to build the full <c>SELECT</c> column list.</summary>
    IReadOnlyList<string> ValidColumnNames { get; }

    /// <summary>The subset of <see cref="ValidColumnNames"/> that are id columns (primary key or foreign key) and must be read through <c>LOWER(...)</c>.</summary>
    IReadOnlyList<string> IdColumnNames { get; }
}

/// <summary>
/// Reflection-based, per-<see cref="Type"/>-cached <see cref="IEntityColumnMetadata"/> — the default
/// (and, today, only) implementation. A property counts as persisted the same way Dapper.Contrib itself
/// decides whether to write it: excluded only by <c>[Write(false)]</c> or <c>[Computed]</c>, mirroring
/// what <c>SqliteRepositoryBase{T}.ValidColumnNames</c> already computed before this class existed. An
/// id column is inferred by name — every persisted property ending in <c>Id</c>, matching this
/// project's universal naming convention and the same inference <c>SqlSelectPresentationGuard</c> uses
/// for hand-written SQL.
/// </summary>
internal sealed class ReflectedColumnMetadata : IEntityColumnMetadata
{
    private static readonly Dictionary<Type, ReflectedColumnMetadata> Cache = [];
    private static readonly Lock CacheLock = new();

    /// <inheritdoc/>
    public IReadOnlyList<string> ValidColumnNames { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> IdColumnNames { get; }

    private ReflectedColumnMetadata(Type type)
    {
        ValidColumnNames = type.GetProperties()
            .Where(p => p.GetCustomAttribute<WriteAttribute>()?.Write != false
                     && p.GetCustomAttribute<ComputedAttribute>() is null)
            .Select(p => p.Name)
            .ToList();
        IdColumnNames = ValidColumnNames
            .Where(name => name.EndsWith("Id", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>Returns the (cached) column metadata for <paramref name="type"/>.</summary>
    public static IEntityColumnMetadata For(Type type)
    {
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(type, out var metadata))
                Cache[type] = metadata = new ReflectedColumnMetadata(type);
            return metadata;
        }
    }
}
