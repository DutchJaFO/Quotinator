# #210 — Canonicalize Quotes.Id at capture, case-insensitive lookup

**Status:** Waiting for release — eighth round of scope expansion (see "Closing the RepositorySql SELECT-list boundary" below) shipped, full suite green, T1+T2 both confirmed
**GitHub issue:** #210
**Tiers required:** T1, T2
**Depends on:** none (parent tracking issue #207; shares `EntityIdCanonicalizer` with sibling sub-issue #209, which landed first)

## Closing the RepositorySql SELECT-list boundary (eighth round of scope expansion)

The seventh round below documented `RepositorySql.cs`'s generic `SELECT *` queries as a "structural
boundary" — entity-agnostic by design (ADR 004), with (it was claimed) no explicit column list available
for a text-based guard to rewrap. The developer reviewed `RepositorySql.SelectByIds`/`SelectPage` and
`SqliteRepository.GetPageAsync` directly and rejected that framing: *"you claim that you can do nothing
for RepositorySql, but when I trace the calls I notice that we are aware of the available columns when
calling SelectPage. If we add an interface that allows us access to the ValidColumnNames property of a
class then we can do validate both the order by part of the query and the column typing we require for
Id-columns. Id-columns could be part of that interface, so classes could indicated foreign keys that were
different from the standard."*

This was correct: `SqliteRepositoryBase<T>.ValidColumnNames` (reflection-derived, already used to validate
`GetPageAsync`'s caller-supplied `ORDER BY` column) already proved the necessary column knowledge existed
one layer above `RepositorySql`'s static factory methods — the "no column-list knowledge available" premise
behind the seventh round's "structural boundary" framing was simply wrong.

**Fix**: a new `IEntityColumnMetadata` interface
(`src/Quotinator.Data/Repositories/EntityColumnMetadata.cs`) exposing `ValidColumnNames` (every persisted
column) and `IdColumnNames` (the id-column subset). `ReflectedColumnMetadata` is the default, per-`Type`-
cached implementation — same Dapper.Contrib `[Write(false)]`/`[Computed]` exclusion criteria
`SqliteRepositoryBase<T>` already used for `ValidColumnNames`, with `IdColumnNames` inferred as every
persisted property ending in `Id` (matching `SqlSelectPresentationGuard`'s own naming-convention
inference). It is an interface rather than a bare class specifically so a future entity whose FK doesn't
follow the `*Id` convention could supply its own implementation instead of relying on reflection inference
— directly answering the developer's "classes could indicate foreign keys that were different from the
standard"; no such entity exists today, but the indirection means that exception, if ever needed, wouldn't
require changing `RepositorySql` itself.

`RepositorySql.BuildSelectColumns(IEntityColumnMetadata columns)` replaces every `SELECT *` with an
explicit column list, wrapping each id column via `IdClauses.SelectColumn`. Six factory methods
(`SelectById`, `SelectDeleted`, `SelectByForeignKey`, `SelectJunctionRow`, `SelectByIds`, `SelectPage`) now
take an `IEntityColumnMetadata columns` parameter. `SqliteRepositoryBase<T>`'s `ValidColumnNames` field was
replaced with `protected static readonly IEntityColumnMetadata Columns = ReflectedColumnMetadata.For(typeof(T))`,
threaded through `SqliteRepository<T>`, `SqliteRestorableRepository<T>`, `SqliteOneToOneRepository<TParent,
TDetail>` (resolves `TDetail`'s own metadata separately, mirroring how it already independently resolves
`DetailTableName`), and `SqliteLinkRepository<TLeft,TRight,TJunction>` (resolves `TJunction`'s metadata for
the class-level junction queries, and `TEntity`'s metadata per-call in the generic `QueryByIdsAsync<TEntity>`,
since `TEntity` is a method type parameter, not tied to the class).

`RepositorySqlGuardTests`/`RepositorySqlTests` needed a local `FakeColumnMetadata` test double (a synthetic
`"TestWidgets"` table has no real `[Table]`-attributed entity behind it for `ReflectedColumnMetadata` to
reflect over). `RepositorySqlFactory_PassesSelectPresentationGuard`/`RepositorySqlFactory_PassesIdCaseGuard`
previously passed vacuously against empty `SELECT *` — they now scan genuine explicit column lists (e.g.
`SELECT LOWER(Id) AS Id, Label, DateCreated, LOWER(ParentId) AS ParentId, ... FROM TestWidgets WHERE
LOWER(Id) = LOWER(@id) AND IsDeleted = 0`), closing the exact "the guard never actually had anything to
scan here" gap the seventh round's boundary left open.

One compile fix needed along the way: `IEntityColumnMetadata` had to be `public`, not `internal` — a
`protected` field on the `public` `SqliteRepositoryBase<T>` is inherited by `Quotinator.Data.Example`
(a different assembly), so an `internal` field type would have been CS0052 inconsistent accessibility.
`ReflectedColumnMetadata` itself stays `internal` — only `Quotinator.Data` calls `.For()` directly.

Full suite green with zero regressions (2, 16, 598, 30, 11, 16, 967, 9, 493 — matching pre-change counts
exactly), confirming the SELECT-list change is functionally transparent to existing behaviour while closing
a real, previously-undetected robustness gap. T2 re-verified live: every generic-repository-backed
masterdata endpoint (sources, characters, people, series, universes, conversations, stage directions,
sound cues) still returns 200 with correct, lowercase-rendered data, and case-insensitive `GetByIdAsync`
lookup still works. ADR 012 and CLAUDE.md updated to remove the now-resolved "structural boundary" claim
and describe the actual mechanism in its place.

## Uniform SELECT-list wrapping, and squashing ADR 012's revision history (seventh round of scope expansion)

The sixth round's registry-based `SqlSelectPresentationGuard` fixed the reported `SystemChangeLog.EntityId`
gap, but the developer reviewed the code directly (a screenshot of `SqliteQuoteService.SelectBase`) and
found the same class of gap still open on `q.Id`/`ser.Id`/`uni.Id` — columns the registry never covered
because they were assumed `Guid`-typed and therefore "safe." The developer's question: *"why are you not
employing the same technique for columns in select statements"* as the JOIN guard already uses — i.e.
wrap unconditionally, don't reason about which columns need it.

**Root cause of the inconsistency**: the SELECT-list guard was built around a registry of columns
*believed* to be `string`-typed on their C# side, on the theory that a `Guid`-typed column already renders
lowercase for free via `System.Text.Json`'s default formatting. That is the exact "safe because of how
it's used today" reasoning `IdClauses.Join` had already rejected for JOIN conditions — and it is fragile
in the same way: `Quotinator.Core.Models.MasterDataReference.Id` is `string`-typed today specifically
because a `Guid` wasn't enough for a not-yet-existing row's id, proving a column's downstream type can
change without the query being touched.

**Fix**: `IdClauses.SelectColumn(column, alias)` — `LOWER(column) AS alias` — added as the uniform
SELECT-list wrap, used for every `*Id`-suffixed column found anywhere in `Quotinator.Data`'s and
`Quotinator.Core`'s `Sql.cs` files (`SqliteQuoteService.SelectBase`/`SelectRawById`, `Characters`,
`CharacterSources`, `People`, `Sources`, `Series`, `Universe`, `Conversations`, `ConversationLines`,
`StageDirections`, `SoundCues`, plus `Quotinator.Data`'s `SystemAudit`/`SystemImportActions`/
`SystemChangeLog`/`Queries.WidgetWithOwner`). `SqlSelectPresentationGuard` was rewritten from a
registry-based scan to a fully generic strip-then-scan (mirroring `SqlIdCaseGuard`'s own technique
exactly: strip every already-`LOWER(...)`-wrapped column first, then flag any remaining `*Id`-suffixed
reference) — no registry, no per-column classification, one exemption (`InitiatedById`, by name, since
it is polymorphic and not always an id). `EntityIdPresentationClassificationTests` (the sixth round's
comprehensiveness check) was deleted — a fully generic guard needs no registry to keep in sync, so the
whole class of drift that test existed to catch no longer exists.

**Regex debugging found live**: a lookbehind-based first attempt at the generic guard couldn't reliably
skip an arbitrary bracket-quoted table alias inside `LOWER(...)` — `LOWER([w].[Id])` still let a bare
`Id` match slip through. Rewritten to the same strip-then-scan technique `SqlIdCaseGuard.FindViolations`
already uses (remove protected occurrences via `.Replace()` before scanning) rather than encoding
"already protected" as a lookbehind assertion.

**Structural boundary documented, not silently left**: `RepositorySql.cs`'s generic queries (`SelectById`,
`SelectByIds`, `SelectPage`, etc.) all use `SELECT *` — entity-agnostic by design (ADR 004), with no
explicit column list for a text-based guard to rewrap. Correctness there depends on every domain entity's
id/FK properties staying `Guid`-typed, which is true today (confirmed: no `Quotinator.Core.Entities` type
has a `string`-typed `Id`-suffixed property) but is now an explicit, documented constraint rather than an
unstated assumption.

