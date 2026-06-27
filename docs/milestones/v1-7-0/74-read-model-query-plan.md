# Issue #74 — Add read-model query pattern to Quotinator.Data for join and projection queries

**Milestone:** v1.7.0  
**Status:** Open  

---

## Depends on

- **#73** (audit trail) — complete ✅

---

## Problem

`IRepository<T>` targets one flat table. Queries that join two or more tables, or return a projection (a subset or combination of columns), cannot be expressed through it. The first real consumer is **#121** (`SqliteQuoteService` refactor), which joins `Quotes → Sources → Characters → People`.

---

## Design decisions (all confirmed)

| Decision | Choice |
|----------|--------|
| SQL fragment helpers | `Sql.Joins.Inner(...)` and `Sql.Joins.Left(...)` in `Sql.cs` |
| Full query assembly | `Sql.Queries.*` factory methods in `Sql.cs`, assembled from `Sql.Joins.*` fragments |
| Join abstraction | `IJoinStrategy<TResult>` interface — concrete strategy classes call `Sql.Queries.*` |
| DI registration | Strategy registered as `IJoinStrategy<TResult>`; injected into query repository |
| Projection | Caller specifies SELECT columns in the `Sql.Queries.*` factory method |
| Read model POCOs | Plain classes (no `RecordBase`, no `[Table]`) in `Quotinator.Data/Models/` |
| Strategy classes | In `Quotinator.Data/Queries/` alongside `Sql.cs` |
| Documentation | New `docs/data-access.md` (referenced by #75 and #76) |
| First concrete example | Test-only `Widget`/`Owner` pair — domain joins land in #121 |
| `SqlQueryGuardTests` | `Sql.Queries.*` factory methods added to `AssembledQueryCases` |

---

## What gets built

### 1. `Sql.Joins` — fragment helpers (in `Sql.cs`)

Table names, column names, and aliases are quoted with `[…]` (SQLite bracket quoting). This neutralises injection even if a string literal is accidentally non-constant — a `]` inside a value breaks the identifier rather than escaping into SQL.

```csharp
internal static class Joins
{
    internal static string Inner(string rightTable, string rightAlias,
                                 string leftAlias,  string leftKey, string rightKey)
        => $"INNER JOIN [{rightTable}] [{rightAlias}] ON [{leftAlias}].[{leftKey}] = [{rightAlias}].[{rightKey}]";

    internal static string Left(string rightTable, string rightAlias,
                                string leftAlias,  string leftKey, string rightKey)
        => $"LEFT JOIN [{rightTable}] [{rightAlias}] ON [{leftAlias}].[{leftKey}] = [{rightAlias}].[{rightKey}]";
}
```

> **Rule:** `Sql.Joins.*` parameters must always be compile-time string literals — never user input, never runtime strings. Bracket quoting is a defence-in-depth measure, not a licence to pass dynamic values.

### 2. `Sql.Queries` — full query factory methods (in `Sql.cs`)

```csharp
internal static class Queries
{
    // Example — #121 will add real domain queries here
    internal static string WidgetWithOwner() => $"""
        SELECT w.Id AS WidgetId, w.Label,
               o.Name AS OwnerName
        FROM   Widgets w
        {Joins.Inner("Owners", "o", "w", "OwnerId", "Id")}
        WHERE  w.IsDeleted = 0
        """;
}
```

### 3. `IJoinStrategy<TResult>` — interface (new file in `Quotinator.Data/Queries/`)

```csharp
/// <summary>Provides the SQL for a join query that returns <typeparamref name="TResult"/> read models.</summary>
public interface IJoinStrategy<TResult>
{
    /// <summary>Returns the full parameterised SELECT … FROM … JOIN … SQL string.</summary>
    string BuildSql();
}
```

### 4. Concrete strategy class — example (in `Quotinator.Data/Queries/`)

```csharp
/// <summary>Join strategy for Widget with its Owner — canonical example for the pattern.</summary>
public sealed class WidgetWithOwnerStrategy : IJoinStrategy<WidgetWithOwner>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.Queries.WidgetWithOwner();
}
```

### 5. Read model POCO — example (in `Quotinator.Data/Models/`)

```csharp
/// <summary>Read model returned by the Widget-with-Owner join query.</summary>
public sealed class WidgetWithOwner
{
    /// <summary>Widget primary key.</summary>
    public Guid   WidgetId  { get; init; }
    /// <summary>Widget display label.</summary>
    public string Label     { get; init; } = string.Empty;
    /// <summary>Name of the owning entity.</summary>
    public string OwnerName { get; init; } = string.Empty;
}
```

### 6. Query repository — reusable base (in `Quotinator.Data/Repositories/`)

A lightweight repository that executes any `IJoinStrategy<TResult>`. Domain-specific repositories extend this or use it directly.

```csharp
/// <summary>Executes a join query defined by an <see cref="IJoinStrategy{TResult}"/>.</summary>
public class JoinQueryRepository<TResult>(
    IDbConnectionFactory        factory,
    IJoinStrategy<TResult>      strategy)
{
    /// <summary>Returns all rows matching the strategy's SQL with optional Dapper parameters.</summary>
    public async Task<IReadOnlyList<TResult>> QueryAsync(object? parameters = null)
    {
        using var conn = factory.CreateConnection();
        conn.Open();
        return (await conn.QueryAsync<TResult>(strategy.BuildSql(), parameters)).ToList();
    }
}
```

### 7. `docs/data-access.md` — new doc

Covers:
- When to use `IRepository<T>` vs a join query
- `IJoinStrategy<TResult>` pattern: interface, strategy class, registration, injection
- `Sql.Joins.*` fragment helpers and `Sql.Queries.*` factory methods
- Read model naming and folder rules (no `RecordBase`, no `[Table]`, lives in `Models/`)
- How to add a complex query that needs WHERE parameters
- Cross-reference to #75 (master/detail), #76 (1:1), #77 (many-to-many)

---

## Folder structure after this issue

```
Quotinator.Data/
  Models/
    AuditPageResult.cs        ← existing
    WidgetWithOwner.cs        ← new (example read model)
  Queries/
    Sql.cs                    ← add Sql.Joins + Sql.Queries nested classes
    IJoinStrategy.cs          ← new
    WidgetWithOwnerStrategy.cs ← new (example strategy)
  Repositories/
    JoinQueryRepository.cs    ← new (reusable base)
```

---

## SQL safety

**Identifier quoting:** `Sql.Joins.*` wraps every table name, alias, and column name in `[…]` (SQLite bracket quoting). A `]` inside a value breaks the identifier rather than escaping into SQL, providing defence-in-depth even if a literal is accidentally non-constant. Parameters must still always be compile-time string literals — bracket quoting is not a licence to pass dynamic values.

**`Sql.Queries.*` coverage:** every factory method must be added to `SqlQueryGuardTests.AssembledQueryCases`. The guard drives the method and asserts the output contains no vulnerable patterns (unparameterised string concatenation, comment injections, stacked statements).

**`IJoinStrategy<TResult>` coverage:** `SqlQueryGuardTests` must also discover all concrete `IJoinStrategy<TResult>` implementations in `Quotinator.Data` via reflection, call `BuildSql()` on each, and run the output through the same vulnerability checks. This ensures no strategy class ships with unsafe SQL regardless of how it builds its query.

```csharp
// Sketch of the reflection-driven strategy scan in SqlQueryGuardTests
var strategyTypes = typeof(Sql).Assembly.GetTypes()
    .Where(t => !t.IsAbstract && !t.IsInterface)
    .Where(t => t.GetInterfaces()
        .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJoinStrategy<>)));

foreach (var type in strategyTypes)
{
    var instance = Activator.CreateInstance(type)!;
    var sql = (string)type.GetMethod("BuildSql")!.Invoke(instance, null)!;
    AssertNoVulnerablePatterns(sql, type.Name);
}
```

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `Sql.Joins.Inner` and `Sql.Joins.Left` exist in `Sql.cs` | Integration test | `SqlQueryGuardTests` — existing scan still passes; helpers present |
| 2 | ⬜ | `Sql.Queries.WidgetWithOwner()` covered by `SqlQueryGuardTests` | Integration test | `SqlQueryGuardTests.AssembledQueryCases` — `WidgetWithOwner` case included |
| 3 | ⬜ | `IJoinStrategy<TResult>` defined in `Quotinator.Data.Queries` | Code review | Interface file exists; correct namespace |
| 4 | ⬜ | `WidgetWithOwnerStrategy` implements `IJoinStrategy<WidgetWithOwner>` and delegates to `Sql.Queries` | Code review | Strategy class exists; `BuildSql()` calls `Sql.Queries.WidgetWithOwner()` |
| 5 | ⬜ | `JoinQueryRepository<TResult>` executes the strategy and returns results | Integration test | `JoinQueryRepositoryTests.QueryAsync_ReturnsProjectedReadModels` |
| 6 | ⬜ | Integration test: Widget+Owner join returns correct read model fields | Integration test | `JoinQueryRepositoryTests.QueryAsync_WidgetWithOwner_MapsAllColumns` |
| 7 | ⬜ | Integration test: LEFT JOIN returns null-safe read model when right side is absent | Integration test | `JoinQueryRepositoryTests.QueryAsync_LeftJoin_NullRightSide_ReturnedWithDefaults` |
| 8 | ⬜ | `docs/data-access.md` created and covers all required topics | Code review | Doc exists; sections for IRepository vs join query, IJoinStrategy, Sql.Joins, Sql.Queries, read model rules |
| 9 | ⬜ | `Sql.Joins.*` output uses `[…]` bracket quoting on all identifiers | Unit test | `SqlQueryGuardTests` — assert brackets present in `Inner` and `Left` output |
| 10 | ⬜ | `SqlQueryGuardTests` discovers all `IJoinStrategy<TResult>` implementations via reflection and asserts `BuildSql()` output passes vulnerability checks | Unit test | `SqlQueryGuardTests.AllJoinStrategies_BuildSql_PassesVulnerabilityCheck` |
| 11 | ⬜ | No inline SQL outside `Sql.cs` — `SqlSourceScanTests` passes | Unit test | `SqlSourceScanTests` — all pass |
| 12 | ⬜ | Build clean — 0 warnings, 0 errors | Build | `dotnet build --configuration Release` |
| 13 | ⬜ | All tests pass | Build | `dotnet test --configuration Release` |
| 14 | ⬜ | App starts without error | T1 | User starts app in VS; confirms startup banner |
