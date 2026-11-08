# #305: Database integrity check: verify every database's full schema against its migration path at startup

**Status:** In progress (step 12)
**GitHub issue:** #305
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Execute from step 12: update the documents this changes.** Every decision is settled (see
*Decisions*, including D5, found in step 3), the ADR 003 and ADR 009 amendments were approved on
2026-11-07, and the issue's title and body reflect the widened scope.

---

## Description

Startup reports a database healthy when its recorded schema version is current, without confirming that
the schema that version implies exists on disk. A table lost outside a migration goes unnoticed until
the first query touching it throws, at whichever call site runs first. This was reproduced by
simulation during #293, then occurred unprompted on a developer database, where `System_Notification`
was missing behind `schema is up to date` and `Quotinator ready`. #327 measured it again on
2026-11-01: with `System_Notification` missing, `/health` reads `healthy`.

This issue gives every database the application builds an **integrity check**: its actual schema is
compared, in every aspect that can be measured, against the schema its own migration path defines.
Anything missing or different is a hard failure that degrades, never a self-heal; anything extra is
reported, because it means a migration may have been missed. The same check is part of the testing
procedure, where it proves that a fresh install and an upgraded database end up identical.

---

## Scope changes

Developer direction, 2026-11-07, widening the issue as filed (tables only, main database only):

| Direction | Effect on this plan |
|---|---|
| The check covers as many aspects as can be measured: columns, foreign keys and indexes included | The comparison covers tables, columns, foreign keys, indexes, views and triggers. CHECK constraints are the one aspect not compared structurally; see *What is measured* |
| Extra tables and columns are indicated, so a missed migration is noticed | Extra objects are reported in the result and the log; they do not degrade |
| Every database the application builds can perform the check, since each has its own migration path | The check is generic and runs for the main database and the changelog database; a guard test holds every future database to it |
| The check is part of the testing procedure | The baseline-versus-replay drift tests and the last-release upgrade check run the same comparer |
| Database-engine specifics are kept to a minimum, so a future engine change stays cheap | The schema model and the comparer are engine-neutral; only the reader that produces a snapshot knows SQLite |
| Metadata is avoided because it is highly engine-specific, and engine-specific features are kept to a minimum; integrity verification is a valid use | ADR 003 is amended to state this, and `CLAUDE.md`'s sentence points to it (step 1) |

Per `process.md` → *Scope changes and deferrals*, the issue carries a comment recording this widening,
and its title and body were rewritten to match, both approved by the developer and posted on 2026-11-07
during planning.

---

## Spec cross-check

Checked per `process.md` → Planning step 3, in source order.

### ADRs

