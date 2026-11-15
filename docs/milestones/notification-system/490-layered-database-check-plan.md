# #490: Check every database in layers, cheapest first, at startup, before a Reset, and on demand

**Status:** In progress (step 11)
**GitHub issue:** #490
**Tiers required:** T1, T2
**Depends on:** none. Blocks #457, #491, #492.

---

## Next action

**Execute from step 11.** Decisions A to E were accepted as recommended on 2026-11-14, and decision E's
follow-up is #493. Step 6 left one deviation from the verification table for the developer to confirm; it
is listed under step 6. The plan is built against the code at `342e0cfa` and the sqlite.org
documentation as read on that date.

---

## What this builds

The issue body covers the motivation. In short: today a database is judged in three places by three
different means. Startup opens it and catches what breaks. A Reset runs `PRAGMA quick_check`, which #482
chose without asking the developer. The integrity endpoint compares only schema metadata. This issue
replaces all three with one check per database, run in layers from cheapest to most thorough, where the
first failure is the result.

Two developer directions (2026-11-14) govern the whole design:

- **Engine-neutral.** The check is *"as generic as possible, so we can replace the database engine if we
  want to."* The principle and every type the rest of the application sees are engine-neutral. Only the
  implementation behind them is SQLite (decision A).
- **Documented behaviour only.** *"If it is not documented then we cannot rely on it for our test and we
  have to assume the worst case scenario."* No layer, test fixture or decision rests on behaviour observed
  by experiment. Where the engine's documentation is silent, the design assumes the worst case, and *Only
  what is documented* below says which case that is. Measuring how long something takes is still done,
  because that is a cost, not a behaviour the design relies on.

**Boundary with #457** (posted on both issues, 2026-11-14):
- **#457 owns** the degraded reason, its remedies and its Knowledgebase entry for the layers that mean the
  file is not a sound copy (Format, Journal, Pages, Indexes below). Until #457 lands, those layers keep the
  generic reason.
- **This issue owns** the check, the outcome, the startup wiring, the reasons for the Storage and
  References layers and for an inconclusive check (decision D), and creating `StartupDegradationReasons`.
  It keeps the existing #326 and #305 reasons.

**Not this issue:** showing the report on a page is #491, and retrying startup is #492.

---

## Verified against the code at `342e0cfa`

