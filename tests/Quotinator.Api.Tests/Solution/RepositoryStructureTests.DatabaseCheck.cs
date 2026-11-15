using System.Reflection;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Database;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Solution;

public partial class RepositoryStructureTests
{
    /// <summary>
    /// The engine's own namespaces. A type from any of them reaching the Api layer through the check would
    /// tie everything above <c>Quotinator.Data</c> to SQLite.
    /// </summary>
    private static readonly string[] EngineNamespaces = ["Microsoft.Data.Sqlite", "SQLitePCL"];

    /// <summary>
    /// #490, decision A, ADR 028: no type the Api layer sees from the database check comes from the engine,
    /// so replacing the engine means writing a new <see cref="IDatabaseCheck"/> and nothing above
    /// <c>Quotinator.Data</c> changes. Walks every public member of the contract, and the generic arguments
    /// of every type it reaches, rather than trusting a list of members.
    /// </summary>
    [TestMethod]
    public void TheDatabaseCheckContract_NamesNoEngineType()
    {
        Type[] contract =
        [
            typeof(IDatabaseCheck),
            typeof(DatabaseCheckResult),
            typeof(DatabaseCheckLayerResult),
            typeof(DatabaseCheckLayer),
            typeof(DatabaseCheckLayerStatus),
            typeof(DatabaseCheckDepth),
            typeof(DatabaseOperationResult),
        ];

        Assert.IsNotEmpty(EngineTypesReachableFrom(typeof(EngineTypeLeak)),
            "Positive control failed: a member typed as a SqliteConnection was not detected, so the search is broken.");

        string[] leaks = [.. contract.SelectMany(EngineTypesReachableFrom).Distinct()];

        Assert.IsEmpty(leaks, "The database check's contract exposes an engine type (decision A, ADR 028):\n  " + string.Join("\n  ", leaks));
    }

    /// <summary>Every engine type reachable from the public members of <paramref name="type"/>, as "member: type".</summary>
    private static IEnumerable<string> EngineTypesReachableFrom(Type type)
    {
        const BindingFlags Public = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;

        IEnumerable<(string Member, Type Type)> memberTypes =
            type.GetProperties(Public).Select(p => ($"{type.Name}.{p.Name}", p.PropertyType))
                .Concat(type.GetFields(Public).Select(f => ($"{type.Name}.{f.Name}", f.FieldType)))
                .Concat(type.GetMethods(Public).Where(m => !m.IsSpecialName).SelectMany(m =>
                    m.GetParameters().Select(p => ($"{type.Name}.{m.Name}({p.Name})", p.ParameterType))
                        .Append(($"{type.Name}.{m.Name}", m.ReturnType))));

        foreach ((string member, Type memberType) in memberTypes)
        {
            foreach (Type reached in TypeAndItsArguments(memberType))
            {
                if (EngineNamespaces.Any(ns => reached.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true))
                    yield return $"{member}: {reached.FullName}";
            }
        }
    }

    private static IEnumerable<Type> TypeAndItsArguments(Type type)
    {
        Type element = type.HasElementType ? type.GetElementType()! : type;
        yield return element;

        if (element.IsGenericType)
        {
            foreach (Type argument in element.GetGenericArguments().SelectMany(TypeAndItsArguments))
                yield return argument;
        }
    }

    /// <summary>The positive control: a type that does expose an engine type.</summary>
    private sealed class EngineTypeLeak
    {
        public Task<IReadOnlyList<SqliteConnection>> Connections { get; } = Task.FromResult<IReadOnlyList<SqliteConnection>>([]);
    }
}