| ADR | Governs this issue? | Consequence for the design |
|---|---|---|
| 002, 008, 015 | No: no table, column or enum-backed column is added | No migration and no baseline change, unless step 4 finds drift a baseline must correct |
| 003 | **Yes** | Rule 3 forbids provider abstractions and conditional branches for other engines; rule 2 makes interfaces the contract with SQLite-specific implementations behind them. The design takes rule 2's shape and nothing more: an engine-neutral model and comparer, one interface for reading a snapshot, **one** SQLite implementation, no branching on engine and no configuration for one. This is the minimum the engine-change direction asks for, and nothing in it anticipates a second engine beyond keeping SQLite out of the comparison. No Dapper or SQLite type appears on a public interface. The ADR gains a design goal stating that engine-specific features, metadata reads included, are kept to a minimum (step 1) |
| 004 | Yes | The check never names a Quotinator table, so the model, comparer and reader live in `Quotinator.Data`. The wording an operator reads lives in `Quotinator.Api`, beside `SchemaOvershootGuidance` |
| 006 | Yes | New test classes stay sequential; each owns its temp database |
| 009 | **Yes, newly** | The last-release upgrade check this ADR mandates at milestone close gains a concrete instrument: the integrity check, run against a database upgraded from the last release's schema (step 5). The ADR is amended to name it (step 12) |
| 010 | Yes | Sabotage in the T2 documents goes through `scripts/testing/execute-sql.csx`; the check itself is run through the admin endpoint or the C# DbInspector command (R1), never a script in another language |
| 012, 017 | No: no id column is compared or selected, no join over domain tables | Metadata reads are not entity queries |
| 016 | Yes | The outcome is `SchemaIntegrityResult`; the model types are plain domain value objects (the suffix rule's own exemption), and any enum, such as the kind of difference, goes in `Quotinator.Data/Enums/`. The endpoint's top-level type is `DatabaseIntegrityResponse`; its per-database and per-difference members are `Dto`s, never `Response`s |
| 018 | **Yes** | "Every database gets the same migration capability" extends here: every database gets the same integrity capability, the changelog database included |
| 020 | Yes, by its reasoning, and literally for the endpoint | The expected schema is derived from each database's own migration path, never from a list kept by hand, which is how the existing drift tests' `ConsumerDomainTables` list came to need maintaining. The endpoint joins the existing admin tag, already declared with a description; `EveryTagAnEndpointUses_IsDeclaredWithADescription` confirms it |
| 021, 023 | No | No file input, no rule file |
| 022 | Yes | A difference is detectable by a check, so it is **returned as an outcome, never thrown**. Existing catches stay as the backstop for what the check cannot foresee |
| 024 | No, by D6 | Every compared value compares as written, foreign key actions included: names, declared column types, default expressions, index filters |
| 025 | Yes | The check writes nothing, at startup or through the endpoint, so there is no backup to place |
| 026 | Yes, by exclusion | No notification is raised for a failed check: see step 9 |

### Other authoritative sources

- **`CLAUDE.md` → "No exception-based migration recovery"** reserves structural metadata checks for the
  empty-database check. The developer's reasoning, 2026-11-07: metadata is avoided because it is highly
  engine-specific and engine-specific features are kept to a minimum; checking integrity requires
  metadata for a valid reason. No ADR stated this, ADR 003 being the closest, so step 1 adds it to
  ADR 003 as a design goal and makes the `CLAUDE.md` sentence a pointer to it.
- **`CLAUDE.md` → hard failure, not a self-heal**: applied. Nothing is recreated or altered; the
  application degrades and names a remedy.
- **The drift tests as they stand** compare columns and indexes table by table over a hand-kept list
  (`DatabaseInitializerTests.ConsumerDomainTables`, `DumpTableSchemaAsync`) and **never compare foreign
  keys**. Nothing proves today that a fresh install and an upgraded database agree on foreign keys, so a
  startup check that degrades on a difference could degrade a real installation over drift nobody has
  measured. This decides the order of work: measure first (steps 4 and 6), enforce at startup after.
- **`CLAUDE.md`'s endpoint rules**, applied to the integrity endpoint: one job, reporting, with no side
  effect (it never marks health either way: a Reset is what changes health); the `admin` rate-limit
  policy; the *Action* naming shape, `WithName("CheckDatabaseIntegrity")` with its name held in a
  `private const string` shared with the log tag; `[Description]` attributes and `docs/api-endpoints.md`
  updated in the same commit. It is not a list, so the pagination contract does not apply. Admin routes
  are already exempt from `DatabaseHealthGateMiddleware`, so the endpoint answers while degraded, which
  is when it is most useful.
- **The issue body** proposes extending `HasPendingContentSeedAsync`. Not done: that hook answers whether
  the content load needs a backup (#277, #423), and a second verdict does not belong in it.

---

## Decisions

**All settled.** D1, D2 and D4 are settled by the developer's direction above. D3 is settled as:
extras are reported, not degrading. R1 to R3 were answered by the developer on 2026-11-07, D6 on 2026-11-08 in step 11, and D5, an
unforeseen circumstance found in step 3, the same day, by following the T2 suite's own method.

**R1. How is the check run on request, by the testing procedure and by an operator?** **Both
instruments.** A read-only admin endpoint, `GET /api/v1/admin/database/integrity`, runs the check on
every database and returns the report. An `integrity` command in `tools/Quotinator.Tools.DbInspector`
runs it against a database file, without a running application. The T2 suite uses the endpoint against
a live container; the command covers a file on its own, such as a developer database or a backup.
See *On request*.

**R2. What does a failed check on the changelog database do?** **It falls back to the JSON files**, the
same as its initialisation failing today: logged at `Error`, non-fatal, the reader serves the authored
JSON, and main-database health is unaffected. Its content is regenerated from its files on every start,
so nothing is served from a wrong schema.

**D5. Where does row 13's last-release database come from?** (Found in step 3; decided 2026-11-07.)
**Rebuilt from the published image, the way the T2 suite does it; nothing is stored.** The published
`v1.8.3` image creates the database on a fresh volume and this build upgrades it, and the endpoint checks
the result (step 6, and T2 document 04's Upgraded scenario). This follows the 2026-08-25 decision in
`docs/automated-testing/README.md` → *Users skip versions*. A committed fixture was chosen first and
withdrawn once that record surfaced. The expected schema is always derived from this build's baseline, so
the migration condensing planned for this milestone's close needs no change here; the check is repeated
after it (step 12).

**D6. Does the comparison fold case anywhere?** (Found in step 11; decided 2026-11-08.) **No: everything
compares as written, names and foreign key actions alike.** No official SQLite page read states how table,
column or index names are cased, so none is assumed. The developer's
reasoning: the strictness reports a casing drift between our own scripts as soon as it starts. Measured on a
scratch database, SQLite itself folds ASCII letters in names (`CREATE TABLE MYTABLE` after `MyTable` is
refused as existing) and not others (`Ä` and `ä` are two tables). So a casing difference SQLite would ignore
is reported, as one object missing and another extra. Rows 10 to 12 pass case-sensitively, so no such drift
exists today; the live upgrade check (row 13) is repeated case-sensitively by document 04 in step 13.

**R3. Does a Reset re-run the check before clearing health?** **Yes.** Developer's reasoning: the check
is what runs before starting, so it should be what shows that a reset database is in a good state. A
Reset rebuilds from the same baseline the reference comes from, so the check should always pass there;
running it turns "Reset repaired the database" from an assumption into a measurement. A Reset whose
result is not intact stays degraded with the new reason.

---

## Design

### What is measured

| Aspect | Compared | Missing or different | Extra |
|---|---|---|---|
| Table | Name | Degrades | Reported |
| Column | Name, declared type, not-null, default expression, primary-key position | Degrades | Reported |
| Foreign key | Owning table and columns, referenced table and columns, on-delete and on-update actions | Degrades | Reported |
| Index | Name, table, uniqueness, columns in order, filter expression | Degrades | Reported |
| View, trigger | Name and owning table | Degrades | Reported |
| CHECK constraint | Not compared | n/a | n/a |

**CHECK constraints are not compared structurally**, and the plan says so rather than implying full
coverage. No engine-neutral metadata exposes them; the only route is the stored `CREATE TABLE` text,
which differs between a baseline's hand formatting and a migration-rebuilt table for identical
constraints, as the existing `Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues` comment
records. Their behaviour stays proven by that test. Automatically created indexes (those backing a
`UNIQUE` or primary key) are compared by their columns and uniqueness rather than their generated
names, since the name is the engine's and not the schema's.

### Shape

- **`SchemaSnapshot`** (engine-neutral): tables, each with columns, foreign keys and indexes, plus views
  and triggers. Plain values, no engine types.
- **`SchemaIntegrityComparer`** (engine-neutral, static, pure): `Compare(expected, actual)` returns a
  `SchemaIntegrityResult` listing every difference, each naming its object, aspect, expected and actual
  value, and whether it is missing, different or extra. `IsIntact` is true when nothing is missing or
  different. Differences are sorted, so a reason reads the same on every run. Being pure, it is unit
  tested exhaustively without a database.
- **`ISchemaSnapshotReader`** with one implementation, **`SqliteSchemaSnapshotReader`**: the only code
  that knows SQLite. Its metadata queries live in `Quotinator.Data.Queries.Sql`, in a nested class of
  their own.
- **The expected snapshot is derived from each database's own migration path**: a scratch database is
  built from that database's baseline SQL, or, when it has no baseline, by replaying its migrations, and
  read with the same reader. Each initializer supplies only the DDL list (`ExpectedSchemaScripts`); the
  reader runs it in a scratch database of its own (`ReadScriptsAsync`), since building one is
  engine-specific by nature. It is built in memory, so the check writes no file and works on a read-only
  data directory (#332).
- **Every database performs it**: the main database's `DatabaseInitializer` and the
  `ChangelogDatabaseInitializer` each run it after their migrations, and each exposes its last result.
  A guard test finds every type in the solution that declares a `SchemaMigration` list and fails for
  any that does not perform the check, so the next database inherits the requirement without anyone
  remembering it.

### At startup

- **Main database**: after `ApplyMigrationsAsync`, before the content load. Skipped when a migration was
  refused for want of a backup or the recorded version is ahead of the build, both already degraded and
  both meaningless to compare. A not-intact result stops the content load and returns an unsuccessful
  `DatabaseOperationResult`; `Program.cs` degrades with a reason from a new `SchemaIntegrityGuidance`
  naming every missing or different object and the two remedies, a Reset or a restore, keeping
  `MarkFailed`'s first-wins order after the refused-migration and overshoot cases.
- **Changelog database**: a not-intact result is logged at `Error` and the changelog falls back to the
  authored JSON files (R2). Main-database health is unaffected.
- **Extras, on either database**: logged at `Warning` with the `[Database - Init]` prefix, naming each,
  and carried in the result. They never degrade.
- **Reset**: re-runs the check on the rebuilt database before health is cleared (R3).

### On request

- **`GET /api/v1/admin/database/integrity`** (in the read-only admin group, so no key, like the seed
  preview and the audit log; `admin` rate-limit policy, developer-confirmed 2026-11-07): runs the check now,
  on every database the application has, and returns `200` with a `DatabaseIntegrityResponse`: per
  database, its name, whether it is intact, and every difference (object, aspect, expected, actual,
  missing/different/extra). `200` means the check ran, whatever it found; intact or not is in the body.
  It runs a fresh check rather than returning the startup result, so it shows the state after a Reset
  or a manual repair. It changes nothing, health included.
- **`DbInspector integrity <database file>`**: the same comparer against a file, choosing the expected
  schema from the migration tables the file holds (main or changelog). It prints the report and exits
  non-zero when the file is not intact, so a script can depend on it.

### In the testing procedure

- **Baseline versus replay, for every database**: one test per database builds it both ways and
  requires the comparer to report no difference at all, extras included. This covers foreign keys for
  the first time and needs no hand-kept table list.
- **Upgrade from the last release**: a database built by the last released version is upgraded by this
  build and compared against this build's baseline (ADR 009). This is the measurement that must pass
  before step 8 enables degradation at startup. It is rebuilt from the published image, as the T2 suite
  does (D5).
- **Live**: the T2 suite runs the check against a container's databases through the endpoint.

---

## Steps

### 1. Amend ADR 003, and point `CLAUDE.md`'s structural-metadata sentence at it

**Status:** ✅ Done

A `docs [#305]` commit of its own, drafted and approved before it is made.

- **ADR 003** gains a design goal (developer-approved, 2026-11-07): engine-specific features are kept to
  a minimum and confined to the implementations behind the interfaces, so changing engines stays cheap.
  Metadata reads are highly engine-specific, so they are used only where nothing engine-neutral can
  answer the question, such as verifying a schema's integrity, and only inside `Quotinator.Data`'s
  implementations. Stated as what is done and why, with no added explanation or history.
- **`CLAUDE.md`'s** sentence reserving structural metadata checks for the empty-database check becomes
  a pointer to that design goal, carrying no reasoning of its own.
- The same paragraph in `docs/database-conventions.md`'s *Migrations* section, if it restates the rule,
  is changed the same way.

### 2. Create the signatures only

**Status:** ✅ Done

`SchemaSnapshot` and its parts, `SchemaIntegrityResult`, the difference enum, `SchemaIntegrityComparer.Compare`
returning an intact result, `ISchemaSnapshotReader` and `SqliteSchemaSnapshotReader` returning an empty
snapshot, the initializers' result properties, `SchemaIntegrityGuidance.Reason` returning an empty
string, the endpoint returning an empty `DatabaseIntegrityResponse`, and the DbInspector command
returning success. No behaviour, so every test fails on its assertion. The endpoint is registered here,
so its `[Description]` attributes and its `docs/api-endpoints.md` row come with it, as `CLAUDE.md`
requires.

### 3. Write the unit tests and confirm each is red

**Status:** ✅ Done; row 13 is checked live in step 6 (D5)

Rows 1 to 19, 22 and 23 of the Verification table. Comparer tests supply both snapshots, covering each
aspect's missing, different and extra case, several differences at once, name casing (case-insensitive as written in step 3, case-sensitive since D6), and the
empty snapshot on both sides. Reader and startup tests use a real temp-file database per test, with the
sabotage applied by the test, never by editing shipped data.

Findings, 2026-11-07:

- **Step 2's stubs returned "intact", so every test asserting intactness would have been green before any
  behaviour existed.** They now throw `NotImplementedException`, so each test fails until step 4 implements
  what it covers.
- **Red run: 48 of 49 new tests red**, each for its intended reason (the stub's exception, an assertion
  that the check never ran, or the application reporting healthy over a missing table).
  `RepositoryStructureTests.EveryDatabaseWithMigrations_PerformsTheIntegrityCheck` is green, because step 2
  already made both initializers implement the interface it requires. It is a structural guard, so its
  sensitivity is proven by step 11's mutation (removing the interface from the changelog initializer).
- **Dropping `System_Notification` from a database a first run populated is refused by its foreign keys**
  (translation rows reference it). The Api tests turn foreign keys off for the sabotage connection only;
  step 7's T2 sabotage needs the same.
- **The changelog test is named for what the initializer can show.** The JSON fallback happens in
  `Program.cs`, which marks the changelog import as failed. Row 17's test proves the initializer reports
  the table missing and still completes; step 10 wires the fallback, and step 13's run of
  `notifications-and-changelog/07` checks it live.
- **Row 18's positive control already exists** as `Startup_RestartOverAnIntactDatabase_ReportsHealthy`, so
  the row names it instead of adding a duplicate.
- **The initializer construction the new class needed was moved, not copied**: `QuotinatorTestInitializer`
  now holds what `DatabaseInitializerTests.CreateForcedIncrementalInitializer` built, and both use it.
- **Row 13 had no database to run against**: a unit test cannot open a database the T2 suite never stores.
  Settled by D5: row 13 is checked live in step 6, by the T2 method.

### 4. Implement the model, the comparer and the reader; run the drift tests and record what they find

**Status:** ✅ Done

Implemented in that order. Then the baseline-versus-replay tests (rows 10 to 12) run for the first time
across every aspect. **Any difference they find is a finding, recorded here before anything is
changed.** A baseline that disagrees with replay is corrected in the baseline, in the same commit, as
`CLAUDE.md`'s migration policy requires. A difference that means installed databases already hold the
wrong shape cannot be fixed by a baseline edit and needs a new migration. That is an unforeseen
circumstance, not an open question: work stops at this step and the finding goes to the developer.

Findings, 2026-11-07:

- **No drift in any measured aspect, foreign keys included, for any database.** Rows 10 to 12
  (`MainDatabase_BaselineAndReplay_AreIdentical`, `DataOwnedSchema_BaselineAndReplay_AreIdentical`,
  `ChangelogDatabaseInitializerTests.Integrity_BaselineAndReplay_AreIdentical`) passed on their first run,
  with no extra objects either. This is the first time foreign keys and views or triggers were compared
  between a fresh install and a full replay. No baseline change and no migration is needed.
- **Rows 1 to 9 and 14 are green**: every comparer case and the reader against a real database.
- **The index-definition lookup compares its name through `TextClauses.Equals`**, per `CLAUDE.md`'s rule
  for name comparisons; `SqlTextCaseGuard` flagged the bare `name = @index` on the first run.

### 5. Add the integrity endpoint and the DbInspector command

**Status:** ✅ Done

The check on request, before anything enforces it: `ExpectedSchemaScripts` and `CheckIntegrityAsync`
for both initializers, the endpoint's behaviour behind the stub step 2 registered, and the `integrity`
command in `tools/Quotinator.Tools.DbInspector`. The endpoint follows *On request* and the endpoint rules
under *Other authoritative sources*; its description and `docs/api-endpoints.md` row are corrected in the
same commit if the behaviour changed what they say. Neither the endpoint nor the command holds comparison
logic of its own. Rows 22 and 23. It comes before step 6 because step 6 checks through the endpoint (D5).

Findings, 2026-11-07:

- **Row 23 is green** (`IntegrityCommandTests`, all three), and so are four of row 22's six tests.
  `Degraded_EndpointStillAnswers` and `Check_DoesNotChangeHealth` stay red because their precondition is a
  degraded application, which step 9 brings; they go green there.
- **The expected schema runs exactly what a fresh database runs**: the two version tables, then the
  baselines, or a replay of both migration lists when the consumer has no baseline. It is built in an
  in-memory scratch database, constructed directly, with the comment `CLAUDE.md`'s DI policy requires, as
  is the one stateless reader both builders share.
- **The command recognises a file by its version table**: `System_ConsumerSchemaVersion` means main,
  `Changelog_SchemaVersion` means changelog, and a file with neither exits `2` saying it is not a
  Quotinator database. `Program.cs` and `ArgsParser.cs` joined the scoped `IDE0008`/`IDE0090` lists when
  first touched.
- **Step 4's commit left one existing guard red, found by this step's full run.**
  `SqlBoundaryTests.Sql_ContainsOnlyGenericInfrastructureQueries` keeps a documented list of the query
  sets allowed in `Quotinator.Data`, and step 4 added `SchemaMetadata` without adding it there; step 4 had
  run only the tests it touched, not the full suite. The list now includes it, with the reason. Every step
  from here runs the full suite before its commit.
- **Full run: every remaining failure is a test that steps 8 to 10 turn green**, and nothing else fails.
- **Developer review after the commit: the initializers' new code named the engine three times** (a cast
  to `SqliteConnection`, a scratch `SqliteConnection`, and the concrete reader), where ADR 003's design goal
  5 asks for the minimum. The rest of `DatabaseInitializer` already names SQLite throughout, as the SQLite
  implementation of `IDatabaseInitializer`; the new code did not need to add to that. Corrected:
  - The scratch database moved into the reader, as `ISchemaSnapshotReader.ReadScriptsAsync`, so the reader
    is the only class in the check that knows the engine.
  - Each initializer now exposes `ExpectedSchemaScripts`, the plain DDL list a fresh database runs, and
    reads its own database through `DbConnection`.
  - Callers hold the reader through `SqliteSchemaSnapshotReader.Instance`, typed as the interface; the
    one engine name left in each initializer is that assignment.
  - `SqliteSchemaSnapshotReaderTests.ReadScriptsAsync_ReadsTheSchemaTheScriptsCreate` covers the moved
    behaviour. It was written after the move rather than red first, so step 11 adds a mutation for it.

### 6. Check a database upgraded from the last release, live

**Status:** ✅ Done

Row 13, by the T2 suite's own method (D5): the database is rebuilt from the published image, never
stored. The published `v1.8.3` image runs against a fresh volume until its first boot has finished,
judged by its `/health` reading healthy rather than by a wait; it is stopped, and this build's image runs
against the same volume. Startup enforcement is not on yet (step 8), so this build starts whatever it
finds, and `GET /api/v1/admin/database/integrity` must report every database intact. Findings are handled
as in step 4. Containers, volume and image tag are removed afterwards; every step is a single command,
recorded here when run. Step 8 does not start until this row is green.

The same check is repeated at the milestone's close, after the migrations are condensed, as ADR 009's
last-release verification (step 12).

Run 2026-11-07, this build at `88795817`, by document 09's mechanics (`scripts/testing/test-env.csx` over
a bind-mounted directory):

```bash
docker build -f docker/Dockerfile -t quotinator:305-step6 .
dotnet script scripts/testing/test-env.csx -- create --name qt-305-upgrade-183 --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind '<temp>\qt-305-upgrade-data-1'
# waited on its log for "Quotinator ready"; [Runtime - Exception] count before the stop: 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-305-upgrade-183 --bind '<temp>\qt-305-upgrade-data-1'
dotnet script scripts/testing/test-env.csx -- reenter --name qt-305-upgrade-current --port 18530 --image quotinator:305-step6 --bind '<temp>\qt-305-upgrade-data-1'
dotnet script scripts/testing/http.csx -- --url "http://localhost:18530/api/v1/health" --wait-for 200 --status
curl -s http://localhost:18530/api/v1/admin/database/integrity
```

- The released database recorded data v3 and app v5. This build applied 25 Data migrations (v3 to v28)
  and 4 App migrations (v5 to v9), logged no exception, and reported `200` on `/health`.
- **The endpoint answered
  `{"databases":[{"database":"main","isIntact":true,"differences":[]},{"database":"changelog","isIntact":true,"differences":[]}]}`**:
  the upgraded database matches this build's schema in every measured aspect, with no extra object.
  Row 13 is green, so step 8 may enforce the check at startup.
- Torn down with `test-env.csx -- destroy`, `docker rmi quotinator:305-step6`, and the bind directory
  removed. The pulled `1.8.3` image is kept, since other T2 documents use it.

*Found in step 3, 2026-11-07:* row 13 was written as a unit test, but the T2 *Upgraded* profile stores no
database, so a unit test had nothing to open. A committed fixture was chosen first and then withdrawn,
since `docs/automated-testing/README.md` → *Users skip versions* records the 2026-08-25 decision that such
databases are rebuilt from published image tags, not stored. D5 records the outcome.

### 7. Write the T2 documents and confirm them red against a pre-issue build

**Status:** ✅ Done

- `startup-and-degradation/14-a-schema-that-does-not-match-its-version-degrades-and-names-it.md`, in the
  smoke set like every degraded-start document. Fresh profile. It removes `System_Notification` (the
  table from the live incident) and, in a second scenario, a column, through
  `scripts/testing/execute-sql.csx`, with foreign keys off for that connection only (step 3's finding).
  It asserts the degraded contract (process up, `/health` unhealthy with a reason naming the object,
  `/openapi/v1.json` answering, quote endpoints `503`, `/notifications` rendering the degraded page) and
  ends by running the remedy it names, a Reset, then reading healthy.
- `database-lifecycle/04-a-database-is-complete-for-its-schema-version.md`: the integrity endpoint
  against a Fresh container and an Upgraded one (the published `v1.8.3` tag, as in step 6), each
  reporting every database intact with no extras. Then a column is added to the running Fresh database
  through `execute-sql.csx`, and the endpoint reports it as extra while `/health` stays healthy (the
  positive control that the instrument can find one, and that an extra does not degrade).

Canary: both run against a build of the commit before this issue's first code change, in their own
worktree and image tag; document 14 must read `healthy` there. Worktree, image and container are removed
afterwards. Every step is a single command.

Findings, 2026-11-08:

- **Both canaries are red**, against `95093abc` built in a detached worktree as `quotinator:canary-305`.
  Document 14: the integrity call `404`, then with `System_Notification` dropped `/health` read
  `{"status":"healthy"}` and `/quotes/random` `200`, #305's premise reproduced. Document 04: the integrity
  call `404` on a healthy fresh container. Each document records its reading under *Canary*; container,
  directories, image and worktree are removed.
- **The index entries moved here from step 12**: the smoke set (now nineteen), both category tables, and
  *Observed effects awaiting measurement* (now 42 of 83). The guard tests that keep them in step with the
  documents would otherwise fail on this commit. Both documents are also listed in `Quotinator.slnx`.
- **Document 04 waits on the released image by `wait-field` against a published port**, not by its log in
  a shell loop, so every step stays one command, as the index requires.
- **Both documents are red against this build too**, until steps 8 and 9 make startup enforce the check:
  rows 20 and 21 go green in step 13's full run, which also records each document's *Observed effect*.

### 8. Wire the main database's startup and Reset

**Status:** ✅ Done

In `DatabaseInitializer`: run the check step 5 built after migrations, skip on refusal and overshoot,
stop the content load when not intact, log extras and differences through source-generated
`LoggerMessage`s with the `[Database - Init]` prefix. The scratch reference database is the reader's,
already in place from step 5, so nothing engine-specific is added here. Reset per R3.
`HasPendingContentSeedAsync` is not changed.

Findings, 2026-11-08:

- **Startup checks after migrations and before the content load**, skipping a refused migration (which
  returns first) and an overshoot. A difference returns `DatabaseOperationResult.NotIntact()`, a new result
  carrying `SchemaNotIntact`, so a caller can tell it from a backup refusal. Each difference is logged at
  `Error` and each extra object at `Warning`, through source-generated `LoggerMessage`s with the
  `[Database - Init]` prefix, and a summary line names both remedies.
- **Reset checks the rebuilt database before `OnResetCompletedAsync`**, so nothing is written into a rebuilt
  schema that still differs, and returns `NotIntact()` when it does.
- **Three existing tests were re-based, keeping their subject.**
  `DatabaseInitializerTests.InitialiseAsync_ExecuteStepFails_SurfacesDistinctFailureReason` and
  `InitialiseAsync_SeedingFailsOnAlreadyMigratedDatabase_BacksUpFirstAndRethrows` dropped a table to make
  seeding fail after its backup; the check now finds that first, so seeding never ran. The backstop they
  cover is kept for what the check cannot foresee (ADR 022), so each now empties the genre table and adds a
  trigger that refuses its inserts: the check reports the trigger as extra and does not stop, and the insert
  fails after the backup as before. `SafeHasPendingContentSeedAsync`'s fail-safe, which the first test also
  exercised through a missing table, is no longer reached by it: a missing table is now the check's to find.
  `DatabaseBackupQuotaTests.ResetAsync_WithTheOverride_ReportsThatTheBackupWasSkipped` uses a double whose
  reset rebuilds nothing, so it now gives the database Quotinator.Data's schema first, as a real rebuild
  would.
- **Rows 15 and 16 are green, and row 17's Reset test.** Every test still red belongs to steps 9 and 10,
  including the existing `StartupResilienceTests.Startup_DomainTableMissing_*`: the missing table is now
  found, and the application degrades on it once step 9 maps `NotIntact` in `Program.cs`.
- **A Reset that is not intact is reported by its callers as a refused backup** (`AdminEndpoints`'s reset,
  `NotificationActionExecutor`, `DatabaseRepairActions`), since each reads any unsuccessful result as one.
  Unreachable in practice, a Reset rebuilds from the same baseline the expected schema comes from, but
  wrong if reached; step 9 maps `SchemaNotIntact` in all three, keeping the application degraded with the
  new reason (R3).

### 9. Degrade startup in `Program.cs`, with no notification

**Status:** ✅ Done

`SchemaIntegrityGuidance` beside `SchemaOvershootGuidance`, and the `MarkFailed` call after the overshoot
check. The Reset's three callers map `SchemaNotIntact` too (step 8's finding). The reason uses engine-neutral words (table, column, foreign key, index), never an engine's
metadata names. **No notification is written**: writing a row into a database known not to match its
schema is the unprotected write #348 refuses, and the missing object can be `System_Notification`
itself. The generic catch's wording is #457's.

Findings, 2026-11-08:

- **The reason names every missing or different object and leaves extras out**, since they never
  degrade. Each is described in words (*missing table X*, *column X has declared type 'B' where 'A' is
  expected*), and it ends with both remedies and `GET /api/v1/admin/database/integrity`, now
  `ApiRoutes.DatabaseIntegrity`, for the full list. Startup logs it at `Critical` with the `[Server]`
  prefix, as the two cases before it do.
- **A Reset that is not intact replaces the reason rather than keeping the old one.** `MarkFailed` keeps
  the first reason, and the earlier one no longer describes a database the Reset rebuilt, so
  `DatabaseHealthState.ReplaceFailure` records the new reason without passing through healthy. The admin
  endpoint answers `500` with the reason as `detail`, not `409`, since nothing about it can be resolved and
  retried; the notification action returns `NotificationActionResult.ResetNotIntact`, a new result carrying
  `FailureReason` and no backup obstacle, and leaves the notification active; the repair control tells the
  operator what is still wrong. Each written red first: `AdminEndpointsTests.ResetRebuiltSchemaNotIntact_Returns500NamingTheObjectAndStaysDegraded`,
  `NotificationActionExecutorTests.DatabaseReset_WhenNotIntact_*` (three; the notification one was already
  green, since the refusal path also returns before dismissing), and
  `DatabaseRepairActionsTests.ResetNotIntact_NamesWhatIsStillWrong`.
- **`Startup_SchemaDifference_ResetRestoresHealth` answered `401`**, a step 3 test fault: its admin key was
  set through `UseSetting`, which a developer's `appsettings.local.json` overrides. It now sets it in
  memory, as `AdminEndpointsTests` does.
- **Rows 18, 19 and 22 are green**, and so are the existing `Startup_DomainTableMissing_*`. The full run
  leaves only `ChangelogDatabaseInitializerTests.Integrity_MissingTable_ReportedWithoutFailingInitialisation`
  red, which is step 10's.

### 10. Wire the changelog database

**Status:** ✅ Done

`ChangelogDatabaseInitializer` runs the same check after its migrations. A not-intact result is logged
at `Error` and the reader falls back to the authored JSON (R2).

Findings, 2026-11-08:

- **The check runs after every initialisation path** (baseline, up to date, migrated) and records its
  result. Each difference is logged at `Error` and each extra at `Warning` with the `[Changelog - Init]`
  prefix, and a summary line says the changelog is served from its JSON files and main health is
  unaffected. Initialisation does not fail.
- **`Program.cs` marks the changelog import failed instead of running it** when the result is not intact,
  so the reader serves the JSON files; the content refresh never writes into the schema.
- **A dropped table could not prove that wiring**: the import fails on its own without the table, and the
  reader falls back regardless, so a test built on it passed before `Program.cs` changed.
  `StartupResilienceTests.Startup_ChangelogSchemaDifference_FallsBackToTheJsonFilesAndStaysHealthy` drops
  `IX_Changelog_Line_ChangelogEntryId` instead, which the import tolerates, and was red before the change.
- **Row 17 is green.** The full run is green with 0 warnings.

### 11. Prove each test is wired to the behaviour

**Status:** ✅ Done

Mutation, each reverted with `git checkout` and re-run green: compare names case-sensitively (run before D6, when names folded case; its reverse, folding case, is run after it); drop
foreign keys from the reader; treat an extra as a difference; run the content load despite a
difference; stop skipping on overshoot; omit an object from the reason; remove the check from the
changelog initializer (the guard test must fail); clear health on Reset without re-running the check;
have the endpoint return the startup result instead of a fresh check; make the DbInspector command exit
zero when not intact; let `ReadScriptsAsync` reuse one scratch database across calls.

Findings, 2026-11-08:

- **Each of the first five mutations turned its test red**, and each was reverted:
  - compare names case-sensitively: `NameCasingDiffers_IsNotADifference`
  - drop foreign keys from the reader: both `SqliteSchemaSnapshotReaderTests`
  - treat an extra as a difference: `ExtraObjects_AreReportedButIntact` and `AdminDatabaseIntegrityEndpointTests.ExtraColumn_ReportedButIntact`
  - run the content load despite a difference: `MissingExpectedTable_DetectedAsSchemaInconsistency_TreatedAsHardFailure` and `Difference_ContentLoadDoesNotRunAndNothingIsWritten`
  - stop skipping on overshoot: `SchemaVersionOvershoot_IntegrityNotChecked`
- **The first mutation raised D6**: the developer decided nothing folds case. The casing test was rewritten
  as four, each red against the case-folding comparer, and the comparer changed. A first pass kept foreign
  key actions case-insensitive as closed-set keywords, which D6 does not allow; corrected the same day. The
  full run is green, rows 10 to 12 included.
- **M6 (omit an object from the reason)** turned `Reason_NamesEveryDifference` and both
  `Startup_SchemaDifference_HealthReasonNamesTheObject` rows red.
- **M7 (remove the check from the changelog initializer)** does not compile: the integrity endpoint lists
  the changelog initializer as an `IIntegrityCheckedDatabase`, so the compiler refuses before the guard test
  runs. With the endpoint's list reduced as well, so the build succeeds,
  `RepositoryStructureTests.EveryDatabaseWithMigrations_PerformsTheIntegrityCheck` turned red.
- **M8 (clear health on Reset without re-running the check)** turned
  `Reset_RerunsTheCheckBeforeClearingHealth` red.
- **M9 (the endpoint returns the startup result)** turned only `ExtraColumn_ReportedButIntact` red: it is the
  one endpoint test that changes the schema after startup, so the others read the same either way. That one
  is enough to hold the endpoint to a fresh check.
- **M10 (the DbInspector command exits zero when not intact)** turned
  `MissingTable_ReportsItAndExitsNonZero` and `ChangelogFile_ComparedAgainstTheChangelogSchema` red.
- **M11 (`ReadScriptsAsync` reuses one scratch database)** turned
  `ReadScriptsAsync_ReadsTheSchemaTheScriptsCreate` red.
- **Every mutation was reverted with `git checkout`**, and the full run on the clean tree is green with 0
  warnings.

### 12. Update the documents this changes

**Status:** ⬜ Not started

- `docs/automated-testing/README.md`: done in step 7, with the documents; here, only document 14 and 04
  leaving *Observed effects awaiting measurement* once step 13 records their readings.
- `startup-and-degradation/01` drops `Quotinator_Quote`, which now degrades through this check instead of
  the generic catch; any reason it records is updated, and it is re-run.
- ADR 009 amended (developer-approved, 2026-11-07): its procedure names the integrity check as the
  instrument of the last-release verification, step 3's "confirm the result matches" becoming "the
  integrity check reports the upgraded database intact". The amendment states what is done and why,
  with no added explanation or history. Per D5, its step 1 obtains the last release's database by running
  the published image, as the T2 suite does. `docs/workflow/checklist.md` → *Milestone close* names the
  same instrument, run after the migrations are condensed.
- `docs/vocabulary.md`: *integrity check*, *schema snapshot*.
- `tools/Quotinator.Tools.DbInspector/README.md`: the `integrity` command.
- A Knowledgebase entry for the new degraded reason, pointing at the endpoint to see every difference;
  no `QTN-` code, since #333 owns allocation.

`docs/api-endpoints.md` goes with the endpoint's code commits (steps 2 and 5), as `CLAUDE.md` requires;
the rest each in a `docs [#305]` commit separate from the code.

### 13. Run the full gates

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both at 0 warnings and 0 errors; `docker build`; T2 at end-of-issue scope (the smoke set, documents 14
and `database-lifecycle/04`, and `startup-and-degradation/01`, `06` and `12`, the last two because the
check is skipped on their paths, and `notifications-and-changelog/07` for the changelog database);
then T1, the developer starting the app in Visual Studio.

### 14. Waiting-for-release housekeeping

**Status:** ⬜ Not started

`checklist.md` → *Before closing an issue* → *Waiting for release*: boyscout pass over every touched file
(each added to `.editorconfig`'s scoped `IDE0008` and `IDE0090` sections when first touched, log
prefixes, `nameof` in any `Sql.*` class touched), process gap check, the `unreleased` changelog entry
with a highlight in `en`/`nl`/`de` lockstep and `CHANGELOG.md` regenerated alone, the Definition of done
ticked except the closing-comment box, and the status moved to `Waiting for release` here and in
`overview.md`.

---

## Verification

All rows started ❌. Rows 1 to 12, 14 to 19, 22 and 23 were written red in step 3; row 13 is checked live
in step 6; rows 20 and 21 are written red in step 7.

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Identical snapshots are intact, including two empty ones | Unit test | `SchemaIntegrityComparerTests.IdenticalSnapshots_AreIntact`, `SchemaIntegrityComparerTests.TwoEmptySnapshots_AreIntact` |
| 2 | ✅ | A missing table is detected and named | Unit test | `SchemaIntegrityComparerTests.MissingTable_IsADifference` |
| 3 | ✅ | A missing column, and each differing column property, is detected | Unit test | `SchemaIntegrityComparerTests.MissingColumn_IsADifference`, `SchemaIntegrityComparerTests.ColumnPropertyDiffers_IsADifference` (one row per property) |
| 4 | ✅ | A missing or differing foreign key is detected | Unit test | `SchemaIntegrityComparerTests.MissingForeignKey_IsADifference`, `SchemaIntegrityComparerTests.ForeignKeyActionDiffers_IsADifference` |
| 5 | ✅ | A missing or differing index is detected, an engine-named index by its columns | Unit test | `SchemaIntegrityComparerTests.MissingIndex_IsADifference`, `SchemaIntegrityComparerTests.IndexUniquenessDiffers_IsADifference`, `SchemaIntegrityComparerTests.AutomaticIndexNameDiffers_IsNotADifference` |
| 6 | ✅ | A missing view or trigger is detected | Unit test | `SchemaIntegrityComparerTests.MissingViewOrTrigger_IsADifference` |
| 7 | ✅ | Extra tables, columns, foreign keys and indexes are reported and leave the result intact | Unit test | `SchemaIntegrityComparerTests.ExtraObjects_AreReportedButIntact` |
| 8 | ✅ | Every difference is reported, sorted, not only the first | Unit test | `SchemaIntegrityComparerTests.SeveralDifferences_AreAllReportedInOrder` |
| 9 | ✅ | Everything compares as written, so a casing difference in a table, column, foreign key reference, foreign key action or declared value is reported (D6) | Unit test | `SchemaIntegrityComparerTests.TableNameCasingDiffers_IsADifference`, `SchemaIntegrityComparerTests.ColumnNameCasingDiffers_IsADifference`, `SchemaIntegrityComparerTests.ForeignKeyReferenceCasingDiffers_IsADifference`, `SchemaIntegrityComparerTests.ForeignKeyActionCasingDiffers_IsADifference`, `SchemaIntegrityComparerTests.DeclaredTypeCasingDiffers_IsADifference` |
| 10 | ✅ | Main database: baseline and full replay are identical in every measured aspect | Integration test | `DatabaseIntegrityCheckTests.MainDatabase_BaselineAndReplay_AreIdentical` |
| 11 | ✅ | Data-owned schema alone: baseline and replay are identical | Integration test | `DatabaseIntegrityCheckTests.DataOwnedSchema_BaselineAndReplay_AreIdentical` |
| 12 | ✅ | Changelog database: baseline and replay are identical | Integration test | `ChangelogDatabaseInitializerTests.Integrity_BaselineAndReplay_AreIdentical` |
| 13 | ✅ | A database the published `v1.8.3` image created, upgraded by this build, is intact | Live (Docker) | Step 6's commands: `v1.8.3` on a fresh volume until healthy, then this build on the same volume; `GET /api/v1/admin/database/integrity` reports every database `isIntact: true` |
| 14 | ✅ | The reader captures each aspect from a real database, and reads the schema a list of scripts creates in a scratch database of its own | Integration test | `SqliteSchemaSnapshotReaderTests.ReadsTablesColumnsForeignKeysIndexesViewsAndTriggers`, `SqliteSchemaSnapshotReaderTests.ReadScriptsAsync_ReadsTheSchemaTheScriptsCreate` |
| 15 | ✅ | Main startup: a difference stops the content load, takes no backup and writes nothing; a complete database loads normally | Integration test | `DatabaseIntegrityCheckTests.MissingExpectedTable_DetectedAsSchemaInconsistency_TreatedAsHardFailure`, `DatabaseIntegrityCheckTests.Difference_ContentLoadDoesNotRunAndNothingIsWritten`, `DatabaseIntegrityCheckTests.AllExpectedTablesPresent_PassesCleanly` |
| 16 | ✅ | The check does not run on a refused migration or an overshoot | Integration test | `DatabaseIntegrityCheckTests.MigrationRefused_IntegrityNotChecked`, `DatabaseIntegrityCheckTests.SchemaVersionOvershoot_IntegrityNotChecked` |
| 17 | ✅ | Every database declaring migrations performs the check; a changelog database that is not intact is reported without failing its initialisation, so the changelog falls back to the JSON files without affecting main health; a Reset re-runs the check before clearing health, and each of its callers keeps a not-intact Reset degraded on the new reason rather than reporting a refused backup | Unit test / Integration test | `RepositoryStructureTests.EveryDatabaseWithMigrations_PerformsTheIntegrityCheck`, `ChangelogDatabaseInitializerTests.Integrity_MissingTable_ReportedWithoutFailingInitialisation`, `StartupResilienceTests.Startup_ChangelogSchemaDifference_FallsBackToTheJsonFilesAndStaysHealthy`, `DatabaseIntegrityCheckTests.Reset_RerunsTheCheckBeforeClearingHealth`, `AdminEndpointsTests.ResetRebuiltSchemaNotIntact_Returns500NamingTheObjectAndStaysDegraded`, `NotificationActionExecutorTests.DatabaseReset_WhenNotIntact_StaysUnhealthyWithTheNewReason`, `NotificationActionExecutorTests.DatabaseReset_WhenNotIntact_ReportsTheReasonRatherThanABackupObstacle`, `NotificationActionExecutorTests.DatabaseReset_WhenNotIntact_LeavesTheNotificationActive`, `DatabaseRepairActionsTests.ResetNotIntact_NamesWhatIsStillWrong` |
| 18 | ✅ | Against the real schema, a missing table or column degrades startup with a reason naming it and both remedies; quote endpoints are gated; pages render degraded; a Reset restores health; a complete database is healthy | Integration test | `StartupResilienceTests.Startup_SchemaDifference_HealthReportsUnhealthy`, `StartupResilienceTests.Startup_SchemaDifference_HealthReasonNamesTheObject`, `StartupResilienceTests.Startup_SchemaDifference_QuoteEndpointsAreGated`, `StartupResilienceTests.Startup_SchemaDifference_BlazorPageRendersDegradedUi`, `StartupResilienceTests.Startup_SchemaDifference_ResetRestoresHealth`, `StartupResilienceTests.Startup_RestartOverAnIntactDatabase_ReportsHealthy` (existing, the positive control) |
| 19 | ✅ | The reason names every difference in engine-neutral words, and both remedies | Unit test | `SchemaIntegrityGuidanceTests.Reason_NamesEveryDifference`, `SchemaIntegrityGuidanceTests.Reason_NamesResetAndRestore`, `SchemaIntegrityGuidanceTests.Reason_UsesNoEngineMetadataNames` |
| 20 | ❌ | Live: a removed table, and a removed column, each degrade the container with a named reason, and a Reset recovers it; a pre-issue build does not degrade | Live (Docker) | `automated-testing/startup-and-degradation/14-a-schema-that-does-not-match-its-version-degrades-and-names-it.md`, green on this build, red as its canary |
| 21 | ❌ | Live: a Fresh and an Upgraded container report every database intact through the endpoint, and an added column is reported as extra without degrading | Live (Docker) | `automated-testing/database-lifecycle/04-a-database-is-complete-for-its-schema-version.md` |
| 22 | ✅ | The endpoint runs a fresh check on every database, reports intact and not-intact results with every difference, answers while degraded, writes nothing and leaves health unchanged, answers without a key like the other read-only admin endpoints, and is described in the spec | Integration test | `AdminDatabaseIntegrityEndpointTests.IntactDatabases_ReportIntact`, `AdminDatabaseIntegrityEndpointTests.MissingTable_ReportsTheDifference`, `AdminDatabaseIntegrityEndpointTests.ExtraColumn_ReportedButIntact`, `AdminDatabaseIntegrityEndpointTests.Degraded_EndpointStillAnswers`, `AdminDatabaseIntegrityEndpointTests.Check_DoesNotChangeHealth`, `AdminDatabaseIntegrityEndpointTests.WithoutAdminKey_IsAnswered`, `OpenApiSpecEndpointTests.EveryTagAnEndpointUses_IsDeclaredWithADescription` |
| 23 | ✅ | The DbInspector command reports a file's differences and exits non-zero when it is not intact | Unit test | `IntegrityCommandTests.IntactFile_ExitsZero`, `IntegrityCommandTests.MissingTable_ReportsItAndExitsNonZero`, `IntegrityCommandTests.ChangelogFile_ComparedAgainstTheChangelogSchema` |
| 24 | ❌ | No regression | Build | `dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`: 0 warnings, 0 errors, all green; T2 end-of-issue scope per step 13 green |
| 25 | ❌ | The application still starts | Manual | T1: the developer starts `Quotinator.Api` in Visual Studio and confirms it starts without error |
