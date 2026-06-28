# Data Access Patterns

This document covers the two data access patterns in `Quotinator.Data` and the test helpers in `Quotinator.Data.Testing`.

---

## When to use which pattern

| Pattern | Use when |
|---------|----------|
| `IRepository<T>` / `SqliteRepository<T>` | CRUD on a single, flat table |
| `JoinQueryRepository<TResult>` + `IJoinStrategy<TResult>` | Read-only query that joins two or more tables, or returns a projection (a subset or combination of columns) |

Never reach for a join strategy for single-table reads — `IRepository<T>` is simpler and already tested.

---

## `IJoinStrategy<TResult>` pattern

### 1. Add the SQL to `Sql.Queries`

All SQL lives in `Quotinator.Data/Queries/Sql.cs`. Add a factory method to `Sql.Queries`:

```csharp
internal static class Queries
{
    internal static string WidgetWithOwner() => $"""
        SELECT [w].[Id] AS WidgetId, [w].[Label],
               [o].[Name] AS OwnerName
        FROM   [Widgets] [w]
        {Joins.Inner("Owners", "o", "w", "OwnerId", "Id")}
        WHERE  [w].[IsDeleted] = 0
        """;
}
```

Use `Sql.Joins.Inner(...)` or `Sql.Joins.Left(...)` for JOIN clauses — they bracket-quote all identifiers as defence-in-depth.

**Rule:** parameters to `Sql.Joins.*` must always be compile-time string literals — never user input. Bracket quoting is defence-in-depth, not a licence to pass dynamic values.

### 2. Add the factory method to `SqlQueryGuardTests`

In `tests/Quotinator.Core.Tests/Security/SqlQueryGuardTests.cs`, add a case to `AssembledQueryCases`:

```csharp
yield return ["Queries.YourQuery()", Sql.Queries.YourQuery()];
```

This ensures the CVE-2025-6965 aggregate guard runs over the new query.

### 3. Define the read model POCO

Read model POCOs live in `Quotinator.Data/Models/`. They carry no `RecordBase`, no `[Table]` attribute — Dapper maps them by column alias.

```csharp
public sealed class WidgetWithOwner
{
    public Guid   WidgetId  { get; init; }
    public string Label     { get; init; } = string.Empty;
    public string OwnerName { get; init; } = string.Empty;
}
```

Column aliases in the SELECT must match the POCO property names exactly (Dapper maps case-insensitively).

### 4. Write the strategy class

Strategy classes live in `Quotinator.Data/Queries/`. One class per join shape.

```csharp
public sealed class WidgetWithOwnerStrategy : IJoinStrategy<WidgetWithOwner>
{
    public string BuildSql() => Sql.Queries.WidgetWithOwner();
}
```

All concrete `IJoinStrategy<TResult>` implementations are discovered by the `SqlQueryGuardTests.AllJoinStrategies_BuildSql_PassesAggregateGuard` test via reflection — adding a class automatically adds it to the guard.

### 5. Register and inject

Register the strategy and the repository in DI:

```csharp
services.AddSingleton<IJoinStrategy<WidgetWithOwner>, WidgetWithOwnerStrategy>();
services.AddSingleton<JoinQueryRepository<WidgetWithOwner>>();
```

Inject `JoinQueryRepository<WidgetWithOwner>` into your service via the constructor.

### 6. Execute with optional parameters

```csharp
var results = await _repo.QueryAsync();                         // no parameters
var results = await _repo.QueryAsync(new { lang = "nl" });     // with Dapper parameters
```

---

## `Sql.Joins` fragment helpers

`Sql.Joins.Inner(rightTable, rightAlias, leftAlias, leftKey, rightKey)` and `Sql.Joins.Left(...)` return bracket-quoted JOIN clauses:

```
INNER JOIN [Owners] [o] ON [w].[OwnerId] = [o].[Id]
LEFT JOIN  [Owners] [o] ON [w].[OwnerId] = [o].[Id]
```

Always pass string literals as arguments. These helpers are a convenience for consistent quoting — they are not a sanitisation mechanism for dynamic input.

---

## Adding a WHERE clause with parameters

Extend the `Sql.Queries` factory method to accept a filter clause, and pass parameters when calling `QueryAsync`:

```csharp
internal static string WidgetsByLabel(bool filterLabel) =>
    $"""
    SELECT [w].[Id] AS WidgetId, [w].[Label], [o].[Name] AS OwnerName
    FROM   [Widgets] [w]
    {Joins.Inner("Owners", "o", "w", "OwnerId", "Id")}
    {(filterLabel ? "WHERE [w].[Label] LIKE @label" : "")}
    """;
```

Add cases for all filter-flag combinations to `SqlQueryGuardTests.AssembledQueryCases`.

---

## `Quotinator.Data.Testing`

A companion library for test infrastructure. Add a project reference from test projects only — never from production code.

```xml
<ProjectReference Include="..\..\src\Quotinator.Data.Testing\Quotinator.Data.Testing.csproj" />
```

### `TempDatabase`

Creates a real SQLite database in a temp directory, executes DDL, and deletes everything on `Dispose`.

```csharp
[TestInitialize]
public void Init() => _db = new TempDatabase([CreateTableA, CreateTableB]);

[TestCleanup]
public void Cleanup() => _db.Dispose();
```

`_db.ConnectionFactory` is a fully configured `IDbConnectionFactory` pointing at the temp file. Use it wherever you'd normally inject `IDbConnectionFactory`.

### `NoOp*` stubs

| Type | Implements | Use when |
|------|-----------|----------|
| `NoOpAuditWriter` | `IAuditWriter` | Test does not exercise audit writes |
| `NoOpAuditReader` | `IAuditReader` | Test does not exercise audit reads |
| `NoOpCallerContext` | `ICallerContext` | Test does not need a caller agent |
| `NoOpDatabaseInitializer` | `IDatabaseInitializer` | Endpoint test using a fake service layer |

All stubs expose a static `Instance` singleton. Prefer `Instance` over `new` in tests to make intent clear.

```csharp
_repo = new SqliteRepository<Widget>(
    factory,
    NoOpAuditWriter.Instance,
    NoOpCallerContext.Instance);
```

### `FakeJoinStrategy<TResult>`

Lets you inject an arbitrary SQL string without a real strategy class:

```csharp
var strategy = new FakeJoinStrategy<MyReadModel>("SELECT 1 AS Id FROM Foo");
var repo     = new JoinQueryRepository<MyReadModel>(factory, strategy);
```

Useful for testing LEFT JOIN edge cases or filter variations without creating a new strategy class.

---

## Cross-references

- **#75** — master/detail (parent/child) repository pattern
- **#76** — 1:1 relationship pattern
- **#77** — many-to-many relationship pattern
- **#121** — first real consumer: `SqliteQuoteService` joins (`Quotes → Sources → Characters → People`)