**This "structural boundary" framing was itself rejected one review cycle later — see the eighth round
above.** The premise (no column-list knowledge available at the generic-repository layer) turned out to be
false: `ValidColumnNames` already existed one layer up and proved the knowledge was there all along.

Full suite green (no pre-existing test needed changes — nothing asserted exact casing for any of the
newly-wrapped columns). T2 re-verified live: masterdata/quotes/characters endpoints all still render
lowercase ids after the sweep, no regression.

**ADR 012 rewritten into clean current-state form.** Separately, the developer made a standing-policy
point: *"the git history of an ADR carries its changes. Adding history to the file itself makes them hard
to read, especially for an ADR that has not been pushed on the main branch."* ADR 012 had accumulated
four "Revision —" sections plus a "Follow-up" subsection across this issue's prior rounds — replaced with
a single clean document stating the current, settled design (canonical form, the three mechanisms, the
one exemption, the `RepositorySql` boundary) with no revision-log structure. This plan doc and
`overview.md` keep their own round-by-round narrative — that convention is unaffected; the rewrite
applies to the ADR file specifically, per the developer's own scoping of the request.

## A missed column, stale comments, and a mechanical guard (sixth round of scope expansion)

Immediately after the fifth round below shipped, the developer reviewed the code directly (not just the
live API) and found two further gaps in the same fix, then made the pointed observation that neither the
guard nor the test suite had caught either one: *"guard checks should have caught those. The fact that
these weren't caught tells me that your unit tests and guards are not good enough."*

**Missed column**: `Sql.SystemChangeLog.SelectByEntity` selected `EntityId` — a `string`-typed
id-reference column identical in shape to the three already fixed on `SystemImportAction` — completely
unwrapped. Missed because `ISystemChangeLogReader.GetHistoryAsync` has no HTTP endpoint yet; an earlier
pass had incorrectly concluded there was "no reader/SELECT query yet" for `SystemChangeLog`'s string id
fields, when the reader and its query both already existed, DI-registered, simply unexposed over HTTP.
Fixed the same way as the others: `LOWER(EntityId) AS EntityId`.  `SystemChangeLog.InitiatedById` — also
`string`/`Id`-suffixed — is deliberately **not** wrapped: it is polymorphic (an import batch UUID, an HTTP
route, or an enrichment provider name), so forcing it lowercase would corrupt legitimate mixed-case
content in the non-id cases. This is an explicit, documented exemption, not a second miss.