- **Startup** (`Program.cs`, after `app.StartAsync()`): the changelog database initialises first, inside
  its own try/catch, and falls back to the JSON files on any failure (#309, ADR 018). The main database
  then runs `dbInitializer.InitialiseAsync()`. Three outcome blocks follow (#348, #350, #305), then
  `catch … when (IsDataDirectoryNotWritable(ex))` for error codes 14 and 8 (#326), then the generic catch.
- **`DatabaseInitializer.InitialiseAsync`** (line 684): `MigrateFilenameIfNeeded`, open, `EnableWal`,
  migrations, the #305 schema check, then the content hook. Nothing checks the file before it is opened
  for writing.
- **`ResetAsync`** (line 856) calls the private `IsDatabaseFileReadable()`, which opens the file
  `immutable=1` read-only and runs `PRAGMA quick_check`, then either `CheckBackupReadiness()` or
  `CheckSetAsideReadiness()`. `RebuildAsync` (around line 1186) calls `CheckSetAsideReadiness()` again.
- **There are two database files:** `quotinatordata.db` (`DataPaths.DatabaseFile`) and
  `quotinatorchangelog.db` (`DataPaths.ChangelogDatabaseFile`). Both are files.
- **The #305 check** is `IIntegrityCheckedDatabase` (`DatabaseName`, `LastIntegrityResult`,
  `CheckIntegrityAsync`). Both initializers implement it, and
  `GET /api/v1/admin/database/integrity` (`AdminEndpoints.cs`, `CheckDatabaseIntegrityName`) iterates
  over them and returns `DatabaseIntegrityResponse`/`DatabaseIntegrityDto` (`Quotinator.Core.Models`).
- **Foreign keys (115 `REFERENCES` clauses) are enforced today, but only by accident, and the code
  believes otherwise.** Checked 2026-11-14 at the developer's request:
  - `SqliteConnectionFactory` builds `Data Source={dbPath}` and nothing else. Nothing in `src/` turns
    enforcement on for ordinary connections.
  - Microsoft's connection-string page says that when `Foreign Keys` is not specified, the provider
    *"Doesn't send `PRAGMA foreign_keys`. This is the default."* So the setting is SQLite's own default.
  - sqlite.org documents that default as off (*"disabled by default (for backwards compatibility)"*)
    unless the library is compiled with `SQLITE_DEFAULT_FOREIGN_KEYS`.
  - The native library this solution ships reports `DEFAULT_FOREIGN_KEYS` in `PRAGMA compile_options`,
    and a fresh connection built exactly as the factory builds it returns `PRAGMA foreign_keys` = `1`.
    This was read on Windows only, first with `SQLitePCLRaw.lib.e_sqlite3` 3.53.3, then with the
    `SQLite` 3.53.4 package that Microsoft.Data.Sqlite 11 takes its native library from. The 3.53.3 pin
    was removed on 2026-11-15, once it no longer decided which library ships.
  - So enforcement rests on a compile option of a third-party native build, which no project document
    records or decides. The comments in `Sql.cs` (`ChangelogContent`, `DeleteByIds`) and in
    `SqliteFileResourceRepository` state that it is *"off by default per connection"*, which is wrong
    for this build.
  - No ADR or convention document decides enforcement either way. The earliest code (`51c5ec3d`,
    `TruncateDataAsync`) turned it off for a delete and back on afterwards, which reads as an assumption
    that it was on. Later comments assumed the opposite.
  - Migrations and `RebuildAsync` turn it off on purpose (`DatabaseInitializer.cs` around lines 1652 to
    1718), and ADR 014 keeps dangling references in the audit-trail tables. Decision E.

---

## Only what is documented (sqlite.org, read verbatim 2026-11-14)

Pages read: `fileformat.html`, `pragma.html`, `wal.html`, `foreignkeys.html`, `uri.html`, `dbpage.html`,
`howtocorrupt.html`. Each fact below is either quoted there, or recorded as not documented together with
the worst case the design assumes. ADR 028 (step 4) carries this list as its SQLite-specific section, with
the URLs.

### Documented, and relied on

- **Header** (`fileformat.html` §1.3):
  - 100 bytes, beginning with the 16-byte string `SQLite format 3\0`.
  - The page size is 2 bytes big-endian at offset 16, a power of two from 512 to 32768, or 1 for 65536.
    Any other value violates the documented format.
- **The in-header database size** (offset 28) is valid only if it is non-zero and the change counter
  (offset 24) *"exactly matches"* the version-valid-for number (offset 92). Otherwise the size is
  *"computed by looking at the actual size of the database file"*. An invalid in-header size is therefore
  documented behaviour, not a fault.
- **`quick_check`** is like `integrity_check`, except that it *"does not verify UNIQUE constraints and
  does not verify that index content matches table content"*.
  - `integrity_check` reports, among others, *"Missing or surplus index entries"*.
  - Neither finds foreign key errors: *"PRAGMA integrity_check does not find FOREIGN KEY errors."*
  - A healthy result is a single row, `ok`.
- **`foreign_key_check`** reports the foreign key constraints that are violated, one four-column row each,
  with an optional table argument. The documentation states this without any condition on the
  `foreign_keys` setting.
- **The WAL header** (`fileformat.html` §4.1): 32 bytes, eight big-endian 32-bit values. In order:
  - magic `0x377f0682` or `0x377f0683`;
  - file format version 3007000;
  - database page size;
  - checkpoint sequence number;
  - salt-1 and salt-2;
  - checksum-1 and checksum-2, on the first 24 bytes of the header, computed as §4.2 defines. The byte order
    for the checksum follows the magic number.
- **A `-wal` is part of the database** (`wal.html` §4): *"The WAL file is part of the persistent state of
  the database"*, and it *"should be kept with the database if the database is copied or moved"*.
  Otherwise *"transactions that were previously committed to the database might be lost"*.
- **Opening a database recovers its `-wal`:** *"the first new connection to open the database will start a
  recovery process"*. A read in WAL mode uses the `-wal`.
- **A read-only open of a WAL database** (`wal.html` §5, SQLite 3.22.0 and later) works when the `-shm`
  and `-wal` already exist and are readable, when the directory is writable, or when the open uses
  `immutable`.
- **`immutable`** (`uri.html`) *"skips all file locking and change detection"*. If the file changes
  anyway, *"SQLite might return incorrect query results and/or SQLITE_CORRUPT errors"*.
- **`sqlite_dbpage`** can write any page, but it needs the compile-time option `SQLITE_ENABLE_DBPAGE_VTAB`
  and is *"disabled by default"*.

### Not documented, and the worst case assumed

- **What an `immutable` open does with an existing `-wal`.** *Worst case:* it ignores the `-wal`, so a
  check of the main file alone misses committed transactions after a crash. So the query layers never run
  on an `immutable` open while the `-wal` holds data (decision D).
- **What SQLite does with a `-wal` whose header is invalid.** *Worst case:* it silently discards the
  `-wal`, losing committed transactions. So an invalid `-wal` header fails the Journal layer, which means
  the file is not a sound copy.
- **Whether a file longer or shorter than the in-header size is valid.** Neither case is documented, so the
  Format layer makes no claim from the file's length. *Worst case:* a truncation is caught only by a later
  layer, or not at all. Whether pages are missing is the Pages layer's documented question, so a truncated
  file is expected to fail there, and its test (row 3) confirms it against a fixture whose in-header size is
  valid. If the in-header size is invalid, SQLite takes the size from the file itself, and no documented
  check then guarantees that a truncation is detected. That limit is recorded in ADR 028.
- **Whether `foreign_keys` can be set on a read-only connection.** It is not needed: `foreign_key_check`
  is documented without a condition on that setting (above), so the check never sets it.
- **Whether a database the application itself produced has dangling references.** Enforcement depends on
  an undocumented compile option of the bundled library. It is off on purpose during migrations and
  Rebuild, and it may have differed under earlier library versions. *Worst case:* a database may hold
  dangling references the application wrote itself. Decision E.
- **A documented way to damage a file deliberately.** `howtocorrupt.html` describes accidental causes only.
  `writable_schema` warns of corruption but documents no procedure, and `sqlite_dbpage` is not compiled in
  by default. So every test fixture builds its damage by writing bytes that the documented file format
  defines (step 6), never by relying on how SQLite happens to react to a misuse.

---

## How it is built

### The engine-neutral contract (`Quotinator.Data`)

- **`IDatabaseCheck`**: one implementation per engine, one call per database.
  `Task<DatabaseCheckResult> CheckAsync(DatabaseCheckDepth depth)`. It writes nothing to the database, and
  leaves the file and every sidecar exactly as it found them.
- **`DatabaseCheckResult`**: the database's name, plus one `DatabaseCheckLayerResult` per layer in order.
  It also carries `FirstFailure` and `IsSound`. ADR 016's `Result` suffix applies, because it is an
  in-process outcome.
- **`DatabaseCheckLayerResult`**: the layer, its status, and an optional `Finding`. The `Finding` is the
  engine's own diagnostic text, logged and shown verbatim. It is never parsed and never used to decide
  anything.
- **Enums** in `Quotinator.Data/Enums/` (ADR 016):
  - `DatabaseCheckLayer`: `Storage`, `Format`, `Journal`, `Pages`, `Indexes`, `References`, `Schema`.
    These name what is checked, not how.
  - `DatabaseCheckLayerStatus`:
    - `Passed`;
    - `Failed`;
    - `Inconclusive`: the layer could not be run soundly (decision D), which is never treated as a pass;
    - `NotRun`: an earlier layer failed or was inconclusive;
    - `Skipped`: excluded by the `Fast` depth.
  - `DatabaseCheckDepth`: `Full`, `Fast`.
- **What each layer means is engine-neutral:**

  | Layer | What it answers | Depth |
  |---|---|---|
  | Storage | Is the file there, can it be read, and can its directory be written? | Fast |
  | Format | Is it a database of this engine, by its documented header? | Fast |
  | Journal | Is the engine's write-ahead file, if any, one it documents as valid? | Fast |
  | Pages | Can every page be read, and are records well-formed? | Fast |
  | Indexes | Do indexes agree with their tables, and do unique constraints hold? | Full only |
  | References | Does every declared reference point at a row that exists? | Full only |
  | Schema | Does the schema match what its version defines (#305)? | Fast |

  A layer an engine has no counterpart for reports `Passed` with a `Finding` saying it does not apply.
  It is never left out, so every engine's report has the same shape.
- **Nothing outside the implementation names the engine.** The Api layer, the guidance text, the
  endpoint's response and the setting speak in layers. A guard test holds this (verification row 16).

### The SQLite implementation

`SqliteDatabaseCheck` maps each layer to SQLite:

| Layer | How SQLite checks it |
|---|---|
| Storage | File system calls, plus `DirectoryWriteProbe` |
| Format | The magic string and a page size the format allows, read as bytes from the 100-byte header |
| Journal | When a `-wal` of non-zero length exists: its length is at least 32 bytes, and its magic, version, page size (equal to the database's) and header checksum are valid |
| Pages | `PRAGMA quick_check` |
| Indexes | `PRAGMA integrity_check`, whose failures quick_check has not already reported |
| References | `PRAGMA foreign_key_check` |
| Schema | The existing #305 comparison, through `IIntegrityCheckedDatabase` |

- A `-wal` page size different from the database's is treated as a `-wal` that does not belong to this
  database. Nothing documents that case, so the worst case applies.
- **Where the query layers run.** When there is no `-wal`, or it is empty, they run on an `immutable`
  read-only open of the main file, as #482's do. When the `-wal` holds data, they run on a copy of the
  database and its sidecars in a temporary folder, opened normally, so SQLite recovers the copy's `-wal`.
  `wal.html` documents both that copying and that recovery. The originals are never opened.
- **The temporary folder is a reserved folder inside the data directory, not the system temporary
  folder.** The add-on's AppArmor profile (`addon/apparmor.txt`) grants `/tmp/** rw` with no lock
  permission (`k`), and SQLite locks the files it opens (the #294 comment in `SqliteConnectionFactory`).
  The data directory has `rwk`. If the folder cannot be written, the copy cannot be made (decision D).
- Every engine-specific fact in the code cites its sqlite.org page in a comment.

### Where the check is called

1. **Startup**, before anything opens the database for writing, on both databases, at the depth
   `Quotinator:DatabaseStartupCheck` selects (`Full` by default). `InitialiseAsync` returns the failed
   check as an outcome on `DatabaseOperationResult` (ADR 022), and `StartupDegradationReasons.For(...)`
   chooses the reason. The changelog database's failure keeps its fallback to the JSON files and never
   degrades the main database (#309, ADR 018).
2. **A Reset**, always at `Full`, replacing `IsDatabaseFileReadable()`. If the file is not a sound copy
   (Storage could not read it, or Format, Journal, Pages or Indexes failed), it is set aside. If only
   References or Schema failed, the file is backed up (ADR 025). An inconclusive check is decision D.
3. **`GET /api/v1/admin/database/integrity`**, always at `Full`: every layer for every database, with
   later layers marked `NotRun` rather than the endpoint throwing.

The `catch … when` on codes 14 and 8 (#326) and the generic catch both stay as backstops for what no layer
foresaw. Neither is interpreted to decide the database's state (ADR 027).

### DRY and SOLID (developer direction, 2026-11-14)

- **One check, three callers.** Startup, a Reset and the endpoint call the same `IDatabaseCheck`.
  `IsDatabaseFileReadable()` is deleted, not kept beside it.
- **One mapping from outcome to reason.** `StartupDegradationReasons.For(...)` replaces the chain of `if`
  blocks in `Program.cs`. It moves the #348, #350 and #305 cases there with their order and text
  unchanged, and adds this issue's cases. #457 then adds its own case first.
- **Open/closed:** each layer is a separate class behind an internal `IDatabaseCheckLayer`, so adding a
  layer, or replacing an engine's implementation of one, touches no other layer. The order lives in one
  list.
- **Dependency direction:** `Quotinator.Data` returns outcomes and knows no wording (ADR 004), and the Api
  layer's `*Guidance` classes turn them into text.
- **Interface segregation:** `IDatabaseCheck` is a new, narrow interface. `IDatabaseInitializer` and
  `IIntegrityCheckedDatabase` are not widened to carry it.

---

## Decisions

Each was raised per `process.md`, Planning step 3. **All five were accepted as recommended (developer,
2026-11-14).** F was missed in planning and decided by the developer at step 8 (2026-11-15).

- **A. The engine-neutral contract above** (developer direction, 2026-11-14).
  - *Recommended:* layers named for **what** is checked, an engine's diagnostic carried only as verbatim
    `Finding` text, and a guard test that no type the Api layer sees comes from `Microsoft.Data.Sqlite`.
    Replacing the engine then means writing a new `IDatabaseCheck`, mapping its documented checks to the
    same seven layers, and recording its own undocumented cases and their worst case in ADR 028. Nothing
    above `Quotinator.Data` changes.
  - *Alternative:* name the layers after SQLite's checks. That is simpler today, but every reason, the
    endpoint and the setting would then speak SQLite.
- **B. How this relates to #305's integrity check and endpoint.** *Recommended:*
  - #305's schema comparison becomes the Schema layer, and `IIntegrityCheckedDatabase` stays as the
    provider it calls.
  - The endpoint keeps its route and its existing fields, and adds a `layers` array per database plus an
    `isSound` flag. Adding fields breaks no client, and renaming the route would.
  - `isIntact` keeps its #305 meaning (the schema matches) and is documented as the Schema layer's result.
  - *Alternative:* a new endpoint for the layered report, leaving #305's as it is. Two endpoints would then
    answer overlapping questions.
- **C. Whether a Storage finding stops the later layers.** The issue says the first failure stops
  everything. But a directory that cannot be written is not a fault in the file, and every later layer
  only reads.
  - *Recommended:* in Storage, *the file cannot be read* stops the check. *The directory cannot be
    written* is reported as a failure, but the reading layers still run. Startup's reason is still the
    Storage one, which keeps #326's behaviour, and the report says whether the file itself is sound.
    #456 and #491 both need that.
  - *Alternative:* follow the issue literally, so that an unwritable directory reports every other layer
    `NotRun`.
- **D. A `-wal` holding data when the temporary copy cannot be made.** By the worst case above, the main
  file alone cannot be checked while the `-wal` holds data, so the copy is required, not optional. The copy
  needs free space for one copy of the database, which after a clean shutdown it never does. What remains
  open is what happens when the copy fails: no space, or no writable temporary folder.
  - *Recommended:*
    - The query layers report `Inconclusive`, with the reason in their `Finding`.
    - An inconclusive check is never a pass. At startup it degrades with its own reason: the database
      could not be checked, what to free, and that a retry (#492) or a restart then checks it.
    - A Reset treats it as not a sound copy, because it cannot show the file is one, so the file is set
      aside rather than backed up. Setting aside moves the file, so it needs no extra space.
  - *Alternative:* fall back to the main file alone and say so in the `Finding`. That accepts the very
    blind spot the documentation leaves open.
- **E. Whether a References failure degrades startup.** By the worst case above, a database the
  application produced itself may fail this layer, because enforcement rests on an undocumented compile
  option, is turned off during migrations and Rebuild, and ADR 014 keeps dangling references on purpose.
  A healthy installation would then degrade on upgrade.
  - *Recommended:* the References layer runs, and its failure:
    - is logged at Warning, kept in the startup check on `DatabaseHealthState`, and reported by the
      endpoint and #491;
    - makes a Reset back the file up;
    - does **not** degrade startup.

    A follow-up issue, #493, makes enforcement explicit on every connection, through the documented
    `Foreign Keys=True` connection-string keyword rather than the library's compile option. It also
    settles ADR 014's declared keys and corrects the comments that say enforcement is off. After that, a
    violation can only come from damage, and References can degrade startup.
  - *Alternative:* degrade on any violation, as the issue says, and accept that existing installations
    may degrade on upgrade.
- **F. Which state of the database startup validates** (developer, 2026-11-15, raised at step 8). The plan
  placed the check "before anything opens the database for writing" without walking each startup path, and
  step 7's Schema layer compares against this build's final schema. A database that needs migrating would
  then fail Schema on every upgrade. Decided: **a database is validated in the state it starts in.**

  | Startup path | Before anything is written | After creating or migrating |
  |---|---|---|
  | No database | Storage alone: there is no file yet | The check runs on the new database, before seeding |
  | Old database | The check runs; a failure in Storage to Indexes stops startup. A Schema difference is expected and does not | The check runs again; its result is startup's, and a Schema failure degrades startup as #305 does |
  | Up-to-date database | The check runs, and its result is startup's | — |

  ADR 028's *Who calls it* section records the same. **Why it was open:** the plan stated where startup
  calls the check but never traced the three startup paths against each layer, and step 7 did not question
  what "the schema its version defines" means before migrations have run. Decisions A to E were the
  questions identified; this one was missed in planning.

---

## Steps

### 1. Confirm decisions A to E

**Status:** ✅ Done

All five were accepted (developer, 2026-11-14). Decision E's follow-up was filed as #493 the same day.

### 2. Measure the cost of each layer

**Status:** ✅ Done

Measure the time each layer takes on `quotinatordata.db` seeded with the bundled content, and on
`quotinatorchangelog.db`, together with each file's size and the time a temporary copy takes (issue
requirement 10). Record the date and the commit here. This is the only measurement in the plan, and
nothing in the design depends on it.

**Measured 2026-11-14 at `1ab5fd1f`.** The code is unchanged since `5285a211`.

**These figures are indicators, not limits, and they will vary (developer, 2026-11-14).** They exist for two
reasons: to show where a problem may lie when a check is slow, and to help tell the user how far the
startup sequence, or a running integrity check, has got. Nothing may test against them or treat them as a
threshold.

- **How the databases were made:** `quotinator:local` was built from that commit and run on an empty data
  directory. It was stopped after the closing startup banner with a 60-second grace period (exit 0), which
  left no `-wal` behind.
- **How the timings were taken:** a scratch `dotnet-script` used the pinned Microsoft.Data.Sqlite 10.0.14
  and SQLitePCLRaw.lib.e_sqlite3 3.53.3. Each run took a fresh copy of the database, opened it `immutable`
  and read-only, and ran each `PRAGMA` to the end of its result. Medians of five runs on the developer's
  Windows machine, in milliseconds:

| | `quotinatordata.db` (4,665,344 bytes) | `quotinatorchangelog.db` (1,171,456 bytes) |
|---|---|---|
| Copy of the main file | 3.35 | 2.89 |
| Header read (Format) | 0.32 | 0.33 |
| Open, `immutable` | 0.38 | 0.37 |
| `quick_check` (Pages) | 7.64 | 1.88 |
| `integrity_check` (Indexes) | 14.42 | 1.31 |
| `foreign_key_check` (References) | 2.08 | 0.19 |

- **Results:** every check returned clean on both seeded databases: `quick_check` and `integrity_check`
  reported `ok`, and `foreign_key_check` returned no rows.
- **Not measured:**
  - Storage and Journal, which are a few file-system calls and at most 32 bytes read.
  - Schema, which is #305's existing comparison, already run at startup today.
  - The add-on's arm64 hardware, where every figure will be slower. A full check at startup costs
    milliseconds here, so the `Fast` depth exists for larger databases and slower storage, not for the
    bundled content.
- **What one run showed about shutdown, not the check:** stopping the container while the seed was still
  importing hit Docker's 10-second grace period, and the process was killed (exit 137). That left a
  4,136,512-byte `-wal` beside the database. So a stop at an awkward moment leaves a `-wal` holding data,
  the case decision D covers. A copy then costs disk space for the database plus its `-wal`.

### 3. Add the touched files to the IDE0008/IDE0090 scoped lists

**Status:** ✅ Done

Every file created or edited goes into `.editorconfig`'s scoped sections the moment it is first touched.

**Done 2026-11-15.**

- **Already in both lists:** every existing file the plan edits except one:
  - `DatabaseInitializer.cs`, `ChangelogDatabaseInitializer.cs`, `DatabaseOperationResult.cs`, `DatabaseOptions.cs`, `DirectoryWriteProbe.cs`;
  - `Program.cs`, `AdminEndpoints.cs`, `SchemaIntegrityGuidance.cs`, `KnowledgebaseLinks.cs`;
  - `DatabaseIntegrityDto.cs`, `DatabaseIntegrityResponse.cs`, `TempDatabase.cs`;
  - and the tests `DatabaseInitializerTests`, `StartupResilienceTests`, `AdminDatabaseIntegrityEndpointTests`, `StartupSummaryLoggerTests` and `RepositoryStructureTests`.
- **Added to both lists, before they exist:** the files the plan names.
  - The contract and the implementation go in a new `Quotinator.Data/Database/Checks/` folder, namespace `Quotinator.Data.Database.Checks`: `IDatabaseCheck.cs`, `DatabaseCheckResult.cs`, `DatabaseCheckLayerResult.cs`, `SqliteDatabaseCheck.cs`.
  - The three enums go in `Quotinator.Data/Enums/`.
  - Also `Quotinator.Api/Startup/StartupDegradationReasons.cs`.
  - The tests: `Quotinator.Data.Tests/Database/Checks/SqliteDatabaseCheckTests.cs`, `Quotinator.Api.Tests/Startup/StartupDegradationReasonsTests.cs` and `Quotinator.Api.Tests/Solution/RepositoryStructureTests.DatabaseCheck.cs`.
- **Added when first touched, by the step that touches them:**
  - `StartupSummaryLogger.cs`, which holds seven `var` declarations, at step 10;
  - each internal layer class, the fixture helper in `Quotinator.Data.Testing` and any new `*Guidance` class, under names their steps choose.

### 4. Write the ADR before the code

**Status:** ✅ Done

**Done 2026-11-15.**

- **ADR 028** is written and Accepted (dated 2026-11-14, the day decisions A to E were accepted), and it is
  in the ADR index and the solution.
  - Its section on ADR 003 explains why one engine-neutral contract with a single implementation, and
    nothing to choose between engines, stays inside design goal 3's ban on provider abstractions.
  - A *Known non-compliance* section names what the code does until this issue lands.
  - Its sqlite.org anchors were checked against the pages' own contents links.
- **ADRs 003, 025 and 027** point to ADR 028 instead of naming `PRAGMA quick_check`. ADRs 025 and 027 now
  speak of *a sound copy* rather than *a file SQLite cannot read*, and each lists #490 in its header.
- **`CLAUDE.md`** points to ADR 028 at the end of *No exception-based migration recovery*. The guard
  `ClaudeMdResetPassagesPointToTheirAdrs` and `EveryAdrIsIndexed` pass, as do all 60
  `RepositoryStructureTests`.

- **ADR 028, *A database is checked in layers, cheapest first*:**
  - the engine-neutral principle and the seven layers;
  - the first failure as the result, and an inconclusive layer never counting as a pass;
  - the rule that only documented engine behaviour is relied on, with the worst case assumed elsewhere;
  - every layer leaving the files as it found them;
  - the depth setting and what `Fast` gives up;
  - who calls the check, and which way each layer's failure sends a Reset;
  - then a section clearly headed as SQLite-specific: each layer's documented basis with its URL, and every
    undocumented case with the worst case assumed (the list above).
- **ADR 003** design goal 5, **ADR 025** and **ADR 027**: replace each mention of `PRAGMA quick_check`
  with a pointer to ADR 028, and mark every remaining engine-specific recommendation as such.
- **`CLAUDE.md`** points to ADR 028 under *No exception-based migration recovery* and restates none of it.

### 5. Draft the contract with no behaviour

**Status:** ✅ Done

`IDatabaseCheck`, `DatabaseCheckResult`, `DatabaseCheckLayerResult`, the three enums, and the new outcome
on `DatabaseOperationResult`, all with XML docs. No behaviour yet, so that the tests in step 6 compile and
fail on their assertions.

**Done 2026-11-15.**

- **Files:**
  - `Quotinator.Data/Database/Checks/` holds `IDatabaseCheck`, `DatabaseCheckResult` and
    `DatabaseCheckLayerResult`, following `Quotinator.Data/Schema/`'s pattern of a contract beside its
    engine implementation.
  - `Quotinator.Data/Enums/` holds `DatabaseCheckLayer`, `DatabaseCheckLayerStatus` and
    `DatabaseCheckDepth`.
- **The contract's shape:**
  - `IDatabaseCheck` carries `DatabaseName` and `CheckAsync(DatabaseCheckDepth)`. It takes no cancellation
    token, matching `IIntegrityCheckedDatabase.CheckIntegrityAsync`.
  - `DatabaseCheckResult` carries `DatabaseName`, `Depth` and `Layers`. `FirstFailure` (the first layer
    that failed or was inconclusive) and `IsSound` are one-line derivations, not layer behaviour.
- **Added beyond the plan:** `DatabaseCheckLayerResult.Elapsed`, how long a layer took. It serves the
  developer's note on step 2: the figures help locate a slow layer and report progress. It is documented
  as an indicator, never a threshold.
- **`DatabaseOperationResult`** gains `StoppingCheck` and the factory `StoppedByCheck(check)`, following
  `NotIntact()`'s shape.
- **Moved to step 6:** the `SqliteDatabaseCheck` skeleton. Its constructor is shaped by what the tests
  construct, so it is written with them, and its `CheckAsync` stays unimplemented until step 7.
- `dotnet build --configuration Release`: 0 warnings, 0 errors.

### 6. Write the failing tests and confirm them red

**Status:** ✅ Done

Every unit test in *Verification* rows 1 to 17 is written and run against the code before step 7. Each
must fail for the reason it names.

**Fixtures damage files only through the documented format**, in a shared helper in
`Quotinator.Data.Testing`:

| Fixture | How the damage is built |
|---|---|
| Not a database | A file without the header's magic string |
| Truncated | A database whose in-header size is valid (non-zero, change counter equal to version-valid-for), cut to half its length |
| Invalid `-wal` | A `-wal` with a wrong magic or header checksum, per §4.1 and §4.2 |
| Damaged page | A b-tree page whose page-type byte holds a value the format forbids (§1.6) |
| Index that disagrees with its table | A value in an index's leaf cell rewritten in place in the documented record format, with the index located through its `rootpage` in the schema table |
| Dangling reference | A row inserted with enforcement off, which is the documented default |

Each fixture asserts its own precondition (for example, that the in-header size is valid) before the test
uses it.

**Done 2026-11-15.**

- **The fixture helper** is `Quotinator.Data.Testing/Database/DocumentedFormatFixtures`. Each method cites
  the `fileformat.html` section whose bytes it writes, and throws if its precondition does not hold:
  - `WriteNotADatabase`, `TruncateToHalf`, `DamageRootPageType`, `RewriteLastIndexKey`;
  - `CorruptWalMagic`, `CorruptWalHeaderChecksum`;
  - `WriteWithoutForeignKeyEnforcement`. It turns enforcement off explicitly, because the bundled library
    turns it on by default;
  - `Create`, `Execute`, `CreateWithWalHoldingData` and `AddWalHoldingData`, which build the healthy and
    `-wal`-holding databases the damage starts from. A `-wal` holding data is made by copying the files
    while a connection is still open, because closing the last connection checkpoints and deletes it.
- **The signatures, with no behaviour:**
  - `SqliteDatabaseCheck(databaseName, databasePath, workFolder, expectedSchemaScripts, canCreateFileIn)`.
    `canCreateFileIn` defaults to `DirectoryWriteProbe.CanCreateFileIn`, and a test supplies its own,
    because not every platform lets a test make a directory unwritable. `WorkFolderName` is
    `.database-check`. `CheckAsync` reports every layer `NotRun`, a default no test expects, until step 7.
  - `StartupDegradationReasons.For(initialisation, overshootDetected, dataVersion, appVersion, integrity)`
    throws `NotImplementedException` until step 8. Its first draft returned `null`, which is the outcome
    two of its tests expect, so they were green without ever being red; throwing makes every one red. It
    holds the texts `DataDirectoryNotWritable` and `InitialisationFailed`, copied from `Program.cs`, which
    keeps its own until step 8 moves the cases, and the new `CheckInconclusive` text.
  - `DatabaseOptions.StartupCheckDepth`, defaulting to `Full`, which step 10 binds to the setting.
- **The tests:**
  - `SqliteDatabaseCheckTests` covers rows 1 to 12;
  - `DatabaseInitializerTests.DatabaseCheck.cs` covers rows 13 and 14, against real migrated databases
    with extra objects added and damaged;
  - `StartupDegradationReasonsTests` covers row 15;
  - `RepositoryStructureTests.DatabaseCheck.cs` covers row 16;
  - `StartupResilienceTests` covers row 17.
- **Positive controls**, per the testing policy's *Every test proves the positive result as well as the
  negative*: `SqliteDatabaseCheckTests.Check_HealthyDatabase_PassesEveryLayer`,
  `DatabaseInitializerTests.InitialiseAsync_HealthyDatabase_SucceedsCarryingAPassedCheck`,
  `StartupResilienceTests.Startup_RestartOverAnIntactDatabase_KeepsAPassedStartupCheck` and
  `StartupDegradationReasonsTests.For_Success_IsNoReason`. For the set-aside Resets,
  `ResetAsync_DanglingReference_BacksTheFileUp` is the control: a sound copy is still backed up.
- **Confirmed red:** every fixture built without tripping its precondition, and every test fails where it
  names, on its assertion or on the `NotImplementedException` of a signature. Two tests describe behaviour
  that already holds before this issue, so no run against the code before the issue can make them red. Each
  was proven by mutation, per *Red first means signatures first*, then reverted with `git checkout`:
  - `RepositoryStructureTests.TheDatabaseCheckContract_NamesNoEngineType`: a `SqliteConnection` property
    added to `DatabaseCheckResult` made it fail, naming `DatabaseCheckResult.Leak`.
  - `DatabaseInitializerTests.ResetAsync_DanglingReference_BacksTheFileUp`: `IsDatabaseFileReadable`
    returning `false` for every existing file made it fail on the backup count.
- **`DatabaseHealthState.RecordStartupCheck` is implemented here**, because row 17 needs its result.
  Its first draft was written together with its signature, so its two tests were never red: the trap *Red
  first means signatures first* names. It was put back to `throw new NotImplementedException()`, both
  tests ran red, and then it was implemented and both ran green.
- **Added beyond the plan:** `Check_NoDatabaseFileYet_PassesWithNothingToCheck`. A first start has no
  database file, and a Reset creates one, so neither may fail the check. Storage passes, and each later layer
  passes with a finding saying there was nothing to check, so the report keeps its shape. This follows the
  contract's rule for a layer that does not apply.
- **A test is never validated through a log (developer, 2026-11-15).** Where a test needs a result, the
  result is built now. Row 17 needs the failing layer of a degraded start, and a References failure that
  leaves startup healthy, so both are now results:
  - `DatabaseOperationResult.Check` replaces step 5's `StoppingCheck`. It carries the check that ran,
    whatever it found, so a References failure travels on a successful result (`Success(check: …)`).
  - `DatabaseHealthState.StartupCheck`, set through `RecordStartupCheck`, keeps the main database's
    startup check. Recording it changes no health, and a Reset does not clear it. Its tests are in
    `DatabaseHealthStateTests`; step 8 makes `Program.cs` record it.
  - Row 17's tests read the failing layer from `StartupCheck`, so they keep the plan's intent under the
    names `Startup_IndexDisagreesWithItsTable_DegradesNamingTheLayer` and
    `Startup_DanglingReference_StaysHealthyAndReportsTheLayer`. The operator's reason for a broken index is
    still the generic one until #457 (step 8).
- **Deviation for the developer to confirm:** row 13 uses an index that disagrees with its table, not a file
  that is not a database. Today a file that is not a database makes `InitialiseAsync` throw, so the test
  would fail on an exception rather than on its assertion. A broken index is opened and written to today,
  which the test observes as a `-wal` and `-shm` appearing beside the file.
- **How row 17's unwritable directory is observed:** the database's `-wal` and `-shm` already exist and are
  writable, so SQLite opens the database without complaint, and today startup is healthy. Only the check's
  directory probe can detect it. Denied with a Windows deny ACE, or owner read and execute elsewhere, as
  the existing keys-directory test does.

### 7. Implement the SQLite layers

**Status:** ✅ Done

`SqliteDatabaseCheck` and one internal class per layer, in the order of the table, as decided in A, C, D
and E. Register it in DI per database through the keyed connection factories that already exist.

**Done 2026-11-15.**

- **One class per layer**, behind the internal `IDatabaseCheckLayer`, in `Quotinator.Data/Database/Checks/`:
  `StorageLayer`, `FormatLayer`, `JournalLayer`, and the four that read through SQLite, `PagesLayer`,
  `IndexesLayer`, `ReferencesLayer` and `SchemaLayer`. Those four share `QueryLayer`: no file means nothing to
  check, no sound connection means inconclusive, and SQLite rejecting the file is the layer's failure, its
  `SqliteException` logged as handled (ADR 022). `LayerResults` words the shared cases.
- **`SqliteCheckSubject`** carries what earlier layers found (whether the file is readable, its page size,
  whether the `-wal` holds data) and owns the one connection the query layers share. Without a `-wal` holding
  data it opens the main file `immutable` and read-only; with one it copies the database and its `-wal` into
  a new folder under the work folder, opens the copy normally so SQLite recovers it, and removes the copy
  when the check ends. A copy that cannot be made makes every query layer inconclusive.
- **`SqliteDatabaseCheck`** holds the order and the rules connecting the layers: the first failure stops the
  check, except a Storage failure on a readable file (decision C); an inconclusive layer never stops it; at
  `Fast`, Indexes and References are reported skipped. Each layer's `Elapsed` is measured. It gained an
  optional `ILogger<SqliteDatabaseCheck>` for the handled exceptions.
- **The pragmas** live in `Sql.SchemaMetadata`: `QuickCheck`, `IntegrityCheck`, `ForeignKeyCheck`.
- **A zero-length file fails Format** as shorter than the header. Nothing in `fileformat.html` or
  `c3ref/open.html` documents how SQLite treats a zero-byte file, and the developer confirmed at step 8 that it
  is likely a broken file unless another process is accessing it.
- **Registered in DI** in `Program.cs`: the main database's check unkeyed, the changelog's keyed by
  `DatabaseConnectionKeys.Changelog`, each with the expected schema its initializer's own migration path
  defines and the work folder `{dataDir}/.database-check`. Nothing resolves them until step 8.
- **`DatabaseNames`** holds `main` and `changelog`, which the two initializers, `NoOpDatabaseInitializer`
  and the two registrations now share instead of each typing the literal.
- **Result:** all 22 `SqliteDatabaseCheckTests` pass. The other tests step 6 wrote stay red, because they
  belong to steps 8 and 9.

### 8. Run the check at startup

**Status:** ✅ Done

- `InitialiseAsync` runs the check right after `MigrateFilenameIfNeeded()`, before the database is opened
  for writing, and returns a failure as an outcome. Per decision F, a Schema failure there does not stop
  it; after creating or migrating, the check runs again and that result is startup's.
- **Red-first tests decision F adds** (`DatabaseInitializerTests`):
  `InitialiseAsync_NoDatabase_ChecksTheCreatedDatabase`,
  `InitialiseAsync_OldDatabase_KeepsTheCheckTakenAfterMigrating`,
  `InitialiseAsync_MigratedSchemaStillDiffers_ReturnsTheSchemaOutcome`.
- The changelog initializer runs it too, and keeps its fallback to the JSON files on a failure.
- Create `StartupDegradationReasons` and move the #348, #350 and #305 cases into it unchanged.
- Add this issue's reasons:
  - Storage, reusing #326's text;
  - an inconclusive check (decision D);
  - References, only if decision E's alternative is chosen.

  Each new reason gets a Knowledgebase entry and a `KnowledgebaseLinks` constant (issue requirement 9).
- Layers Format to Indexes keep the generic reason until #457.

**Done 2026-11-15.**

- **The main database** (`DatabaseInitializer.InitialiseAsync`): the check runs after
  `MigrateFilenameIfNeeded()` at `DatabaseOptions.StartupCheckDepth`. `StartupDatabaseCheck.Stops` stops
  startup on any failing or inconclusive layer except References (decision E) and Schema (decision F, judged
  after any migration by #305's comparison). After a creation or a migration, the `-wal` is checkpointed
  with `Sql.Schema.CheckpointTruncate`, so the check reads the main file directly, and the check runs again.
  Every outcome carries the check that applies: `StoppedByCheck`, `NotIntact(check)` or
  `Success(check: …)`.
- **The check is a required constructor dependency** of `DatabaseInitializer`, `QuotinatorDatabaseInitializer`
  and `ChangelogDatabaseInitializer` (keyed). A first draft made it optional, with a fallback built from the
  options; the existing guard `InitializerConstructors_DoNotMakeAServiceDependencyOptional` (#302) failed on
  it, which is the guard's purpose. The guard now covers the changelog initializer too, proven by mutation:
  the changelog's check made optional failed it, naming the parameter, and was reverted.
- **Tests pass a check explicitly.** A test on a real file passes the real check through
  `TestDatabaseChecks.ForMain` or `.ForChangelog` (`Quotinator.Data.Testing`), built as `Program.cs` builds the
  registered one. A test on a shared-cache in-memory database, which the SQLite check cannot read, passes
  `NoOpDatabaseCheck`, whose every layer carries a finding saying it was not checked.
- **A zero-byte file stays a Format failure** (developer, 2026-11-15): it is likely a broken file unless
  another process is accessing it. Tests that meant "start with no database" had used `new TempDatabase([])`,
  which leaves a zero-byte file. They now use `TempDatabase.WithoutFile()`, a path with no file, which SQLite
  creates on first open as documented (`c3ref/open.html`). Not every such test needed it:
  `AppVersionTrackerTests` never hands its database to an initializer.
- **The changelog database** (`ChangelogDatabaseInitializer.InitialiseAsync`) now returns a
  `DatabaseOperationResult` and follows decision F the same way, at `Full`. `Program.cs` falls back to the JSON
  files whenever it does not succeed, a stopping check included.
- **`StartupDatabaseCheck`** (`Quotinator.Data/Database/Checks/`) holds the stop rule and logs the result
  for operators: Information when every layer passed, Error for a failing layer, Warning for an
  inconclusive one and for a dangling reference (decision E).
- **`StartupDegradationReasons.For`** is implemented: a check that stopped startup wins, then #348, #350
  and #305 in their old order. `Program.cs` calls it once, records the check on `DatabaseHealthState`, and
  logs the reason through one `LogStartupDegraded`, which replaces three log methods that rendered the same
  `[Server] {Reason}` line. `Program.cs` no longer holds its own copies of the two reason texts.
- **Knowledgebase:** `the-database-could-not-be-checked.md`, linked as
  `KnowledgebaseLinks.TheDatabaseCouldNotBeChecked` from the end of `CheckInconclusive`. Storage reuses #326's
  text and its existing entry.
- **Fixture fix:** `DocumentedFormatFixtures`' root page lookup now opens `immutable`. Read-only alone
  created a `-wal` beside a WAL database it could not remove, which row 13's test found.
- **The changelog's check was implemented before its tests**, the trap *Red first means signatures first*
  names: step 6 had no test for the changelog database. Its three tests in `ChangelogDatabaseCheckTests` were
  then proven by mutation, the check made never to run: all three failed, the first on SQLite's own
  `file is not a database`. Reverted, and all three pass.
- **Result:** decision F's three tests, rows 13 and 15 to 17, and the changelog's three pass. The Reset
  tests of row 14 remain red for step 9.

### 9. Use the check in a Reset

**Status:** ✅ Done

Replace `IsDatabaseFileReadable()`. A Reset always runs at `Full`. A file that is not a sound copy, or whose
check is inconclusive, is set aside. If only References or Schema failed, the file is backed up.

**Done 2026-11-15.**

- `ResetAsync` runs the check at `Full` first, before any storage check, as #482's probe did. The probe,
  `IsDatabaseFileReadable()`, is deleted.
- **`ResetDatabaseCheck.IsSoundCopy`** (`Quotinator.Data/Database/Checks/`) holds the routing: a sound copy is
  one where every layer from Format to Indexes passed. One that failed, was inconclusive, or did not run because
  Storage could not read the file means it is set aside. A References or Schema failure leaves it a sound copy,
  so it is backed up (ADR 025). A directory that cannot be written is left to the backup's own readiness check,
  which already refuses with its own obstacle. A missing file passes every layer, and the Reset creates it.
- **Logged** through a new `LogResetSourceNotASoundCopy`, which names the layer, its status and the engine's
  finding. `LogResetSourceUnreadable` stays for the case the backup copy itself finds, after the check passed.
- **The result carries the check** (`Success(restorePoint, check)`, `NotIntact(check)`), for the endpoint and
  #491 to read.
- **Result:** row 14's three tests that were red pass, and every existing Reset test of an unreadable file,
  #482's, still passes.
- **A limit carried over from #482, not introduced here:** a Reset runs while the application is serving, and
  nothing stops another request writing to the database during the check. The check of a database whose
  `-wal` is empty reads it `immutable`, and `uri.html` says a file that changes under an `immutable` open may
  give incorrect results or `SQLITE_CORRUPT`. The worst case is a sound file judged not sound, which is set
  aside rather than backed up: the file is kept either way, so nothing is lost, but it goes to the
  `unreadable` folder rather than the backup list.

### 10. The setting and the add-on option

**Status:** ✅ Done

- Add `Quotinator:DatabaseStartupCheck` (`Full`/`Fast`) on `DatabaseOptions`, read once at startup and
  shown in the startup banner.
- Add `database_startup_check` to `addon/config.yaml` and `addon-beta/config.yaml`, with translations in
  en, nl and de in both (the `CLAUDE.md` add-on option checklist). Its description names what `Fast` no
  longer detects, and the cost measured in step 2.

**Done 2026-11-15.**

- **`DatabaseStartupCheckSetting`** (`Quotinator.Api/Startup/`) holds the key and parses it case-insensitively,
  since the add-on writes `full` and `fast`. An absent or empty value is the default, `Full`. A value that names
  no depth checks at `Full`, the thorough and safe answer, and `Program.cs` logs a Warning naming it.
  `Program.cs` sets `DatabaseOptions.StartupCheckDepth` from it, and passes it to the changelog initializer,
  whose `InitialiseAsync` now takes the depth.
- **The closing banner** shows `Database check: Full` or `Fast`.
- **Add-on:** `database_startup_check: full`, schema `list(full|fast)`, mapped to
  `Quotinator__DatabaseStartupCheck`, in both channels, with en, nl and de translations in both. The
  description names what `fast` gives up, that a Reset and the integrity check still find it, and the cost:
  about 25 ms on the bundled content, the sum of step 2's layer timings.
- **Two guards built here**, because row 19 names add-on option translation tests that did not exist:
  `EveryAddOnOption_IsTranslatedInEveryLanguage` and `AddOnDatabaseStartupCheckDefault_MatchesTheApplicationDefault`.
  The translation guard was red with the option in the schema and no translations, naming all six files.
- **Tests:** `DatabaseStartupCheckSettingTests`, `StartupSummaryLoggerTests.LogReady_BannerNamesTheStartupCheckDepth`
  (the banner's text is its subject), `StartupResilienceTests.Startup_FastSetting_SkipsIndexesAndStaysHealthyOverABrokenIndex`
  and `ChangelogDatabaseCheckTests.InitialiseAsync_Fast_SkipsIndexesAndReferences`, each red first.
  `Default_IsTheDatabaseOptionsDefault` held before any change, so it was proven by mutation: the options
  default set to `Fast` failed it, and was reverted.
- **Boyscout:** `StartupSummaryLogger.cs` is in the IDE0008/IDE0090 lists, and its seven `var` declarations are
  explicit types.

### 11. Report every layer from the integrity endpoint

**Status:** ⬜ Not started

- Add the `layers` and `isSound` fields per decision B.
- Update the endpoint's `[Description]` and `docs/api-endpoints.md` in the same commit.

### 12. T2 documents

**Status:** ⬜ Not started

- **Two new documents in `startup-and-degradation/`:**
  - a database with a broken index degrades at startup and recovers through a Reset;
  - with `database_startup_check: Fast`, the same database starts healthy, and the integrity endpoint
    still reports the broken index.
- **Re-run `10`** (a data directory that cannot be created), whose detection moves to the Storage layer.
- **Fixtures:** each document's fixture is built with the same documented-format helper as step 6.
- **Canary:** each new document is confirmed red against `quotinator:canary-490`, built from `342e0cfa`.
  Then the container, image and worktree are removed.

### 13. Leave no contradiction in the documentation

**Status:** ⬜ Not started

Search `docs/`, `CLAUDE.md`, the add-on `DOCS.md` files, the Knowledgebase and the endpoint descriptions
for `quick_check`, *reads every page*, and #326's error-code description, and correct each one.

### 14. Boyscout pass and full build

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both 0 warnings and 0 errors.

### 15. T1

**Status:** ⬜ Not started

This is the developer's step. In Visual Studio:

1. Start over a healthy database and confirm the startup time is acceptable at `Full`.
2. Start over a database with a broken index and confirm the degraded reason.
3. Read `/api/v1/admin/database/integrity`.

### 16. T2

**Status:** ⬜ Not started

Run the new documents, `08`, `09`, `10` and `14`, plus the smoke set, against `quotinator:local` built from
this branch. Record each *Observed effect*.

### 17. Changelog

**Status:** ⬜ Not started

Add an `unreleased` entry in `changelog.en.json`, `nl.json` and `de.json`, carrying issue 490. Its highlight
says that the database is checked thoroughly at every start, and that the faster check is an option.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A healthy database passes every layer | Unit test | `SqliteDatabaseCheckTests.Check_HealthyDatabase_PassesEveryLayer` |
| 2 | ❌ | A file that is not a database fails at Format, and no later layer runs | Unit test | `SqliteDatabaseCheckTests.Check_NotADatabase_FailsAtFormatAndRunsNoLaterLayer` |
| 3 | ❌ | A truncated database with a valid in-header size fails at Pages, and no later layer runs | Unit test | `SqliteDatabaseCheckTests.Check_TruncatedDatabase_FailsAtPagesAndRunsNoLaterLayer` |
| 4 | ❌ | A `-wal` with an invalid magic, or header checksum, fails at Journal | Unit test | `SqliteDatabaseCheckTests.Check_InvalidWalMagic_FailsAtJournal`, `…Check_InvalidWalHeaderChecksum_FailsAtJournal` |
| 5 | ❌ | A damaged page fails at Pages | Unit test | `SqliteDatabaseCheckTests.Check_DamagedPage_FailsAtPages` |
| 6 | ❌ | An index that disagrees with its table passes Pages and fails at Indexes | Unit test | `SqliteDatabaseCheckTests.Check_IndexDisagreesWithItsTable_FailsAtIndexes` |
| 7 | ❌ | A dangling reference fails at References | Unit test | `SqliteDatabaseCheckTests.Check_DanglingReference_FailsAtReferences` |
| 8 | ❌ | `Fast` skips Indexes and References, and reports them `Skipped` | Unit test | `SqliteDatabaseCheckTests.Check_Fast_SkipsIndexesAndReferences` |
| 9 | ❌ | Every outcome leaves the file and its sidecars byte-for-byte as they were | Unit test | `SqliteDatabaseCheckTests.Check_AnyOutcome_LeavesTheFileAndSidecarsUntouched` (data-driven over rows 1 to 7 and 11) |
| 10 | ❌ | *(decision C)* An unwritable directory fails Storage, but the reading layers still run | Unit test | `SqliteDatabaseCheckTests.Check_DirectoryNotWritable_FailsStorageAndStillChecksTheFile` |
| 11 | ❌ | A `-wal` holding data is checked through a temporary copy, never through an `immutable` open of the main file | Unit test | `SqliteDatabaseCheckTests.Check_WalHoldsData_ChecksACopyAndLeavesTheOriginalsUntouched` |
| 12 | ❌ | *(decision D)* When the copy cannot be made, the query layers are `Inconclusive`, which is never a pass | Unit test | `SqliteDatabaseCheckTests.Check_CopyCannotBeMade_ReportsInconclusive` |
| 13 | ❌ | Startup returns a failed or inconclusive check as an outcome, without opening the database for writing | Unit test | `DatabaseInitializerTests.InitialiseAsync_LayerFails_ReturnsThatLayersOutcomeWithoutOpeningForWrite` |
| 14 | ❌ | A Reset sets aside a file with a broken index or an inconclusive check, backs up one with a dangling reference, and always checks at `Full` | Unit test | `DatabaseInitializerTests.ResetAsync_IndexDisagreesWithItsTable_SetsTheFileAside`, `…ResetAsync_CheckInconclusive_SetsTheFileAside`, `…ResetAsync_DanglingReference_BacksTheFileUp`, `…ResetAsync_FastSetting_StillRunsEveryLayer` |
| 15 | ❌ | Choosing the startup reason is one mapping: this issue's cases are added, and the #348, #350 and #305 cases keep their order and text | Unit test | `StartupDegradationReasonsTests` (one test per case, plus the first-wins order); the existing #348, #350 and #305 `StartupResilienceTests` stay green |
| 16 | ❌ | *(decision A)* No type the Api layer sees from the check comes from the SQLite library | Unit test | `RepositoryStructureTests.TheDatabaseCheckContract_NamesNoEngineType` |
| 17 | ❌ | Startup detects an unwritable data directory before the database is opened, degrades on a broken index naming the layer in the startup check it keeps (generic reason until #457), and treats a dangling reference as decision E decides, reporting it in the same check | Unit test | `StartupResilienceTests.Startup_DataDirectoryNotWritable_DetectedBeforeTheDatabaseIsOpened`, `…Startup_IndexDisagreesWithItsTable_DegradesNamingTheLayer`, `…Startup_DanglingReference_StaysHealthyAndReportsTheLayer`; `DatabaseHealthStateTests.RecordStartupCheck_KeepsTheCheckWithoutChangingHealth`, `…MarkHealthy_KeepsTheStartupCheck` |
| 18 | ❌ | The integrity endpoint reports every layer, with a failing layer instead of throwing, at `Full` whatever the setting | Unit test | `AdminDatabaseIntegrityEndpointTests.Integrity_CorruptDatabase_ReportsTheFailingLayerInsteadOfThrowing`, `…Integrity_FastSetting_StillReportsEveryLayer` |
| 19 | ❌ | The new strings, the add-on option and the endpoint documentation are complete | Unit test | Existing `TranslationCompletenessTests` and `OpenApiSpecEndpointTests`, plus the add-on option translation tests |
| 20 | ❌ | Live: a broken index degrades at startup and recovers through a Reset | Live (T2) | New `startup-and-degradation/NN`; passes on this build, red on `quotinator:canary-490` |
| 21 | ❌ | Live: with `Fast`, the same database starts healthy, and the endpoint still reports the broken index | Live (T2) | New `startup-and-degradation/NN`; same canary |
| 22 | ❌ | The full build and test suite are clean | Live | `dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`: 0 warnings, 0 errors, all passed |
| 23 | ❌ | T1: startup time at `Full` is acceptable, and a broken index degrades with its reason | Live (T1) | Step 15, confirmed by the developer |
