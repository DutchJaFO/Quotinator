# Data Access Patterns

This document covers the two data access patterns in `Quotinator.Data` and the test helpers in `Quotinator.Data.Testing`.

---

## When to use which pattern

| Pattern | Use when |
|---------|----------|
| `IRepository<T>` / `SqliteRepository<T>` | CRUD on a single, flat table |
| `AggregateRepository<TParent, TChild>` | Parent table with a child collection written atomically |
| `SqliteOneToOneRepository<TParent, TDetail>` | Parent table paired with exactly one detail row, written atomically and loaded by parent ID |
| `ILinkRepository<TLeft, TRight>` / `SqliteLinkRepository<TLeft, TRight, TJunction>` | Many-to-many relationship: link, unlink, restore individual pairs, and load related collections via a junction table |
| `JoinQueryRepository<TResult>` + `IJoinStrategy<TResult>` | Read-only query that joins two or more tables, or returns a projection (a subset or combination of columns) |

Never reach for a join strategy for single-table reads — `IRepository<T>` is simpler and already tested.

**Adopt `JoinQueryRepository`/`IJoinStrategy` whenever a read joins two or more tables or returns a
multi-table projection expressible as a concrete POCO — even when a hand-rolled `connection.QueryAsync`
call already works and adopting the pattern unlocks no new capability.** Consistency and discoverability
is itself the reason; see [ADR 017](architecture-decisions/017-join-capable-reads-use-joinqueryrepository.md)
for why this is stated explicitly (a real, dated gap where every actual join-needing read in the domain
bypassed this pattern despite it existing first). A domain-specific reader interface may still sit above
this mechanism for batched/shaped results (e.g. a `Dictionary` keyed by id) — the reader shapes the flat
`IReadOnlyList<TResult>` `JoinQueryRepository` returns; it doesn't open its own connection or write its
own SQL inline. The one documented exemption is a read whose correct result type cannot be a concrete
POCO — see ADR 017's `ConversationLineCountReader` example.

---

## Transaction coordination — `TransactionScope`

`TransactionScope` removes the `SqliteUnitOfWork` lifecycle boilerplate from multi-step write operations.

```csharp
await TransactionScope.ExecuteAsync(factory, async uow =>
{
    await repoA.InsertAsync(entityA, uow);
    await repoB.InsertAsync(entityB, uow);
}, existingUow);  // null = create own; non-null = join caller's
```

- **`existing` is `null`** — creates a new `SqliteUnitOfWork`, begins a transaction, commits on success, rolls back on exception.
- **`existing` is non-null** — calls `work(existing)` and returns. The caller's unit of work is not committed here; the caller remains responsible for committing or rolling back.

---

## Bulk insert — `InsertManyAsync` and `InsertStrategy`

`IRepository<T>.InsertManyAsync` inserts a collection in one operation. The strategy controls performance vs. diagnostics:

| Strategy | Behaviour |
|----------|-----------|
| `InsertStrategy.Bulk` (default) | One SQL round-trip for all rows + one bulk audit write. Fastest. |
| `InsertStrategy.Sequential` | Loops through `InsertAsync` per entity. A failure on any row propagates with that row's exception. One audit entry per row. |

Both strategies share the same rollback behaviour: within a shared `IUnitOfWork`, a failure rolls back everything committed so far in that transaction. The difference is whether the exception identifies the specific failing entity.

```csharp
// Fastest — one SQL statement per table
await repo.InsertManyAsync(entities, unitOfWork, InsertStrategy.Bulk);

// Row-by-row — identifies which entity failed
await repo.InsertManyAsync(entities, unitOfWork, InsertStrategy.Sequential);
```

**When to choose Sequential:** the caller needs to report per-row import errors, or business logic must execute between each insert.

---

## Parent/child writes — `AggregateRepository<TParent, TChild>`

Encapsulates the pattern of inserting a parent entity and its child collection atomically. Navigation properties on the parent are write-only — the `GetChildren` method supplies the collection; reads go through `JoinQueryRepository` / `IJoinStrategy`.

```csharp
public class OrderRepository(
    IDbConnectionFactory factory,
    IAuditWriter auditWriter,
    ICallerContext callerContext,
    SqliteRepository<OrderLine> lineRepo)
    : AggregateRepository<Order, OrderLine>(factory, auditWriter, callerContext)
{
    protected override IReadOnlyList<OrderLine> GetChildren(Order parent) => parent.Lines;
    protected override SqliteRepository<OrderLine> ChildRepository => lineRepo;

    // Override only when per-row error identification is needed:
    // protected override InsertStrategy ChildInsertStrategy => InsertStrategy.Sequential;
}
```

`AggregateRepository.InsertAsync` wraps the entire write in `TransactionScope.ExecuteAsync`, so a child failure rolls back the parent insert. If a caller-provided `IUnitOfWork` is passed, the aggregate joins that transaction without committing it.