**Stale comments**: a repository-wide sweep (`grep -rniE "upper-case|uppercase" src/`) found roughly
twenty present-tense comments across both `Sql.cs` files, `SqliteImportActionService.cs`,
`RepositorySql.cs`, `GuidHandler.cs`, `EntityIdCanonicalizer.cs`, `QuotinatorMigrations.cs`, and
`SystemAuditEntry.cs` still asserting the *previous* uppercase-canonical convention as current fact — left
behind by the fourth and fifth rounds, neither of which included a full-repository sweep. All corrected;
one migration comment (describing `CharacterSources.Id`'s frozen, `hex()`-generated uppercase format)
was rewritten to explain the resulting permanent, accepted mismatch rather than asserting it no longer
exists, since the migration SQL itself can never change.

**Mechanical gap closed (first attempt)**: `SqlIdCaseGuard` only ever scanned `WHERE`/`JOIN` comparisons
— a `SELECT` column list was structurally outside its patterns, so it could never have caught
`SystemChangeLog.EntityId`. First closed with `Quotinator.Data.Diagnostics.SqlSelectPresentationGuard`
plus a hand-maintained registry of "columns known to be `string`-typed and therefore needing the wrap."
A new regression test, `SystemChangeLogWriterReaderTests.GetHistoryAsync_MixedCaseEntityId_ReturnsLowercase`,
proved the fix. Full suite green (2140 tests), T2 re-verified live.

**This registry-based approach was itself rejected one review cycle later — see the seventh round below.**

## Read-time presentation normalization for string-typed id-reference fields (fifth round of scope expansion)

After the fourth round below shipped (lowercase system-wide, full suite green, T1/T2 verified), the
developer reported — from live Postman output — that `/api/v1/import/actions` and `/api/v1/admin/audit`
responses still showed uppercase `batchId`/`entityId`/`recordId` for pre-existing data (written two days
before this session's fixes existed), sitting next to already-lowercase `id` fields in the same response.
The developer's own framing: *"we should have standardised to lowercase versions regardless of: (a) what
is in the database (b) what is in the imported file (c) what is in the request itself."*

Root cause: every mechanism shipped so far operates at capture time (new writes are canonical) or
comparison time (`IdClauses`' `LOWER()` wrapping matches regardless of stored casing) — neither touches
**read time** for a column that isn't being filtered or joined on. A `Guid`-typed property (`Id`) gets
canonical lowercase rendering for free from `System.Text.Json`'s own default `Guid` formatting; a
`string`-typed property (`BatchId`/`EntityId`/`ExistingBatchId`/`RecordId` — `string` for the same reason
`SourceEntry.Id` is: a not-yet-existing row's id must be representable before any `Guid` can be typed as
one) has no such safety net and renders exactly the casing already on disk.

**Fix**: `LOWER(...) AS ColumnName` wrapping added directly in the shared `SELECT` column lists these
fields flow through — `Sql.SystemImportActions.SelectColumns` (covers `SelectPaged`/`SelectById`/
`SelectAllForBatch` in one edit) and `Sql.SystemAudit.SelectPaged`. `ImportActionSummaryResponse
.ExistingBatchId` and `ImportResultResponse.QuoteId` inherit the fix automatically since both populate
directly from the now-fixed reads. See ADR 012's "Read-time presentation normalization" section for full
reasoning, including why this is a genuinely distinct third mechanism and not a restatement of the
existing rule. (This registry-based approach and its ADR narrative were both superseded by the seventh
round above — ADR 012 no longer carries this as a separate "Revision" section, having been rewritten
into a single current-state document.)

One test updated: `ExistingBatchId_RoundTripsCorrectly`
(`tests/Quotinator.Data.Tests/Repositories/SystemImportActionWriterReaderTests.cs`) previously asserted a
mixed-case fixture (`"BATCH-1"`/`"BATCH-2"`) round-tripped byte-for-byte; now asserts it reads back
lowercase (`"batch-1"`/`"batch-2"`) — this is itself the correct proof of the read-side fix, using a
fixture written directly (bypassing capture-time canonicalization) to simulate genuinely pre-existing
data. Full suite green (1871/1871, same per-project counts as the fourth round). T2 re-verified live: a
fresh `POST /import` under `review` policy followed by `GET /import/actions?status=pending` and `GET
/admin/audit` confirmed every `batchId`/`entityId`/`existingBatchId`/`recordId` renders lowercase.

**Gap found and closed during the #210 issue-body review (2026-07-22)**: `Sql.SystemAudit.SelectPaged`'s
equivalent `recordId` fix had no dedicated regression test of its own — only the live T2 pass above
proved it. Added `SystemAuditReaderTests.GetPagedAsync_MixedCaseRecordId_ReturnsLowercase`, mirroring
`ExistingBatchId_RoundTripsCorrectly`'s technique (write a deliberately uppercase `RecordId` directly,
read it back via `GetPagedAsync`, assert lowercase). Full suite green (Data.Tests 598 → 599; every other
project unchanged).

## System-wide lowercase convention (fourth round of scope expansion)

After the third round of scope expansion below shipped (Quotes.Id unified to uppercase, matching every
other entity), the developer reviewed the shipped result again and drew a distinction the third round's
own reasoning had conflated: `UPPER(...)` in a SQL comparison exists purely so the comparison succeeds
regardless of casing — it says nothing about which casing is canonical for storage or presentation. Those
are separate decisions. The developer's response reopened the format choice itself: **the canonical form
for every entity id, system-wide, is now lowercase** — `Guid.ToString("D")`'s own default, matching the
conventional RFC 4122 UUID string representation. See ADR 012's "Canonical form" section for the full
current-state reasoning; the incident narrative below records two infrastructure-level bugs found live while
making the switch that were not casing bugs themselves but were unmasked by it: `GuidHandler` was never
actually the "single global choke point" its own doc comments claimed (Dapper's built-in `typeMap`
resolves a bare `Guid` parameter's `DbType` before ever consulting a registered `ITypeHandler`, silently
skipping `GuidHandler.SetValue` for outbound parameters this whole project's history), and Dapper's
list-parameter expansion does not reliably invoke a registered handler per element the way scalar binding
does (found via `ConversationLineCountReaderTests`/`CharacterSourceLinkReader`, both of which had silently
matched zero rows).

This section documents the change here rather than rewriting the (now further-superseded) Steps/Notes
sections below, matching this project's convention of appending new decisions rather than editing a plan
doc's historical narrative in place.

**What changed, in one sentence**: `GuidHandler`, `EntityIdentity.StableId`, `EntityIdCanonicalizer`, and
`ImportActionPlanner`'s capture points all flipped back to lowercase; `Quotinator.Data.Helpers.GuidExtensions.ToCanonicalId`
was introduced as the single real choke point, replacing ~35 separately-typed-out `.ToString("D").ToUpperInvariant()`
call sites across `Quotinator.Api`/`Quotinator.Core`/`Quotinator.Data`; `DatabaseConfiguration.Configure()`
now calls `SqlMapper.RemoveTypeMap(typeof(Guid))` before registering `GuidHandler` so a bare `Guid`-typed
Dapper parameter is no longer a silent landmine; `IdClauses` wraps in `LOWER(...)` instead of `UPPER(...)`,
chosen specifically so a value from `ToCanonicalId()` binds directly into an `IN`-list with no further
transformation; `SqlIdCaseGuard`'s six regexes recognize `LOWER(...)` as the protected form. Full suite
green (790/504/493/16/30/2/16/9/11 — every test project, 1871 tests total). Going-forward only — no
migration, no re-casing of already-stored data.

## Quotes.Id casing unification (third round of scope expansion)

After the second round of scope expansion below shipped (`IdClauses`, T1/T2 both green, status
"Waiting for release"), the developer reviewed the live API and asked why quote ids render lowercase
in responses while every other entity's id renders uppercase. The original #210 work deliberately kept
Quote as the one lowercase-canonical entity, reasoning that `QuoteIdentity.StableId`'s lowercase output
was pinned by a production-data regression test and changing it risked breaking already-deployed
databases. The developer's response — "we need to be consistent" — reopened that decision. (Quote's
casing was flipped again, to lowercase, one round later — see "System-wide lowercase convention" above.
Recorded here as the historical narrative of how that decision was reached; ADR 012 itself carries only
the final, current-state design, not this round-by-round account — why the original safety concern was
moot given #210's own case-insensitive-read infrastructure, what changed, and the three real production
bugs found and fixed along the way — a `Guid.ToString()` round-trip in `QuoteSeedWriter`, a plain C#
string-equality comparison in `QuotinatorDatabaseInitializer`, and `ConversationLines.QuoteId`'s FOREIGN
KEY-safety gap — none of which `SqlIdCaseGuard`/`IdClauses` could have caught, since none were
SQL-comparison defects.)

This section documents the change here rather than rewriting the (now superseded) Steps/Notes sections
below, matching this project's convention of appending new decisions rather than editing a plan doc's
historical narrative in place.

**What changed, in one sentence**: `ImportActionPlanner.PlanAsync`'s quote-loop capture point now calls
`EntityIdCanonicalizer.TryCanonicalizeUppercase` instead of `TryCanonicalizeLowercase`; the now-unused
`CanonicalizeLowercase`/`TryCanonicalizeLowercase` methods were deleted; every doc comment and test
asserting the old lowercase convention was corrected. Full suite green (790/504/493 in Core/Data/Api
Tests). Going-forward only — no migration, no re-casing of already-stored data.

## Scope expansion

While implementing this issue, the developer asked to also audit whether audit-log and other
non-masterdata endpoints had the same id-casing gap. That audit found several more case-sensitive
id comparisons beyond Quotes.Id, and the developer's explicit direction was: fix every one found,
and build a permanent, automated guard so this class of regression can never reappear — not a
one-time manual pass. This substantially widened the issue's scope beyond its original spec
requirements 1-4 below. The added work:

- A new systemic guard, `SqlIdCaseGuard` (`src/Quotinator.Data/Diagnostics/SqlIdCaseGuard.cs`),
  structurally mirroring the existing CVE-2025-6965 `SqlAggregateGuard` — a regex-based static
  analyzer that flags any comparison between an id-named column and a bound parameter that isn't
  wrapped `UPPER(...) = UPPER(...)` on both sides (or, for an `IN`/`NOT IN` clause, at least the
  column side). It distinguishes bare/prefixed/aliased/bracket-quoted columns and half-protected
  wraps (only one side wrapped — still flagged) from fully-protected ones, and `UPDATE ... SET`
  assignments (write-side, out of scope) from `WHERE` comparisons (read-side, in scope).
- Wired into three existing guard-test files via their existing `DynamicData` enumeration methods
  (no duplicated enumeration logic, matching how `SqlAggregateGuard` itself is already wired):
  `tests/Quotinator.Core.Tests/Security/SqlQueryGuardTests.cs`,
  `tests/Quotinator.Data.Tests/Security/SqlQueryGuardTests.cs`,
  `tests/Quotinator.Data.Tests/Repositories/RepositorySqlGuardTests.cs`.
- Every violation the guard found was fixed with `UPPER()` wrapping: 34 in
  `src/Quotinator.Core/Queries/Sql.cs` (Quotes, QuoteGenres, QuoteTranslations, SourceTranslations,
  Characters, CharacterSources, Sources, Series, Universe, Conversations, ConversationLines,
  StageDirections, StageDirectionTranslations, SoundCues, SoundCueTranslations), 5 in
  `src/Quotinator.Data/Queries/Sql.cs` (ImportBatches.UpdateRecordCount, SystemAudit.BuildWhere —
  the one genuinely live bug, `GET /admin/audit?recordId=` — SystemImportActions.MarkDecided/
  ClearDecision/MarkApplied, SystemChangeLog.SelectByEntity), 7 in
  `src/Quotinator.Data/Repositories/RepositorySql.cs` (the generic repository layer used by every
  entity, including Quote), plus `SqliteQuoteService.BuildFilterWhere`'s dynamic `seriesId`/
  `universeId` filter clauses (a genuinely new finding neither the manual audit nor the background
  research agent had caught — found only because the automated guard scanned the real assembled
  query, not a manually-curated inventory).
- Most of these fixes are defense-in-depth, not fixes to an actively-reachable bug — e.g.
  `SourceSeriesReferenceReader`/`SeriesUniverseReferenceReader`/`CharacterSourceLinkReader` already
  canonicalize their C#-side parameters correctly today (manually, or via `GuidHandler`'s automatic
  forcing on `Guid`-typed parameters). The point, per the developer's explicit instruction, is to
  never rely on "this happens to be safe today because the known caller already does the right
  thing" — the SQL itself must be safe regardless of what any future caller does.
- **Second round of scope expansion**: the developer then suggested going further — instead of only
  catching a wrong comparison after the fact, build helper methods that construct the correct
  comparison in the first place. Added `Quotinator.Data.Queries.IdClauses`
  (`Equals`/`In`/`NotIn`/`Join`), and rewrote every fixed query and factory method across both
  `Sql.cs` files and `RepositorySql.cs` to call it instead of hand-typing `UPPER(...)`. This required:
  - Converting every affected `const string` query to `static readonly string` (a method call isn't
    a compile-time constant), which surfaced a second, independent bug: the guard-test reflection
    (`EnumerateSqlConstants` in both `SqlQueryGuardTests.cs` files) only ever called `GetFields`, so
    it silently covered `const`/`static readonly` fields but not `static string` properties. Widened
    to also call `GetProperties`, which immediately found a real, previously-invisible bug:
    `Sql.SystemImportActions.SelectById` (`Quotinator.Data.Queries.Sql`) was declared as a property
    with an unwrapped `WHERE Id = @id`, used live by `SystemImportActionReader.GetByIdAsync`. Fixed,
    with a dedicated regression test.
  - The developer decided (asked explicitly, given ADR 012 originally argued JOINs don't need
    wrapping) that `IdClauses.Join` should wrap both sides too, reversing that stance — defense in
    depth outweighs the (negligible, at this project's scale) cost. Every existing JOIN condition and
    correlated-subquery predicate between two id columns was rewritten to call it, and
    `SqlIdCaseGuard` itself was extended with `JoinComparisonPattern`/`ProtectedJoinPattern` so an
    unwrapped join is now a guard-test failure too, not just a JOIN-to-parameter comparison.
  - Found and fixed one more live gap while touching `SqliteQuoteService`: `q.Id NOT IN
    @excludedIds` (the `/random` dedup exclusion clause) had no `UPPER()` wrapping at all, and — a
    third guard blind spot — the original `IdComparisonPattern` regex only recognised `=`/`IN`, not
    `NOT IN`, so this was invisible to the guard too. `IdClauses.NotIn` was added and the regex fixed.
  - The developer separately asked (2026-07-20, after seeing this pass surface the `EntityType`/
    `Status` string comparisons in `SystemImportActions.BuildWhere`) whether *non-id* string
    comparisons might have the same class of gap. That is out of scope for this issue by design —
    filed separately as
    [#211](https://github.com/DutchJaFO/Quotinator/issues/211) (research issue, `data-import-sources`
    milestone) rather than folded in here, since it is a distinct question (which columns, not just
    ids, need this treatment) that needs its own investigation before any implementation is planned.

See ADR 012 for the resulting policy statement — both the read-side guard and the `IdClauses`
construction helper are documented there, along with the reversed JOIN-wrapping stance.

---

## Spec requirements

1. Add the lowercase throwing/non-throwing forms (`CanonicalizeLowercase`/`TryCanonicalizeLowercase`) to
   `Quotinator.Data.Helpers.EntityIdCanonicalizer` — sibling sub-issue #209 adds the uppercase forms to
   the same class; whichever lands first creates the file with its own half.
2. Canonicalize a file-authored `SourceQuote.Id` to lowercase (matching `QuoteIdentity.StableId`'s own
   pinned, must-never-change lowercase convention) at the single earliest capture point in
   `ImportActionPlanner.PlanAsync`'s quote loop.
3. Audit and `UPPER()`-wrap every `Quotes.Id`/`QuoteId`-matching query that compares against a
   caller-or-file-supplied value, matching the same case-insensitive-by-default policy this project
   already applies to every other id column (CLAUDE.md's "GUID/enum/id comparisons are case-insensitive
   by default"). `Sql.Quotes.SelectById()` — the query behind `GET /api/v1/quotes/{id}` — currently has
   **no** case-insensitive wrapping at all, unlike Source/People's already-partially-mitigated
   equivalents; this is a live-user-facing gap, not just an internal-consistency one.
4. Audit every place a `Quotes.Id`-derived value is bound as a `Guid`-typed Dapper parameter (which
   `GuidHandler` force-uppercases) versus a plain `string` parameter (which doesn't) — a mismatch
   between the two for the *same* logical id is its own, independent source of inconsistency, separate
   from whatever casing the original file used.

---

## Background — why this issue exists

Found while planning sibling sub-issue #209 (Source/Person/StageDirection/SoundCue/Conversation
canonicalization — see #207, the parent tracking issue): a file-authored `Quotes.Id` has the identical
capture-time gap as Source/Person, confirmed by tracing the actual code, not assumed:

- `ImportActionPlanner.PlanAsync`'s quote loop uses `q.Id` directly and unconditionally
  (`seenQuotes[q.Id] = q;` line 87, `EntityId = q.Id` at lines 102/144/164) — never canonicalized.
- `SqliteQuoteService.GetById(string id, ...)` (line 50) binds `id` as a **plain `string`** parameter
  into `Sql.Quotes.SelectById()`, whose WHERE clause is `q.Id = @id` with **no `UPPER()` wrapping at
  all** (`Sql.cs` line 92-93) — confirmed by direct read, not inferred from the Source/People pattern.
  This means `GET /api/v1/quotes/{id}` today only succeeds if the URL's casing happens to exactly
  match however that specific quote's id was originally typed in its source file — worse than
  Source/People's situation, which at least has partial `UPPER()` read-side tolerance.
- `QuoteIdentity.StableId` (the auto-generated fallback, used only when a quote entry omits its own
  `id`) is deliberately, permanently lowercase — pinned by "a production-data regression test" per
  `EntityIdentity.cs`'s own comment. But that pin only governs the *auto-generated* path; a
  file-authored `id` (the overwhelmingly common case — nearly every quote entry in this project's own
  bundled/curated data supplies one) is never checked or conformed against it. So `Quotes.Id` is not
  actually the consistent lowercase convention the `QuoteIdentity.StableId` comment implies — it is, in
  practice, "whatever casing each import happened to use," exactly Source/Person's bug, just targeting
  lowercase instead of uppercase, and with a weaker read-side mitigation (none) than Source/People have.
- A further, independent inconsistency was found alongside this (not fully resolved during planning,
  flagged for the implementation step to investigate and enumerate all instances of, not designed to a
  specific fix here): some call sites bind a quote id as a `Guid`-typed parameter (e.g.
  `QuoteSeedWriter.InsertGenresAsync(connection, resolved, Guid.Parse(resolved.Id), ...)` —
  `GuidHandler` force-uppercases this), while others (`Sql.Quotes.Insert`'s own `Id = resolved.Id`)
  bind the same logical value as a plain `string` (no forcing). Two call sites touching the *same*
  quote's id can therefore each apply a *different* casing transform to it, independent of whatever the
  source file originally said.

---

## Approach

### `EntityIdCanonicalizer` — lowercase forms

`Guid.ToString("D")` is already lowercase by .NET's own default, so the lowercase form needs no explicit
transform, mirroring exactly how `QuoteIdentity.StableId` itself produces its output today:

```csharp
public static class EntityIdCanonicalizer
{
    /// <exception cref="FormatException">rawId is not a valid Guid.</exception>
    public static string CanonicalizeLowercase(string rawId) => Guid.Parse(rawId).ToString("D");

    public static bool TryCanonicalizeLowercase(string rawId, out string? canonical)
    {
        if (Guid.TryParse(rawId, out var parsed)) { canonical = parsed.ToString("D"); return true; }
        canonical = null;
        return false;
    }
}
```

If sub-issue #209 has not landed yet, this sub-issue creates the file with only these two methods; #209
then adds its uppercase forms alongside them (or vice versa, whichever lands first).

### `PlanAsync`'s quote loop — capture-point fix (lowercase target)

Same shape as Source/Person's fix in #209, targeting `CanonicalizeLowercase` instead:

```csharp
foreach (var q in quotes)
{
    var canonicalQuoteId = EntityIdCanonicalizer.TryCanonicalizeLowercase(q.Id, out var canonical) ? canonical! : q.Id;
    // every later reference to q.Id in this iteration (seenQuotes[q.Id], EntityId = q.Id, resolved.Id,
    // and the SourceQuote instances threaded through QuoteFieldMerge) uses canonicalQuoteId instead.
```

`SourceQuote` is a `record`/init-only type — the exact mechanics of substituting `canonicalQuoteId` back
onto `q` (a `with` expression producing a corrected copy vs. threading the canonical string alongside `q`
through the rest of the loop) are an implementation-time detail, not a design question; whichever reads
more clearly once the surrounding loop body is in front of the implementer.

### Query audit — every `Quotes.Id`/`QuoteId`-matching site

Full inventory from a direct grep of `Sql.cs` (not exhaustive design of each fix here — the exact wrap
is mechanical once the inventory is confirmed correct; recorded so the implementation step has a checked
starting list rather than re-deriving it):

| Location | Current | Needs `UPPER()`? |
|---|---|---|
| `Quotes.SelectById()` (~line 92) | `WHERE q.Id = @id` | **Yes** — the live, user-facing `GET /quotes/{id}` gap |
| `Quotes.SelectRawById()` (~line 118) | `WHERE q.Id = @id` | Yes — internal merge/conflict-resolution use, same defense-in-depth precedent as Sources/People's internally-used-but-still-wrapped siblings |
| `Quotes.SelectCompletenessById`/`UpdateCompletenessById` (lines 33, 37) | `WHERE Id = @id` | Yes, matching Sources/People's already-wrapped siblings |
| `Quotes.UpdateOnNewestWins` (lines 43-45) | `WHERE Id=@id` | Yes |
| `QuoteGenres.DeleteForQuote`/`LoadForQuote`/the two Insert variants (lines 157-171) | `WHERE QuoteId = @id` / `@QuoteId` | Yes |
| `QuoteTranslations.DeleteForQuote`/Insert (lines 178-183) | `WHERE QuoteId = @id` | Yes |
| `ConversationLines`'s two quote-referencing queries (lines 536, 551, 555) | `WHERE cl.QuoteId = @quoteId` etc. | Yes |

Queries already confirmed correctly wrapped and needing no change: `Characters.CountActiveReferences`-
style siblings that reference `PersonId`/`SourceId` (lines 296, 363) — already `UPPER()`-wrapped from
prior work; not `QuoteId`-related, listed only to confirm they were checked, not missed.

`SelectBase`'s own internal JOINs (`Quotes` → `Sources`/`Characters`/`People`/translations/`Series`/
`Universe`) are FK joins between two internally-computed values, not a caller-supplied comparison — once
the capture-point fix makes both sides consistently canonical, these need no wrapping.

---

## Steps

### 1. `EntityIdCanonicalizer` lowercase forms + tests

**Status:** Done. `src/Quotinator.Data/Helpers/EntityIdCanonicalizer.cs` extended with
`CanonicalizeLowercase`/`TryCanonicalizeLowercase` alongside #209's uppercase forms (#209 landed
first). `tests/Quotinator.Data.Tests/Helpers/EntityIdCanonicalizerTests.cs` extended with
`CanonicalizeLowercase_UppercaseGuid_ReturnsLowercaseD`, `CanonicalizeLowercase_AlreadyCanonical_IsIdempotent`,
`CanonicalizeLowercase_Malformed_Throws`, `TryCanonicalizeLowercase_ValidGuid_ReturnsTrueWithCanonicalForm`,
`TryCanonicalizeLowercase_Malformed_ReturnsFalse`.

### 2. `PlanAsync`'s quote loop capture-point fix

**Status:** Done. `ImportActionPlanner.PlanAsync`'s quote loop (`src/Quotinator.Core/Database/ImportActionPlanner.cs`)
renames the loop variable to `rawQuote` and builds a canonicalized `SourceQuote` copy (`q`) at the top of
each iteration via `EntityIdCanonicalizer.TryCanonicalizeLowercase`. `SourceQuote` is a plain class with
init-only properties, not a record — no `with` expression support — so the copy is built via object
initializer, the same pattern `QuoteFieldMerge.ApplyMergedFields` already uses in this same file. Every
later reference to `q.Id` in the iteration is automatically canonical once this substitution is made.

### 3. `UPPER()`-wrap every `Quotes.Id`/`QuoteId` query

**Status:** Done — subsumed by the scope-expansion work above, in two passes. The first pass
hand-wrapped every row in the query-audit table (`Quotes.SelectById()`/`SelectRawById()`/
`SelectCompletenessById`/`UpdateCompletenessById`/`UpdateOnNewestWins`, `QuoteGenres.*`,
`QuoteTranslations.*`, `ConversationLines`'s two quote-referencing queries) as part of the systemic
`SqlIdCaseGuard` fix pass, not as a separate, narrower pass targeting only this table — the guard
scans every SQL constant/factory-method/assembled query in the codebase, so Quotes/QuoteId coverage
is a subset of that full pass, not a distinct step. The second pass (the `IdClauses` helper, see
"Second round of scope expansion" above) rewrote every one of those same comparisons again to call
`IdClauses.Equals`/`In`/`NotIn`/`Join` instead of the hand-typed `UPPER(...)` text, including every
JOIN condition in `Quotes.SelectBase`/`SelectRawById()` and the correlated-subquery predicates in
`ConversationLines`. Verified via `SqlConstant_PassesIdCaseGuard`/`AssembledQuery_PassesIdCaseGuard`
(both test projects, reflection now covering fields *and* properties) and
`RepositorySqlFactory_PassesIdCaseGuard` — zero violations found on the final run, including under
the guard's own extended JOIN/`NOT IN` detection added during this same pass.

### 4. Audit `Guid`-typed vs. `string`-typed quote-id parameter bindings

**Status:** Done — no code change needed; findings recorded in Notes below. The only two call sites that
declare a `Guid quoteId` parameter (`QuoteSeedWriter.InsertTranslationsAsync`, `InsertGenresAsync`) never
bind that `Guid` struct directly to Dapper — both call `quoteId.ToString()` first, and `Guid.ToString()`'s
default format is already lowercase "D", identical to `EntityIdCanonicalizer.CanonicalizeLowercase`'s
output. Since Step 2 now guarantees the source string is canonically lowercase before
`Guid.Parse(resolved.Id)` is called, `Guid.Parse(...).ToString()` round-trips to the exact same string —
no forcing mismatch exists at either site. Every other quote-id-related call site in the codebase
(`SqliteQuoteService.GetById`, `QuoteSeedWriter.TryGetExistingFieldsAsync`, `ClearStaleAddTargetsAsync`'s
Quote branch, `RepositorySql.HardDelete("Quotes")`) binds the id as a plain `string`, never `Guid` —
confirmed by direct grep, not inferred from the two sites above.

### 5. Tests

**Status:** Done.

| Test class | Test method |
|---|---|
| `Quotinator.Data.Tests.Helpers.EntityIdCanonicalizerTests` | 5 lowercase-form cases (Step 1) |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanAsync_UppercaseExplicitQuoteId_ResolvedIdIsCanonicalLowercase` |
| `Quotinator.Core.Tests.Services.SqliteQuoteServiceTests` | `GetById_UppercaseUrlIdAgainstLowercaseStoredQuote_StillResolves` — corrected from this doc's original planning-stage placement in `SqliteImportActionServiceTests` (`GetById` is actually defined on `SqliteQuoteService`, verified by reading the code before writing the test, per this project's standing "verify plan doc against current code" rule). Inserts a quote via raw SQL with an explicit lowercase id (the generic repository's `Guid`-typed insert path would force uppercase and not actually exercise a lowercase-stored row), then confirms `GetById` resolves it via an uppercase URL id. |
| `Quotinator.Core.Tests.Security.SqlQueryGuardTests` (both projects) | `SqlConstant_PassesIdCaseGuard`, `AssembledQuery_PassesIdCaseGuard` — reflection widened to cover `static string` properties, not just fields |
| `Quotinator.Data.Tests.Repositories.RepositorySqlGuardTests` | `RepositorySqlFactory_PassesIdCaseGuard` |
| `Quotinator.Data.Tests.Diagnostics.SqlIdCaseGuardTests` | 23 cases covering the guard's own regex logic directly (bare/prefixed/aliased/bracket-quoted columns, half-protected wraps, `UPDATE SET` stripping, `IN`/`NOT IN` clauses, and both unwrapped and half-wrapped JOIN/correlated-subquery conditions) |
| `Quotinator.Data.Tests.Repositories.SystemImportActionWriterReaderTests` | `GetByIdAsync_LowercaseStoredId_StillResolves` — regression test for the property-reflection blind spot that let `Sql.SystemImportActions.SelectById`'s unwrapped `WHERE Id = @id` go undetected |
| `Quotinator.Data.Tests.Repositories.SystemAuditReaderTests` | `GetPagedAsync_MixedCaseRecordId_ReturnsLowercase` — regression test closing the fifth round's own coverage gap (`recordId`'s read-time fix previously had no unit test, only a live T2 pass) |

### 6. Verify

**Status:** Done for build/unit-test suite/T2; T1 needs a fresh confirmation from the developer after
this revision (Pass 1 predates it — see checklist row 7). `dotnet build --configuration Release` →
0 Warning(s)/0 Error(s). `dotnet test --configuration Release --verbosity normal` → full suite green
(790 in Core.Tests, 504 in Data.Tests, 493 in Api.Tests, plus the smaller projects — no regressions
from the capture-point fix, the `UPPER()`-wrap pass, the `IdClauses` refactor, or the casing-unification
revision). T2 (Docker) run three times — see checklist row 8 for all three passes, including Pass 3's
`ConversationLines.QuoteId` FOREIGN KEY-safety scenario specifically.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `EntityIdCanonicalizer`'s lowercase forms canonicalize, are idempotent, and reject malformed input via both a throwing and non-throwing form | Unit test | `EntityIdCanonicalizerTests` (5 lowercase-form cases) |
| 2 | ✅ | A file-authored quote id canonicalizes to lowercase at capture | Unit test | `ImportActionPlannerTests.PlanAsync_UppercaseExplicitQuoteId_ResolvedIdIsCanonicalLowercase` |
| 3 | ✅ | Every `Quotes.Id`/`QuoteId`-matching query is case-insensitive — and, per the scope expansion, every id-matching query in the codebase | Unit test | `SqlIdCaseGuard` wired into `SqlQueryGuardTests` (both projects) + `RepositorySqlGuardTests`; zero violations on the final run |
| 4 | ✅ | `GET /quotes/{id}` resolves regardless of URL casing — the previously fully-unmitigated gap | Unit test | `SqliteQuoteServiceTests.GetById_UppercaseUrlIdAgainstLowercaseStoredQuote_StillResolves` |
| 5 | ✅ | `Guid`-typed vs. `string`-typed quote-id parameter bindings are consistent | Doc review + code review | Step 4's audit findings recorded above — no mismatch found, no code change needed |
| 6 | ✅ | No regression | Unit test | Full `dotnet test --configuration Release --verbosity normal` — all green |
| 7 | ✅ | T1 — app starts in Visual Studio; every entity id, including quote ids, renders lowercase (the final convention — see "System-wide lowercase convention" above; superseded the round-3 uppercase-unification this row originally targeted) | Live (T1) | Pass 1 (pre-casing-unification) confirmed: clean startup, schema up to date (data v10, app v9), 796 quotes/479 sources/7 characters seeded, every touched endpoint 200. Pass 2 (developer's own Visual Studio session, covering every round through the eighth — lowercase system-wide, read-time presentation normalization, uniform SELECT-list wrap, `IEntityColumnMetadata`): confirmed live via manual testing of the masterdata/import-actions/audit flows — ids rendered lowercase throughout, case-insensitive lookups held. (This same session also surfaced an unrelated `batchId`/request-logging bug in the import-actions endpoints, fixed separately and out of scope for this issue — not part of this row's verification.) |
| 8 | ✅ | T2 — the case-insensitive lookup gap is fixed end to end, stays fixed after the `IdClauses` refactor and the `IEntityColumnMetadata` rewrite, and quote ids now render uppercase like every other entity | Live (T2), 4 passes | **Pass 1** (initial casing fix, pre-revision): imported a quote with explicit id `F0000210-0000-4000-8000-000000000210`; response's own `id` field came back canonically lowercase (`f0000210-...`); `GET /api/v1/quotes/{id}` returned 200 for both uppercase and lowercase URL casing. **Pass 2** (after the `IdClauses` refactor, pre-revision): baseline endpoints re-run clean; import-actions decide/apply exercised the fixed `SelectById` property; `/random?n=10` exercised the fixed `NOT IN` clause. **Pass 3** (after the casing-unification revision, fresh image rebuild): a fresh-seeded `/quotes/random` call already returned an uppercase `id` (e.g. `8AECF114-...`), confirming newly-seeded quotes are uppercase by default. Imported a quote with explicit id `f0000210-0000-4000-8000-000000000210` (lowercase) — response's own `id` came back canonically **uppercase** (`F0000210-...`), and `GET /api/v1/quotes/{id}` returned 200 for both casings. Imported a conversation whose `lines[].quoteId` (`F0000210-...-211`, uppercase) deliberately mismatched the referenced quote's own explicit id (`f0000210-...-211`, lowercase) — returned `200`, not `SQLite Error 19: FOREIGN KEY constraint failed`, and `GET /conversations/{id}` confirmed the line's embedded quote resolved correctly. Baseline search/import-actions/audit re-run clean. **Pass 4** (after the `IEntityColumnMetadata` rewrite replaced every `RepositorySql` `SELECT *` with an explicit column list): every generic-repository-backed masterdata endpoint re-run — `/masterdata/sources`, `/masterdata/characters`, `/masterdata/people`, `/masterdata/series`, `/masterdata/universes`, `/conversations`, `/masterdata/stagedirections`, `/masterdata/soundcues` (`pageSize=2` each) — all 200, all ids lowercase-rendered; `GetByIdAsync`'s case-insensitive lookup re-confirmed with both original and uppercased URL casing. |

---

## Notes

Step 4's audit (`Guid`-typed vs. `string`-typed quote-id parameter bindings): the only two call sites
declaring a `Guid quoteId` parameter — `QuoteSeedWriter.InsertTranslationsAsync` and `InsertGenresAsync`
— call `quoteId.ToString()` before binding, never the raw `Guid` struct. `Guid.ToString()`'s default
format is lowercase "D", matching `EntityIdCanonicalizer.CanonicalizeLowercase`'s output exactly, so once
Step 2 guarantees the source string is canonically lowercase before `Guid.Parse(resolved.Id)` runs,
`Guid.Parse(...).ToString()` round-trips to the identical string. No inconsistency exists; no code change
was needed for this step.

T2 note: since `data/sources/quotinator-curated.json` and the two bundled converter-plugin sources are
seeded with lowercase `id` fields already matching `QuoteIdentity.StableId`'s convention, the T2 smoke
test for this issue specifically imports an uppercase-cased explicit quote id (not a bundled file) to
exercise the fix — see the Verify step and CLAUDE.md's pre-push checklist for the exact commands once run.
