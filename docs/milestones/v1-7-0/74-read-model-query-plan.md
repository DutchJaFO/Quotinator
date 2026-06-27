# Issue #74 — Add read-model query pattern to Quotinator.Data for join and projection queries

**Milestone:** v1.7.0  
**Status:** Open  

---

## Depends on

- **#73** (audit trail) — complete ✅

---

## Problem

`IRepository<T>` targets one flat table. Queries that join two or more tables, or return a projection (a subset or combination of columns), cannot be expressed through it. The first real consumer is **#121** (`SqliteQuoteService` refactor), which joins `Quotes → Sources → Characters → People`.

Consumers building repositories on top of `Quotinator.Data` also have no test helpers — no-op stubs and temp-database infrastructure are duplicated across three internal test projects and unavailable to external consumers.

---

## Design decisions (all confirmed)

| Decision | Choice |
|----------|--------|
| SQL fragment helpers | `Sql.Joins.Inner(...)` and `Sql.Joins.Left(...)` in `Sql.cs` — identifiers bracket-quoted |
| Full query assembly | `Sql.Queries.*` factory methods in `Sql.cs`, assembled from `Sql.Joins.*` fragments |
| Join abstraction | `IJoinStrategy<TResult>` interface — concrete strategy classes call `Sql.Queries.*` |
| DI registration | Strategy registered as `IJoinStrategy<TResult>`; injected into query repository |
| Projection | Caller specifies SELECT columns in the `Sql.Queries.*` factory method |
| Read model POCOs | Plain classes (no `RecordBase`, no `[Table]`) in `Quotinator.Data/Models/` |
| Strategy classes | In `Quotinator.Data/Queries/` alongside `Sql.cs` |
| Test helpers | New `Quotinator.Data.Testing` project — public API, XML summaries, referenced only from test projects |
| Documentation | New `docs/data-access.md` (referenced by #75 and #76); includes `Quotinator.Data.Testing` usage |
| First concrete example | Test-only `Widget`/`Owner` pair — domain joins land in #121 |
| `SqlQueryGuardTests` | `Sql.Queries.*` methods in `AssembledQueryCases`; reflection scan covers all `IJoinStrategy<TResult>` implementations |

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

### 6. `JoinQueryRepository<TResult>` — reusable base (in `Quotinator.Data/Repositories/`)

Executes any `IJoinStrategy<TResult>`. Domain-specific repositories extend this or use it directly.

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

### 7. `Quotinator.Data.Testing` — new companion project

A dedicated project for test infrastructure. Consumers add it as a test-only project reference. All types are `public` with XML `<summary>` tags.

**Contents:**

#### `NoOps/` — no-op stubs for all `Quotinator.Data` interfaces

Consolidates the three duplicate copies currently spread across `Quotinator.Api.Tests`, `Quotinator.Core.Tests`, and `Quotinator.Data.Tests`. Each stub implements the interface with no-op behaviour suitable for unit tests that do not exercise audit or caller-context logic.

```csharp
/// <summary>No-op <see cref="IAuditWriter"/> for use in unit tests that do not exercise audit behaviour.</summary>
public sealed class NoOpAuditWriter : IAuditWriter
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpAuditWriter Instance = new();
    /// <inheritdoc/>
    public Task WriteAsync(AuditEntry entry, IDbConnection connection, IDbTransaction? transaction = null) => Task.CompletedTask;
    /// <inheritdoc/>
    public Task WriteAsync(AuditEntry entry) => Task.CompletedTask;
    /// <inheritdoc/>
    public Task ClearAsync(string? table = null) => Task.CompletedTask;
}

// Similarly: NoOpAuditReader, NoOpCallerContext, NoOpDatabaseInitializer
```

#### `Fakes/` — configurable test doubles

```csharp
/// <summary>
/// Configurable <see cref="IJoinStrategy{TResult}"/> for unit tests.
/// Constructed with a SQL string; <see cref="BuildSql"/> returns it unchanged.
/// </summary>
public sealed class FakeJoinStrategy<TResult>(string sql) : IJoinStrategy<TResult>
{
    /// <inheritdoc/>
    public string BuildSql() => sql;
}
```

#### `Database/` — temp database helper

Creates a real SQLite database in a temp directory, applies a caller-supplied migration set, and deletes everything on `Dispose`. Replaces the boilerplate currently repeated in every `[TestInitialize]` across the data and core test projects.

```csharp
/// <summary>
/// Disposable temp SQLite database for integration tests.
/// Creates a real database, applies the supplied migrations, and deletes all files on dispose.
/// </summary>
public sealed class TempDatabase : IDisposable
{
    /// <summary>Absolute path to the temp database file.</summary>
    public string DbPath { get; }
    /// <summary>Connection factory pointed at <see cref="DbPath"/>.</summary>
    public IDbConnectionFactory ConnectionFactory { get; }

    /// <summary>
    /// Creates a temp database and applies <paramref name="migrations"/> in order.
    /// </summary>
    public TempDatabase(IReadOnlyList<string> migrations) { … }

    /// <inheritdoc/>
    public void Dispose() { … } // SqliteConnection.ClearAllPools(); Directory.Delete(tempDir, recursive: true)
}
```

**Usage in a test:**

```csharp
[TestInitialize]
public void Init()
{
    _db   = new TempDatabase(QuotinatorMigrations.All);
    _repo = new SqliteRepository<Widget>(_db.ConnectionFactory, NoOpAuditWriter.Instance, NoOpCallerContext.Instance);
}

[TestCleanup]
public void Cleanup() => _db.Dispose();
```

**Existing no-op stub files to remove once consolidated:**
- `tests/Quotinator.Api.Tests/Fakes/NoOpAuditStubs.cs`
- `tests/Quotinator.Core.Tests/Helpers/AuditStubs.cs`
- `tests/Quotinator.Data.Tests/Helpers/AuditStubs.cs`

Each test project adds a project reference to `Quotinator.Data.Testing` instead.

### 8. `docs/data-access.md` — new doc

Covers:
- When to use `IRepository<T>` vs a join query
- `IJoinStrategy<TResult>` pattern: interface, strategy class, `Sql.Queries.*`, registration, injection
- `Sql.Joins.*` fragment helpers and identifier-quoting rule
- Read model naming and folder rules (no `RecordBase`, no `[Table]`, lives in `Models/`)
- How to add a complex query with WHERE parameters
- **`Quotinator.Data.Testing` usage guide** — `TempDatabase`, `NoOp*`, `FakeJoinStrategy<TResult>`
- Cross-reference to #75 (master/detail), #76 (1:1), #77 (many-to-many)

---

## Folder structure after this issue

```
src/
  Quotinator.Data/
    Models/
      AuditPageResult.cs          ← existing
      WidgetWithOwner.cs          ← new (example read model)
    Queries/
      Sql.cs                      ← add Sql.Joins + Sql.Queries nested classes
      IJoinStrategy.cs            ← new
      WidgetWithOwnerStrategy.cs  ← new (example strategy)
    Repositories/
      JoinQueryRepository.cs      ← new (reusable base)
  Quotinator.Data.Testing/        ← new project
    NoOps/
      NoOpAuditWriter.cs
      NoOpAuditReader.cs
      NoOpCallerContext.cs
      NoOpDatabaseInitializer.cs
    Fakes/
      FakeJoinStrategy.cs
    Database/
      TempDatabase.cs
tests/
  Quotinator.Data.Tests/
    Helpers/AuditStubs.cs         ← DELETE (replaced by Quotinator.Data.Testing)
  Quotinator.Core.Tests/
    Helpers/AuditStubs.cs         ← DELETE (replaced by Quotinator.Data.Testing)
  Quotinator.Api.Tests/
    Fakes/NoOpAuditStubs.cs       ← DELETE (replaced by Quotinator.Data.Testing)
docs/
  data-access.md                  ← new
```

---

## SQL safety

**Identifier quoting:** `Sql.Joins.*` wraps every table name, alias, and column name in `[…]` (SQLite bracket quoting). A `]` inside a value breaks the identifier rather than escaping into SQL, providing defence-in-depth even if a literal is accidentally non-constant. Parameters must still always be compile-time string literals.

**`Sql.Queries.*` coverage:** every factory method must be added to `SqlQueryGuardTests.AssembledQueryCases`.

**`IJoinStrategy<TResult>` coverage:** `SqlQueryGuardTests` discovers all concrete implementations in `Quotinator.Data` via reflection, calls `BuildSql()` on each, and runs the output through the same vulnerability checks.

```csharp
// Sketch of the reflection-driven strategy scan
var strategyTypes = typeof(IJoinStrategy<>).Assembly.GetTypes()
    .Where(t => !t.IsAbstract && !t.IsInterface)
    .Where(t => t.GetInterfaces()
        .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJoinStrategy<>)));

foreach (var type in strategyTypes)
{
    var instance = Activator.CreateInstance(type)!;
    var sql      = (string)type.GetMethod("BuildSql")!.Invoke(instance, null)!;
    AssertNoVulnerablePatterns(sql, type.Name);
}
```

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `Sql.Joins.Inner` and `Sql.Joins.Left` exist in `Sql.cs` with bracket-quoted output | Unit test | `SqlQueryGuardTests` — assert `[` present in `Inner` and `Left` output |
| 2 | ⬜ | `Sql.Queries.WidgetWithOwner()` covered by `SqlQueryGuardTests` | Unit test | `SqlQueryGuardTests.AssembledQueryCases` — `WidgetWithOwner` case included |
| 3 | ⬜ | `IJoinStrategy<TResult>` defined in `Quotinator.Data.Queries` | Code review | Interface file exists; correct namespace; public with XML summary |
| 4 | ⬜ | `WidgetWithOwnerStrategy` implements `IJoinStrategy<WidgetWithOwner>` and delegates to `Sql.Queries` | Code review | Strategy class exists; `BuildSql()` calls `Sql.Queries.WidgetWithOwner()` |
| 5 | ⬜ | `SqlQueryGuardTests` reflection scan finds all `IJoinStrategy<TResult>` implementations and passes vulnerability check | Unit test | `SqlQueryGuardTests.AllJoinStrategies_BuildSql_PassesVulnerabilityCheck` |
| 6 | ⬜ | `JoinQueryRepository<TResult>` executes the strategy and returns results | Integration test | `JoinQueryRepositoryTests.QueryAsync_ReturnsProjectedReadModels` |
| 7 | ⬜ | Integration test: Widget+Owner INNER JOIN returns correct read model fields | Integration test | `JoinQueryRepositoryTests.QueryAsync_WidgetWithOwner_MapsAllColumns` |
| 8 | ⬜ | Integration test: LEFT JOIN returns read model with default values when right side is absent | Integration test | `JoinQueryRepositoryTests.QueryAsync_LeftJoin_NullRightSide_ReturnedWithDefaults` |
| 9 | ⬜ | `Quotinator.Data.Testing` project exists; builds clean | Build | `dotnet build --configuration Release` — 0 warnings, 0 errors |
| 10 | ⬜ | `NoOpAuditWriter`, `NoOpAuditReader`, `NoOpCallerContext`, `NoOpDatabaseInitializer` in `Quotinator.Data.Testing`; all public with XML summaries | Code review | Types exist in correct namespace; `Instance` singleton on each no-op |
| 11 | ⬜ | `FakeJoinStrategy<TResult>` in `Quotinator.Data.Testing`; public with XML summary | Code review | Type exists; `BuildSql()` returns the constructor-supplied SQL |
| 12 | ⬜ | `TempDatabase` in `Quotinator.Data.Testing`; creates real DB, applies migrations, disposes cleanly | Integration test | `TempDatabaseTests.Dispose_DeletesTempDirectory` |
| 13 | ⬜ | Duplicate no-op stubs removed from `Api.Tests`, `Core.Tests`, `Data.Tests`; replaced with project reference to `Quotinator.Data.Testing` | Code review | Old stub files absent; each test project references `Quotinator.Data.Testing` |
| 14 | ⬜ | `docs/data-access.md` created; covers all required topics including `Quotinator.Data.Testing` usage | Code review | Doc exists; `TempDatabase`, `NoOp*`, and `FakeJoinStrategy<TResult>` documented with examples |
| 15 | ⬜ | No inline SQL outside `Sql.cs` — `SqlSourceScanTests` passes | Unit test | `SqlSourceScanTests` — all pass |
| 16 | ⬜ | Build clean — 0 warnings, 0 errors | Build | `dotnet build --configuration Release` |
| 17 | ⬜ | All tests pass | Build | `dotnet test --configuration Release` |
| 18 | ⬜ | App starts without error | T1 | User starts app in VS; confirms startup banner |