---

## One-to-one writes and reads — `SqliteOneToOneRepository<TParent, TDetail>`

Extends `AggregateRepository<TParent, TDetail>` (which handles atomic writes) and adds a read side for loading the paired detail record.

### Two layouts

#### Shared primary key

Detail's `Id` equals the parent's `Id`. Use when parent and detail are inseparable.

```sql
CREATE TABLE Widgets      (Id TEXT PRIMARY KEY, ...);
CREATE TABLE WidgetDetails(Id TEXT PRIMARY KEY REFERENCES Widgets(Id), ...);
```

Override `GetDetailAsync` to delegate to `GetDetailBySharedKeyAsync`:

```csharp
public override Task<WidgetDetail?> GetDetailAsync(Guid parentId, IUnitOfWork? uow = null)
    => GetDetailBySharedKeyAsync(parentId, uow);
```

#### Separate foreign key

Detail has its own `Id` and a FK column pointing back to the parent. Use when the detail can have an independent lifetime or may not always be present.

```sql
CREATE TABLE Widgets        (Id TEXT PRIMARY KEY, ...);
CREATE TABLE WidgetDetailsFk(Id TEXT PRIMARY KEY, WidgetId TEXT REFERENCES Widgets(Id), ...);
```

Override `GetDetailAsync` to delegate to `GetDetailByForeignKeyAsync`, passing the FK column name:

```csharp
public override Task<WidgetDetailFk?> GetDetailAsync(Guid parentId, IUnitOfWork? uow = null)
    => GetDetailByForeignKeyAsync("WidgetId", parentId, uow);
```

The FK query uses `RepositorySql.SelectByForeignKey(tableName, fkColumn)` — bracket-quoted, parameterised, and guard-tested.

### Soft-delete strategy

The base class does not prescribe a soft-delete strategy. Document the choice in the concrete repository:

| Strategy | When to use |
|----------|-------------|
| **Cascade** — soft-delete parent also soft-deletes the detail in one `TransactionScope` | Parent and detail are always queried together |
| **Independent** — only the parent is soft-deleted; detail remains active | Detail has a meaningful independent lifetime |

### Full example

```csharp
public sealed class WidgetRepository(
    IDbConnectionFactory factory,
    IAuditWriter auditWriter,
    ICallerContext callerContext,
    SqliteRepository<WidgetDetail> detailRepo)
    : SqliteOneToOneRepository<Widget, WidgetDetail>(factory, auditWriter, callerContext)
{
    protected override SqliteRepository<WidgetDetail> ChildRepository => detailRepo;

    protected override IReadOnlyList<WidgetDetail> GetChildren(Widget parent) =>
    [
        new WidgetDetail { Id = parent.Id, Notes = parent.Notes }  // shared PK: copy Id
    ];

    public override Task<WidgetDetail?> GetDetailAsync(Guid parentId, IUnitOfWork? uow = null)
        => GetDetailBySharedKeyAsync(parentId, uow);
}
```

`InsertAsync(parent)` atomically writes the parent row, the detail row, and an audit entry for each. `GetDetailAsync(parentId)` returns the detail or `null` if none exists.

---

## Many-to-many relationships — `ILinkRepository<TLeft, TRight>`

Use this pattern when two entity types can each relate to many instances of the other through a junction table.

### Interface

```csharp
public interface ILinkRepository<TLeft, TRight>
    where TLeft  : RecordBase
    where TRight : RecordBase
{
    Task LinkAsync        (Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null);
    Task UnlinkAsync      (Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null);
    Task RestoreLinkAsync (Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null);
    Task<IReadOnlyList<TRight>> GetRightAsync(Guid leftId,  IUnitOfWork? unitOfWork = null);
    Task<IReadOnlyList<TLeft>>  GetLeftAsync (Guid rightId, IUnitOfWork? unitOfWork = null);
}
```

The junction entity type (`TJunction`) is an implementation detail of the abstract base class; consumers inject `ILinkRepository<Widget, Tag>` without knowing which table backs it.

### Junction table DDL

Every junction table uses `RecordBase` and a `UNIQUE` constraint on the FK pair:

```sql
CREATE TABLE WidgetTags (
    Id           TEXT    NOT NULL PRIMARY KEY,
    WidgetId     TEXT    NOT NULL REFERENCES Widgets(Id),
    TagId        TEXT    NOT NULL REFERENCES Tags(Id),
    DateCreated  TEXT,
    DateModified TEXT,
    DateDeleted  TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0,
    UNIQUE (WidgetId, TagId)
);
```

### Implementing a concrete link repository

Extend `SqliteLinkRepository<TLeft, TRight, TJunction>` and provide the three abstract members:

```csharp
public sealed class WidgetTagLinkRepository(
    IDbConnectionFactory factory,
    IAuditWriter auditWriter,
    ICallerContext callerContext)
    : SqliteLinkRepository<Widget, Tag, WidgetTag>(factory, auditWriter, callerContext)
{
    protected override string LeftFkColumn  => "WidgetId";
    protected override string RightFkColumn => "TagId";

    protected override WidgetTag CreateJunction(Guid leftId, Guid rightId) => new()
    {
        WidgetId = leftId.ToString("D").ToUpperInvariant(),
        TagId    = rightId.ToString("D").ToUpperInvariant()
    };
}
```

The `LeftFkColumn` and `RightFkColumn` strings are also used via reflection to extract FK values from loaded junction rows — no additional abstract members needed.

### Write semantics

| Operation | Checks current state | Result |
|-----------|---------------------|--------|
| `LinkAsync` | No row → Insert; soft-deleted → Restore; active → no-op | Always idempotent |
| `UnlinkAsync` | Active → SoftDelete; not found / already deleted → no-op | |
| `RestoreLinkAsync` | Soft-deleted → Restore; not found / already active → no-op | |

`INSERT OR IGNORE` is deliberately **not** used — it would bypass `InsertAsync` and produce no audit entry. The check-then-act pattern keeps every state change in the audit trail.

### Cascade deletion

`SqliteLinkRepository` does not cascade soft-deletes to junction rows when a linked entity is removed. Whether junction rows should follow depends on the domain; the concrete repository documents and implements its own cascade strategy.

### Read queries — two round-trips

`GetRightAsync` and `GetLeftAsync` each make exactly two SQL round-trips regardless of N:

1. Load all active junction rows for the given ID (`RepositorySql.SelectByForeignKey`)
2. Load all related entities by primary key list (`RepositorySql.SelectByIds` — Dapper expands `@ids`)

Soft-deleted entity rows are excluded by the `IsDeleted = 0` filter on the entity table (step 2).

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

## Readiness signals — reading a store that is populated asynchronously

**An empty result is not an answer when something is still writing.** If a store is populated by a
background task at startup, a read that arrives during that window sees zero rows for a reason that has
nothing to do with the data. Treating that emptiness as "unavailable" and silently switching to a
fallback answers a question nobody has asked yet — and because the fallback usually works, nothing looks
wrong.

Found live in #309, where it was invisible for the life of the feature: the changelog import and the
consumer that reads it ran as two independent detached tasks, the consumer won the race on **every**
boot, and so the JSON fallback served the startup read every single time while the database-backed path
the work existed to build was never the one that answered. It surfaced only after a log line was added
that stated which source had served each read.

**The rule.** Before interpreting emptiness, establish whether the populate concluded, and distinguish
three states that are easy to collapse into one:

| State | Meaning | Correct response |
|---|---|---|
| Populate still running | Not an answer yet | Wait, then re-query |
| Populate succeeded, zero rows | A real answer — this deployment genuinely has none | Serve the empty result. Not a warning |
| Populate failed | The store cannot be trusted | Fall back, and say so |

That middle row matters for anything in `Quotinator.Data`, which is meant to be reusable (ADR 003/004):
a consuming application may legitimately have no rows at all, permanently. "Empty" and "broken" are
different conditions and must not share a code path or a log level.

**The mechanism** is a small signal the writer completes and readers await — see
`IChangelogImportReadiness`/`ChangelogImportReadiness` (`Quotinator.Data.Import`) for the worked example:

```csharp
if (rows.Count == 0)
{
    ChangelogImportOutcome outcome = await importReadiness.WaitAsync();
    if (outcome is ChangelogImportOutcome.Failed or ChangelogImportOutcome.TimedOut)
        return fallback;                 // and log which of the two it was

    rows = await joinRepository.QueryAsync();   // succeeded — ask again
}
```

Three properties any such signal needs:

- **The writer reports on every exit path**, success *and* failure, or readers wait out their whole
  budget on a signal nobody will ever raise.
- **The wait is bounded**, so a writer that dies without reporting cannot hang a page render. The budget
  is a backstop against that, not a tuned performance value.
- **The budget belongs to the signal, not the caller** — an individual reader has no basis for choosing
  one, and every reader should wait the same amount.

> ⚠️ **Registration is not optional.** A consumer that wires the reader but forgets to register the
> readiness signal as a **singleton** gets behaviour that is correct but badly degraded: every
> empty-store read waits out the full budget before falling back, and a non-singleton registration gives
> each reader its own signal that the writer never completes. Neither fails loudly. Register the signal
> and the reader together, and have the writer's own task set the outcome — as `Program.cs` does for the
> changelog import.

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
- **#309** — readiness signals: why an empty store must not be read as an unavailable one
