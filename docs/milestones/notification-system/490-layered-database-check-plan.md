# #490: Check every database in layers, cheapest first, at startup, before a Reset, and on demand

**Status:** In progress (step 18)
**GitHub issue:** #490
**Tiers required:** T1, T2
**Depends on:** none. Blocks #457, #491, #492, #493.

---

## Next action

**Execute step 18, the changelog.** Every earlier step is done: T2 passed on `3205ba49` (step 16), and step 17
is built and verified. Decisions A to E were accepted as recommended on 2026-11-14, and decision E's follow-up
is #493. Step 6's deviation was answered on 2026-11-17: row 13 is proven with both variants. The plan is built
against the code at `342e0cfa` and the sqlite.org documentation as read on that date.

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
  read-only open of the main file, as #482's do. When the `-wal` holds data, they run on an in-memory image
  of the database with the `-wal`'s committed frames applied, opened with `sqlite3_deserialize` (decision D,
  as superseded 2026-11-16). The originals are never opened for writing, and nothing is written to disk.
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
  - **Superseded (developer, 2026-11-16): the check needs no disk space; it is done in memory.** A copy on
    disk made the check depend on room it should never need, and T2 found the consequence: a full volume
    stopped the check and with it every Reset and migration. The query layers now run on an in-memory image,
    built only from what sqlite.org documents:
    - The main file and the `-wal` are read into memory; neither is opened by SQLite or written.
    - The `-wal`'s frames are applied to the main file's pages by `fileformat.html` §4: a frame is valid if
      and only if its salts match the header's and its checksum matches the one computed consecutively from
      the header (§4.1, §4.2); a frame whose second field is not zero is a commit record carrying the
      database's size in pages after the commit (§4.1); and a page takes "the last valid instance of page P
      that is followed by a commit frame or is a commit frame itself" (§4.5). The image is sized to the last
      commit's page count.
    - Header bytes 18 and 19 are set to 1, as `c3ref/deserialize.html` documents: "The deserialized database
      should not be in WAL mode", and the application can set them "to 0x01 … to force the database file
      into rollback mode".
    - The image is opened with `sqlite3_deserialize`, read-only. The interface "is omitted if SQLite is
      compiled with the SQLITE_OMIT_DESERIALIZE option", and nothing documents whether the bundled library
      was, so a test proves it is present.
    - *Not documented, and the worst case assumed:* whether a reader stops at the first invalid frame. The
      image takes only what §4.5 defines, frames valid by §4.1 and covered by a commit, so a frame after an
      invalid one is used only if it is itself valid by the consecutive checksum, which includes the invalid
      frame's content.
    - **What it removes:** the work folder, the room measurement for the copy, `DatabaseStorageFault.InsufficientSpace`,
      the Reset's refusal when the check has no room, and startup's `NotEnoughSpace` reason. A full volume no
      longer stops the check; it stops only what writes, a backup, through the obstacles that already exist.
    - **What it costs:** memory for the database and its `-wal` while the check runs, only when the `-wal`
      holds data.
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
  | Old database | The check runs; a failure in Storage to Indexes stops startup. Schema is skipped, naming the version table, since a difference is certain until the migrations run | The check runs again; its result is startup's, and a Schema failure degrades startup as #305 does |
  | Up-to-date database | The check runs, compares the schema, and its result is startup's | — |

  ADR 028's *Who calls it* section records the same. **Why it was open:** the plan stated where startup
  calls the check but never traced the three startup paths against each layer, and step 7 did not question
  what "the schema its version defines" means before migrations have run. Decisions A to E were the
  questions identified; this one was missed in planning.

  **Corrected after T1 (developer, 2026-11-15).** As first built, the Schema layer compared an old database
  before migrating and startup then disregarded the result: work whose answer was certain, which startup
  logged at Error on every upgrade. The developer's question, *why compare when migrations are already
  known to be needed*, settled it: the layer now reads the version tables first and compares only when each
  records this build's version, reporting itself skipped otherwise. See step 15.

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
- **Deviation, answered (developer, 2026-11-17: *"why not use both variants?"*):** row 13 used an index that
  disagrees with its table, not a file that is not a database. Before this issue a file that is not a
  database made `InitialiseAsync` throw, so the test would fail on an exception rather than on its assertion.
  A broken index is opened and written to today, which the test observes as a `-wal` and `-shm` appearing
  beside the file. Once the check existed, the throw was gone, so row 13 now has both:
  `InitialiseAsync_NotADatabase_ReturnsTheFormatOutcomeWithoutOpeningForWrite` was added on 2026-11-17 and
  proven red by making `StartupDatabaseCheck.Stops` answer `false`, where it failed with *SQLite Error 26:
  'file is not a database'*, the index variant failing alongside it on its assertion.
  **Not raised again between step 6 and step 18**, though it was open throughout and row 13 was ticked:
  an open question blocks the row it concerns until it is answered.
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

**Status:** ✅ Done

- Add the `layers` and `isSound` fields per decision B.
- Update the endpoint's `[Description]` and `docs/api-endpoints.md` in the same commit.

**Done 2026-11-15.**

- **Fields added, none renamed:** `DatabaseIntegrityDto` gains `isSound` and `layers`, a list of the new
  `DatabaseCheckLayerDto` (`layer`, `status`, `finding`, `elapsedMilliseconds`, the enum values lower case as
  the endpoint's other values are). The route and `isIntact`/`differences` are unchanged.
- **The endpoint** runs each database's registered check (the main one, and the changelog's by its key) at
  `Full`, then #305's comparison only when Format, Journal and Pages passed, since that comparison opens the
  file. When they did not, `isIntact` is `false` and `differences` empty, and the failing layer is the answer:
  a main file that is not a database used to make the endpoint answer `500`.
- **`isIntact` keeps its #305 meaning** (decision B): an index that disagrees with its table makes a database
  not sound and leaves it intact.
- **Tests,** each red first: `IntactDatabases_ReportEveryLayerPassed` (the positive control),
  `Integrity_CorruptDatabase_ReportsTheFailingLayerInsteadOfThrowing` (answered `500` before),
  `Integrity_FastSetting_StillReportsEveryLayer` and `Integrity_IndexDisagreesWithItsTable_ReportsTheLayerAndStaysIntact`.
  The six existing tests of the endpoint still pass.
- **Documentation:** the endpoint's description and `docs/api-endpoints.md` say the same.

### 12. T2 documents

**Status:** ✅ Done

- **Two new documents in `startup-and-degradation/`:**
  - a database with a broken index degrades at startup and recovers through a Reset;
  - with `database_startup_check: Fast`, the same database starts healthy, and the integrity endpoint
    still reports the broken index.
- **Re-run `10`** (a data directory that cannot be created), whose detection moves to the Storage layer.
- **Fixtures:** each document's fixture is built with the same documented-format helper as step 6.
- **Canary:** each new document is confirmed red against `quotinator:canary-490`, built from `342e0cfa`.
  Then the container, image and worktree are removed.

**Done 2026-11-15.**

- **The documents** are `startup-and-degradation/15` (a broken index degrades, and a Reset recovers, setting
  the file aside) and `/16` (with `fast`, the same database starts healthy and the endpoint still reports it).
  `15` is in the smoke set, since it is a degraded startup path; `16` is not. Both are indexed, in the solution,
  and each ends by proving the Reset remedy works.
- **The fixture tool:** a new `TestProbe damage --rewrite-last-index-key` command, which calls
  `DocumentedFormatFixtures.RewriteLastIndexKey`. TestProbe now references `Quotinator.Data.Testing` for it,
  rather than a script repeating the byte layout. Its tests in `DamageCommandTests` were red first. Running the
  canary with a mangled path found that a missing database file made the command crash on SQLite's own error;
  `RewriteLastIndexKey_MissingFile_FailsNamingIt` was written red, and the helper now requires the file to exist
  before every damage.
- **Canary**, `342e0cfa` as `quotinator:canary-490`: `15` read `healthy` over the broken index, and both
  documents failed on the absent `isSound` and `layers`. Container, image and worktree removed.
- **On `quotinator:local` built from `6cb7a4b9`:** `15`, `16` and `10` passed in full. Their *Observed effect*
  sections record the readings. `10`'s reason is still set by the configuration-time guard, before the check
  runs, so the live run cannot show the Storage layer reporting it; the in-process startup test does.
- **Found, recorded in `15` rather than fixed here:**
  - In the container on a Windows bind mount, Pages and Indexes took hundreds of milliseconds on the main
    database, against step 2's 7.6 ms and 14.4 ms read directly. The add-on description's "about 25
    milliseconds" is the direct figure.
  - A file set aside because it is not a sound copy is still named `…_unreadable_…`, #482's name.

### 13. Leave no contradiction in the documentation

**Status:** ✅ Done

Search `docs/`, `CLAUDE.md`, the add-on `DOCS.md` files, the Knowledgebase and the endpoint descriptions
for `quick_check`, *reads every page*, and #326's error-code description, and correct each one.

**Done 2026-11-15.** The search also covered *cannot be read*, *readable* and *unreadable*, since a Reset now
sets aside a file that reads but is not a sound copy.

- **ADR 028's *Known non-compliance* section is removed.** Its three items no longer hold: startup checks
  before opening the database, a Reset routes by `ResetDatabaseCheck.IsSoundCopy`, and the endpoint reports
  every layer.
- **The Reset's description**, in `AdminEndpoints.cs` and `docs/api-endpoints.md`, says the old file is
  checked at full depth first, and that a file that is not a sound copy is set aside. *Readable database*
  became *sound copy*. `docs/data-import.md` says the same and links ADR 028.
- **Knowledgebase:** `the-database-file-cannot-be-read.md` lists the third cause behind the generic reason (a
  file that reads but is damaged inside), says `fast` does not check an index at startup, and points to the
  integrity endpoint's `layers`. `no-backup-could-be-taken.md`'s `SourceUnreadable` remedy says *not a sound
  copy*.
- **The two set-aside notifications** (developer decisions, 2026-11-15) no longer say the file *could not be
  read*, which was false for a broken index, and now say why the file was set aside. In en, nl and de the
  title says the file could not be confirmed sound, and the body names the check layer that failed, its
  status and the engine's finding. A file the check passed but the backup copy could not read is the one
  case with no failed layer; its notifications give the backup's own error instead, through a second body
  key for each (`…SetAsideByBackupBody`). Both payloads record the reason as `checkLayer`, `checkStatus`,
  `checkFinding` or `backupError`, per ADR 026 rule 6. Verification row 24.
- **Every surface that reports a Reset says why it set the file aside, or had to** (developer, 2026-11-15:
  any error or reason that prevents something is reported to the user, as backup obstacles already are).
  The reason is one `SetAsideReasonResult`: the check layer, or the backup's error. It is on the restore
  point when the Reset ran, and on `DatabaseOperationResult` and `NotificationActionResult` when it was
  refused. It reaches:
  - the Reset endpoint's `200` (`backup.checkLayer`, `checkStatus`, `checkFinding` or `backupError`) and its
    `409` for a file that could not be moved (the same fields beside `resetObstacle`);
  - the repair control's refusal text, and the Notifications page, which now also says inline that a Reset
    set the file aside, as it already did for a skipped backup;
  - the recorded result notification of a refused Reset, through two more body keys
    (`…RefusedForResetNotSoundBody`, `…RefusedForResetByBackupBody`).
  Verification row 25.
  - **Red first:** every new test failed on its own assertion against a skeleton whose consumers ignored the
    reason. One Core refusal test first stopped on its helper, since a refusal carries no check; it was
    rewritten to take the expected layer from checking the file beforehand, and was red on its claim. The two
    restore-point tests passed on the first skeleton, which already stored the reason; each was proven by
    mutation, dropping the stored value turning it red on its own assertion.
- **Unchanged, and why:** no hits for #326's error codes, or in `CLAUDE.md` or the add-on `DOCS.md` files.
  ADR 025 and ADR 027 already said *sound copy*. `backup/02`'s *a check that reads every page* is still true
  of the Pages layer. The integrity endpoint's *cannot be read (format, journal or pages)* matches `15`, where
  only Indexes failed and the schema was still compared. `backup/01`'s quoted notification title is a dated
  measurement and stays as observed.
- **Still recorded rather than fixed:** the `…_unreadable_…` file and folder names, internal comments that
  say *unreadable*, and the add-on description's direct-read timing.

### 14. Boyscout pass and full build

**Status:** ✅ Done

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both 0 warnings and 0 errors.

**Done 2026-11-15.**

- **Scoped lists:** of the 103 C# files this issue touched since `342e0cfa`, 13 were in the IDE0008 list
  but not the IDE0090 one: test files and `CurrentSchema.cs`, edited after step 3 when the check became a
  required constructor parameter. They were added, which surfaced 197 IDE0090 warnings; `dotnet format style
  --diagnostics IDE0090` on those 13 files alone cleared them in one pass, and a second and third found
  nothing more. Every changed line only rewrote `new X(...)` as `new(...)`.
- **Logging prefix:** every log message in the touched source files carries a defined prefix (`[Server]`,
  `[Config]`, `[Database - …]`) or takes it as its `{Tag}`; the startup banner is `logging.md`'s documented
  exception.
- **`nameof` for SQL columns:** nothing to apply. The SQL this issue added is PRAGMAs, which name no columns,
  and `SelectRootPage`, which reads `sqlite_master`, an engine table with no entity type.
- **Build and tests:** 0 warnings and 0 errors; 5,177 tests, all passed. Verification row 22.

### 15. T1

**Status:** ✅ Done

This is the developer's step: start the application in Visual Studio and confirm it starts without error.
That is the whole gate (`docs/release-verification.md`, *T1*).

**Corrected 2026-11-15 (developer).** This step first listed a broken-index start and a read of the
integrity endpoint as T1 items. Both are T2: `startup-and-degradation/15` and `16` cover them, and step 16
runs them. Folding them into T1 is what the T1 gate rules out.

**First run, 2026-11-15** (the developer's log, over an existing database at data v3 and app v5):

- **Item 1 held:** the database was backed up, migrated to data v30 and app v9, checked again (*every
  layer passed, 34 ms*) and served; the changelog database's checks took 7 ms and 50 ms. A Reset that
  followed backed up the sound copy rather than setting it aside, and the reseed ran normally.
- **Found:** the check taken before migrating logged *the Schema layer failed* at Error, listing 16 missing
  objects, on an upgrade that then succeeded. The comparison could only ever say "different" there, and
  startup disregarded it. Fixed as decision F's correction (above):
  - `SqliteDatabaseCheck` takes `expectedSchemaVersions`, from `DatabaseInitializer.ExpectedSchemaVersions`
    and `ChangelogDatabaseInitializer.ExpectedSchemaVersions`, keyed by the new `SchemaVersionTable`;
  - the Schema layer reads each version table through the check's own connection and reports itself
    `Skipped` with a finding when one is missing, behind or ahead; a database with no version tables is
    always compared;
  - startup logs a skipped layer that states a reason, at Information.
  - **Red first:** `SqliteDatabaseCheckTests.Check_RecordedVersionBehindThisBuild_SkipsSchemaNamingTheVersionTable`,
    `…Check_RecordedVersionAheadOfThisBuild_SkipsSchema` and `…Check_NoVersionTable_SkipsSchema` failed on
    `Skipped`, and `DatabaseInitializerTests.InitialiseAsync_OldDatabase_LogsTheSchemaSkippedRatherThanFailedBeforeMigrating`
    on the failure line; the log is that test's subject, being what the operator read. The control,
    `…Check_RecordedVersionIsThisBuilds_ComparesTheSchema`, passed throughout. Verification row 26.
- **Second run, same database:** confirmed the fix. Before migrating, startup logged *the Schema layer was
  skipped: System_SchemaVersion records version 3, and this build's is 30*, at Information, with no Error
  line; after migrating, *every layer passed (Full, 36 ms)*.
- **Found in the second run:** the line after the skip still read *every layer passed (Full, 40 ms)*, a
  claim about a layer that did not run. The same wording had always stood beside the two layers `Fast`
  skips. A check that skipped a layer now logs *every layer that ran passed (…; skipped: Schema)*; with
  nothing skipped the line is unchanged. **Red first:** `StartupDatabaseCheckTests.Log_PassedWithASkippedLayer_DoesNotClaimEveryLayerAndNamesTheSkip`
  and `…Log_PassedAtFast_NamesBothSkippedLayers` failed on their claims; the control,
  `…Log_PassedWithNothingSkipped_SaysEveryLayerPassed`, passed throughout. Verification row 26.
- **Third run, on `f431b7f4`:** before migrating, *the Schema layer was skipped* and *every layer that ran
  passed (Full, 30 ms; skipped: Schema)*; after migrating, *every layer passed (Full, 38 ms)*.
- **Result:** all three runs started without error and reached *Quotinator ready*. Verification row 23.
- **Narrowed 2026-11-16 (developer): skipping on an overshoot was wrong.** *"The only reason we skip schema
  verification is when we know we are going to migrate, as the bad schema will be fixed once the migration
  completes. In an overshoot situation a schema verification confirms the details of the overshoot."* The
  Schema layer now skips only when a version table is missing or behind this build, and the initialisation's
  own #305 comparison, which had skipped every overshoot since #350, now compares it too. The overshoot stays
  the degraded reason, ahead of a schema that is not intact; the comparison is its detail, and a difference
  it finds also stops content being loaded. **Red first:** `SqliteDatabaseCheckTests.Check_RecordedVersionAheadOfThisBuild_ComparesTheSchema`
  (replacing `…SkipsSchema`), `DatabaseIntegrityCheckTests.SchemaVersionOvershoot_IntegrityChecked` (replacing
  `…IntegrityNotChecked`) and `…SchemaVersionOvershoot_WithATableDropped_NamesIt` failed on their claims; the
  behind-and-missing controls passed throughout. ADR 028 follows. Verification rows 26 and 32.
- **Reworded to match (developer, 2026-11-16).** The overshoot's degraded reason, its notification body in all
  three languages and its log line said *the schema's shape is unknown*, which the comparison now contradicts.
  Each now says the schema was compared, names `GET /api/v1/admin/database/integrity` where the reason and the
  notification are read, and keeps what is still unknown: what the migrations this build never saw meant for
  the data. **Red first:** `StartupDegradationReasonsTests.OvershootReason_PointsToTheComparisonRatherThanCallingTheShapeUnknown`
  and `…OvershootNotification_…` failed on the old text. `startup-and-degradation/06` asserts the route in the
  reason and no longer explains the degradation by an unknown shape.

### 16. T2

**Status:** ✅ Done 2026-11-17. Restarted from the first document on `2fd67742` after the review below; complete on `3205ba49`.

The *end of an issue* scope (`docs/automated-testing/README.md`, *When to run what*): the smoke set, every
document this issue changed, and every test relevant to what it touched, each against `quotinator:local` built
from this branch's final commit. **Every document is first read against the README's three rules** (one
command per step, the test supplies its own data, no outdated result) and converted where it breaks one,
before it runs; then it is executed exactly as written, and its *Observed effect* is replaced by this run's
reading.

**Corrected 2026-11-15 (developer).** This step first listed `08`, `09`, `10` and `14` beside the smoke
set, though all four are in it, and recorded no relevant set at all.

- **The smoke set**, all twenty. It includes `startup-and-degradation/08`, `09`, `10`, `14` and `15`, which
  this issue's check now decides, and `15` and `10`, which it changed.
- **Changed by this issue, outside the smoke set:** `startup-and-degradation/16`.
- **Relevant: the check now gates the upgrade path, before migrating and after.** T1 found its one defect
  there.
  - `notifications-and-changelog/03` (upgrade from an intermediate schema version);
  - `notifications-and-changelog/09` (upgrading translates a shipped notification);
  - `startup-and-degradation/02` (startup backs up only when there is work);
  - `database-lifecycle/04` (a database is complete for its schema version, fresh or upgraded), which reads
    the integrity endpoint whose response this issue changed.
- **Relevant: a Reset now routes by the check, and reports why it set a file aside.**
  - `backup/01` (a file that is not a database), `backup/02` (a truncated one);
  - `backup/03` (the disk fills during a Reset's backup), `backup/04` (the backup folder cannot be written);
  - `backup/09` (setting a file aside refused for an unwritable data directory), `backup/10` (a Reset with
    the override).
- **Relevant: the set-aside notifications' text and payload changed.** `notifications-and-changelog/14`
  (every notification kind renders what its layout promises).
- **Not relevant, and why:** `backup/05` and `08` cover backup storage limits, which no Reset routing
  changes; `notifications-and-changelog/10` and `12` cover the reseed recommendation and an action's running
  state over a healthy database, whose Reset `database-lifecycle/03` in the smoke set already exercises.

**First run, 2026-11-15, on `9b59f414`: stopped at the ninth smoke test.**

- **Passed:** `api-surface/01` and `02`, `import-and-staged-actions/01`, `14` and `19`,
  `startup-and-degradation/03`, `database-lifecycle/03` (step 8 failing as the known #468 it records), and
  `startup-and-degradation/01`, once its pre-#305 prose about a seeding backup and a handled `SqliteException`,
  which contradicted its own commands, was removed.
- **`startup-and-degradation/05` had not been converted for this issue,** though its fault, an unwritable
  data directory, is the Storage layer's. Startup now stops there before any migration, so its log count of a
  `SqliteException` read `0`. Converting it was reactive, after a run, which step 12 should have done and this
  step's rule forbids. The developer's direction that followed: **every document in scope is reviewed against
  this issue's changes before any more run, since each may break for a different reason; only responses and
  application state are evidence, and the log is read only for exceptions nobody knew about.**
- **Found, a defect:** with `/data` read-only, `GET /admin/database/integrity` answered `500`. It ran #305's
  comparison whenever the file could be read, through the application's own connection, which cannot open a
  write-ahead-log database in a directory it cannot write; the exception went unhandled. **Fixed:** the
  comparison runs only when Storage passed as well. **And, per the developer's direction (2026-11-15), a failed
  check degrades the application at any point, not only at startup:** `DatabaseCheckResult.DegradingFailure` is
  the one rule of which failures degrade, read by startup and the endpoint alike, and
  `StartupDegradationReasons.ForCheck` maps a failure to the reason startup gives for it. ADR 028, the endpoint
  description and `api-endpoints.md` say so. **Red first:**
  `AdminDatabaseIntegrityEndpointTests.Check_LayerFailsAfterStartup_DegradesTheApplication`,
  `…Check_StorageFails_ReportsWithoutComparingAndDegrades` and `…Check_SchemaBrokenAfterStartup_DegradesTheApplication`
  failed on their claims; the controls `…Check_ReferencesFailOnly_LeavesTheApplicationHealthy`,
  `…Check_ChangelogFails_LeavesTheApplicationHealthy` and `…Check_ThatPasses_LeavesHealthAsItWas` passed
  throughout. Verification row 27.
- **T2 restarts from the first test** on a build with the fix, after the review.

**The review, 2026-11-15, before any further run.** Every document in scope was read against this issue's
changes, the evidence rule and the README's three rules; the record is kept per document. It converted
`startup-and-degradation/08` and `16` and `notifications-and-changelog/01` and `03`, and measured one
behaviour rather than predict it: `backup/03`'s full volume.

- **Found, a defect in decision D:** with the volume full and the `-wal` holding data, a Reset's check could
  not make its copy, reported every query layer `Inconclusive`, and the Reset then set a healthy database
  aside, answering `200`, where `backup/03` expects a refusal. The developer's correction (2026-11-15): **a copy
  that cannot be made is a result, what Storage found, never an inconclusive one; `Inconclusive` means only a
  test that ran and answered neither way; and no room for the check's copy is no room for a backup, so a Reset
  refuses as for any backup obstacle, and with `allowNoBackup` proceeds and sets the file aside.**
- **Fixed:** the Storage layer measures the room the copy needs, the database and its `-wal`, before making it,
  and makes it; a copy that cannot be made fails Storage with a typed `DatabaseStorageFault`
  (`InsufficientSpace` or `DirectoryNotWritable`), and the reading layers then do not run. A Reset refuses on
  `InsufficientSpace` with `InsufficientDiskSpace`; with the override it sets the file aside, naming Storage as
  the reason. Startup degrades with a new reason, `NotEnoughSpace`, and a file Storage could not read now gets
  the generic reason rather than the data directory's. ADR 028, the knowledgebase entry the reason links to,
  the Reset's descriptions and `api-endpoints.md` follow. **Red first:**
  `SqliteDatabaseCheckTests.Check_CopyCannotBeWritten_FailsStorageAndRunsNoReadingLayer` (row 12's test,
  rewritten), `…Check_NoRoomForTheCopy_FailsStorageWithoutAttemptingIt` and the fault assertion added to
  `…Check_DirectoryNotWritable_FailsStorageAndStillChecksTheFile`;
  `DatabaseInitializerTests.ResetAsync_NoRoomToCheck_RefusesForWantOfABackup` and
  `…ResetAsync_NoRoomToCheck_WithOverride_SetsTheFileAside`; `StartupDegradationReasonsTests.For_StorageFoundNoRoom_IsTheSpaceReason`
  and `…For_StorageFoundTheFileUnreadable_IsTheGenericReason`, each failing on its claim. The controls
  `…Check_RoomForTheCopy_ChecksThroughIt`, `…Check_NoCopyNeeded_NeedsNoRoom` and
  `…ResetAsync_CopyCannotBeWritten_SetsTheFileAside` (row 14's test, renamed) passed throughout. Verification
  row 28.
- **Found while converting `backup/03`, a defect in the room measurement itself:** on Linux,
  `DiskSpaceProvider` measured `DriveInfo` on the path's root, and the root of `/data/...` is `/`, so a full
  tmpfs volume mounted at `/data` read as the container's root file system, about a terabyte free. Every
  room check before a backup or the check's copy measured the wrong file system. **Fixed (developer,
  2026-11-15):** the file system holding the path is measured by each platform's documented call, `statvfs`
  on Linux and `GetDiskFreeSpaceExW` on Windows, from the nearest existing parent, falling back to `DriveInfo`
  elsewhere or when the call fails. A separate mount needs a Linux container, so the red proof is
  `backup/03`, run on `9b59f414` (before this fix) and on this build; `DiskSpaceProviderTests` holds the
  controls. Verification row 29.
- **Found while converting `backup/03`'s override step, and corrected by the developer (2026-11-16): the
  check must not need disk space at all; it is done in memory.** With a `-wal` holding data the check
  copied the database to disk, so a full volume stopped it, and the outcome of a confirmed Reset depended on
  what the `-wal` held. Decision D is superseded (above): the `-wal`'s committed frames are applied in memory
  by `fileformat.html` §4, header bytes 18 and 19 set to 1, and the image opened with `sqlite3_deserialize`.
  It removed the work folder, the room measurement, `DatabaseStorageFault.InsufficientSpace`, the Reset's
  refusal when the check had no room, startup's `NotEnoughSpace` reason and its knowledgebase entry. ADR
  028, the Reset's description and `api-endpoints.md` follow. **Red first:** `WalReplayTests`, against a
  `WalReplay.Apply` that returned the main file unchanged, failed on 8 of 11 claims, the 3 controls passing;
  `SqliteDatabaseCheckTests.Check_WalHoldsData_NothingCanBeWritten_StillChecksTheFile` and a no-free-space
  variant failed on the copy-based check. The no-free-space test was then removed with the check's disk-space
  parameter, which no longer exists. `DatabaseInitializerTests.ResetAsync_WalHoldsData_NothingWritableForTheCheck_BacksTheSoundCopyUp`
  was written after the change, so its red was proven by running it against `0323c57f` in a temporary
  worktree: it failed on `Assert.IsEmpty(MainSetAsideFiles())`, the healthy file set aside. `backup/03` step 6
  now asserts the confirmed Reset exactly: `Skipped`, naming `InsufficientDiskSpace`. Verification row 30.
- **The review's open questions, answered (developer, 2026-11-16).** `notifications-and-changelog/09` was
  converted rather than dropped: single commands, the API and the stopped database file as evidence, the log
  read only for exceptions. `import-and-staged-actions/19` step 8 was not a dedicated log test: the seed's
  counts are now read from `GET /api/v1/version` at the end of step 1, and the log step is gone.
  `notifications-and-changelog/14` was reviewed and needs no change. `backup/03` was converted to the measured
  refusal, the confirmed Reset and the room-freed control; `backup/07` and `startup-and-degradation/06` and
  `12` were converted or reviewed with step 17 and the overshoot comparison. **The review is complete**, and T2
  restarts from the first document on `2fd67742`.

**Restarted run, 2026-11-17, on `2fd67742`, then `272fa048` and `3205ba49`: complete, every document
passing.**

- **The smoke set**, twenty of twenty, failing only where the documents record the known #468
  (`database-lifecycle/03` step 8) and #472 (`notifications-and-changelog/01` step 9).
- **Changed and relevant:** `startup-and-degradation/16` and `02`, `notifications-and-changelog/03`, `09`
  (its first run in the converted form, so it left the awaiting-measurement list) and `14`,
  `database-lifecycle/04`, and `backup/01`, `02`, `03`, `04`, `07`, `09` and `10`. Each *Observed effect*
  is this run's reading.
- **Found by `backup/03` step 6, a defect in the backup pre-flight:** it refused only when no space at all
  was free, while the attempt refuses when the space is less than the database's size. With some room but
  not enough, the pre-flight said a backup could be taken, so the override never applied and the attempt
  refused the Reset the operator had confirmed. Hidden until the room was measured on the right file system.
  **Fixed in `272fa048`, red first** (row 30's tests); the `InsufficientDiskSpace` cause, which said the
  volume had no free space left, was reworded in `3205ba49`.
- **Found by `backup/07` step 5, a defect in the document:** it copied the database after `destroy`, a
  SIGKILL that leaves the `-wal`, and after DbInspector's read-only open, which leaves an empty one of its
  own. Step 5 now stops the container cleanly and copies before reading. The `-wal` life cycle is the SQLite
  engine's, and is now recorded for tests in `docs/automated-testing/README.md` and for operators in the
  knowledgebase entry *Files named quotinatordata.db-wal and quotinatordata.db-shm appear beside the
  database*.

### 17. Let the operator confirm a migration without a backup

**Status:** ✅ Done. Built 2026-11-16; its T2, `backup/07`, green on `272fa048` (2026-11-17). Added 2026-11-16 (developer): *"when there is no room we cannot make any
backups. so we have to inform the user and ask them to confirm. when that happens when we need to migrate
that means we start degraded as we first need to allow the user to fix it or allow us to migrate without a
backup."* Found in step 16: a Reset already works this way, a migration does not. Startup degrades with
`MigrationRefusedReason`, which deliberately drops the `allowNoBackup` remedy
(`BackupObstacleGuidance.WithoutOverride`), and nothing in the application can run the migration later; the
only remedy is to free room and restart. Decided 2026-11-16: an admin endpoint, not a setting.

**The endpoint: `POST /api/v1/admin/database/migrate`, with `?allowNoBackup=true`.** It needs the option for
the reason a Reset does: by the time it is called, the room may be back.
- Without the option it takes the backup, as startup would, or refuses with `409` naming the
  `backupObstacle` and its `remedies`, `allowNoBackup` among them. The refusal is how the operator is told.
- With it, a backup that cannot be taken is skipped: the operator's confirmation. The skip is written to the
  audit trail, as the Reset's is (#348), and the response says what was left to restore from (`backup`, as
  the Reset's does).
- A backup that can be taken is always taken, with or without the option. The option accepts going without
  one; it never asks to.

**It reruns startup's own initialisation, not a second path through it.** `InitialiseAsync` gains
`allowNoBackup`, so the endpoint runs exactly what startup runs: the check before writing, the migrations,
the check after, the schema comparison, and the content load. Its result is mapped by
`StartupDegradationReasons.For`, the one mapping startup uses: healthy if nothing degrades, otherwise the
application stays degraded on the new reason, which replaces the old one.

**The option covers the migration's backup only (corrected 2026-11-16, on reading the code).** A content load
refused for want of a backup does not degrade startup: it raises its own notification, whose remedy is a
reseed (#348). Letting the migration's confirmation also skip the content load's backup would bundle a second
data decision into the call, which CLAUDE.md's endpoint side-effect policy rules out. The content load keeps
its own backup and its own refusal.

**What startup does after a healthy initialisation runs too.** Program.cs gates on `dbHealth.IsHealthy`
everything after initialisation: reading the last active version, recording the current one, and the
notification producers, among them one notification per migration applied (#371) and the what's-new
catch-up (#81), then the condition checks (#348). A migration completed from the endpoint must leave the
same state a healthy restart would, so that block moves out of Program.cs into one service that startup and
the endpoint both call. The ordering comments that hold it in place (#81's read before #312's write) move
with it.

**Where it can be called (developer, 2026-11-16).** Only while the application is degraded because a backup
was refused before a migration, the one backup refusal that degrades. Anywhere else it answers `409` saying there is nothing to resume: a healthy application has
already initialised, and every other degraded reason has its own remedy, which rerunning initialisation
would not reach.

**The guidance.** `MigrationRefusedReason` names the endpoint, with and without the option, in place of
*then restart*, and keeps `allowNoBackup` among its remedies. The knowledgebase entry it links,
`no-backup-could-be-taken.md`, says the same.

**Red first.** At least:
- `InitialiseAsync(allowNoBackup: true)` with a backup obstacle migrates, records the skipped backup, and
  reports no restore point; without the option it refuses as today; with room it takes the backup either way.
- The endpoint: `409` with the obstacle and remedies; `200` with the option, after which `/health` is healthy,
  the schema version is current, and the migrations-applied notifications exist; `409` when called on a
  healthy application.
- The startup-completion service: one test that startup and the endpoint leave the same notifications for the
  same migrations.

**T2.** `backup/07` (a migration refused for want of a backup) gains the endpoint: refused without the
option, then completed with it, and the restart it uses today kept as the control that freeing room still
works. `startup-and-degradation/12` is reviewed against it, since it asserts the same refusal's startup
contract.

**As built, 2026-11-16.**
- `IDatabaseInitializer.InitialiseAsync(allowNoBackup)`: the migration's backup is still attempted first; only
  when it cannot be taken does the confirmation skip it, logged by `LogMigrationProceedingWithoutBackup`. The
  result carries what the migration left to restore from, `BackedUp` or `Skipped`, whatever happens after it.
- `DatabaseInitialisationSequence` (`Quotinator.Api/Startup`) holds what Program.cs did with the main database,
  moved verbatim with its comments: `InitialiseAsync` maps the result to health through
  `StartupDegradationReasons.For`, and `CompleteAsync` is the healthy-start work. Program.cs calls both; the
  endpoint calls both. `DataDirectoryFault.IsNotWritable` moved out of Program.cs with it rather than being copied.
- `DatabaseHealthState.MigrationAwaitsConfirmation`, set by `MarkMigrationRefused`, cleared by anything that
  changes the reason, decides where the endpoint can be called.
- `POST /api/v1/admin/database/migrate` answers `409` with the obstacle and remedies when still refused, `409`
  with nothing to resume elsewhere, `500` when the resumed initialisation degrades for another reason, and `200`
  with `DatabaseMigrationResponse` otherwise. A skipped backup is written to the audit trail, as a Reset's is.
- `MigrationRefusedReason` names the endpoint, with and without the confirmation, where the obstacle allows
  it. `no-backup-could-be-taken.md` and `api-endpoints.md` say the same.

**Red first.** `DatabaseInitializerTests.InitialiseAsync_MigrationPendingWithNoBackupPossible_WithOverride_Migrates`
and `…_ReportsTheSkippedBackup` failed on their claims, the controls `…MigrationPendingWithRoom_WithOverride_StillTakesTheBackup`
and `…ContentLoadWithNoBackupPossible_WithOverride_IsStillRefused` passing. `AdminMigrateEndpointTests` (6 of 7)
and `DatabaseHealthStateTests.MarkMigrationRefused_DegradesAndAwaitsConfirmation` failed against a stub endpoint
answering `501` and a marker that was always false, the controls passing. `BackupObstacleGuidanceTests.MigrationRefusedReason_OffersTheConfirmationOnTheMigrateEndpoint`
and `…OffersResumingOnceResolved` failed on the old reason; `…_DoesNotOfferTheOverride`, whose premise this step
reverses, was replaced, its control kept as `…SourceUnreadable_OffersNoConfirmation`.

**Found by the full suite, and fixed.** The first version of the sequence marked the application healthy after a
clean initialisation, and replaced rather than kept an earlier reason, which erased a failure startup had recorded
before the database: `StartupResilienceTests.Startup_KeysDirectoryCannotBeCreated_StartsDegradedInsteadOfCrashingBeforeKestrelBinds`
and `…Startup_KeysDirectoryNotWritable_HealthReportsUnhealthyRatherThanHealthy` failed, reading `200`. Now only a
resumed migration clears or replaces a reason; at startup the first reason stands, and a migration refused after an
earlier failure does not await confirmation (`DatabaseHealthStateTests.MarkMigrationRefused_AfterAnEarlierFailure_KeepsItAndDoesNotAwaitConfirmation`).
`RepositoryStructureTests.Program_WaitsForTheBackgroundWorkItStarted` and `…BeforeMarkingStartupComplete` read
Program.cs for #419's wait, which moved into `CompleteAsync`; they now assert the wait there, after the last
background start, and that startup runs `CompleteAsync` before `MarkComplete()`.

**The degraded page offers it too (developer, 2026-11-16: "Yes, add it").** The repair control,
`DatabaseRepairActions`, rendered by the startup modal and the degraded banner, offers the Reset in-process. While
`MigrationAwaitsConfirmation`, it also offers *Resume the migration*, which takes the backup, and *Migrate without a
backup*, armed by the same confirm step the Reset uses. So both the endpoint and the control run one implementation,
the endpoint's logic moves into `DatabaseInitialisationSequence.ResumeMigrationAsync`, returning a
`MigrationResumeResult` that each maps to its own surface: HTTP statuses, or a translated sentence through
`DatabaseRepairActions.MigrationOutcomeText`. Its sentences are in all three translation files. Tested through the
static helper, as the control's Reset outcome is; what it renders is `startup-and-degradation/06`'s and
`backup/07`'s.

**As built, 2026-11-16.** `MigrationResumeResult` carries a `MigrationResumeOutcome` (`Resumed`,
`NothingToResume`, `RefusedForBackup`, `StillDegraded`), the obstacle, the reason and the restore point; the endpoint
maps it to `200`, `409`, `409` and `500`, and the control to a sentence. The control shows *Resume the migration* and
*Migrate without a backup* only while `MigrationAwaitsConfirmation`, the second armed by a confirm step warning that
there will be no backup to restore. **Red first:** `DatabaseRepairActionsTests.MigrationRefusedForBackup_NamesTheObstacleInTheOperatorsLanguage`,
`…MigrationStillDegraded_NamesWhatIsStillWrong` and `…NothingToResume_SaysSo` failed against a helper that returned
nothing; `…MigrationResumed_SaysNothing` passed throughout, as the control. `AdminMigrateEndpointTests` stayed green
across the move.

**T2 documents, converted 2026-11-16, not yet run.** `backup/07` now proves each route on the same refused
database, saved once (after confirming no `-wal` beside it) and restored per route: the reason offers the
endpoint, the page shows both buttons, the endpoint without the confirmation refuses with `409`, the page's
*Migrate without a backup* migrates with `backups/` holding only the filler, the endpoint resumes with the backup
once the filler is gone, and the restart stays as the control. Its #490 canary is `56dfe433`. Two page scripts
drive the buttons, `migration-repair-controls.js` and `migration-confirm-without-backup.js`.
`startup-and-degradation/12` was reviewed: its contract is unchanged, and it points to `backup/07` for resuming.

### 18. Changelog

**Status:** ⬜ Not started

Add an `unreleased` entry in `changelog.en.json`, `nl.json` and `de.json`, carrying issue 490. Its highlight
says that the database is checked thoroughly at every start, and that the faster check is an option.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A healthy database passes every layer | Unit test | `SqliteDatabaseCheckTests.Check_HealthyDatabase_PassesEveryLayer` |
| 2 | ✅ | A file that is not a database fails at Format, and no later layer runs | Unit test | `SqliteDatabaseCheckTests.Check_NotADatabase_FailsAtFormatAndRunsNoLaterLayer` |
| 3 | ✅ | A truncated database with a valid in-header size fails at Pages, and no later layer runs | Unit test | `SqliteDatabaseCheckTests.Check_TruncatedDatabase_FailsAtPagesAndRunsNoLaterLayer` |
| 4 | ✅ | A `-wal` with an invalid magic, or header checksum, fails at Journal | Unit test | `SqliteDatabaseCheckTests.Check_InvalidWalMagic_FailsAtJournal`, `…Check_InvalidWalHeaderChecksum_FailsAtJournal` |
| 5 | ✅ | A damaged page fails at Pages | Unit test | `SqliteDatabaseCheckTests.Check_DamagedPage_FailsAtPages` |
| 6 | ✅ | An index that disagrees with its table passes Pages and fails at Indexes | Unit test | `SqliteDatabaseCheckTests.Check_IndexDisagreesWithItsTable_FailsAtIndexes` |
| 7 | ✅ | A dangling reference fails at References | Unit test | `SqliteDatabaseCheckTests.Check_DanglingReference_FailsAtReferences` |
| 8 | ✅ | `Fast` skips Indexes and References, and reports them `Skipped` | Unit test | `SqliteDatabaseCheckTests.Check_Fast_SkipsIndexesAndReferences` |
| 9 | ✅ | Every outcome leaves the file and its sidecars byte-for-byte as they were | Unit test | `SqliteDatabaseCheckTests.Check_AnyOutcome_LeavesTheFileAndSidecarsUntouched` (data-driven over rows 1 to 7 and 11) |
| 10 | ✅ | *(decision C)* An unwritable directory fails Storage, but the reading layers still run | Unit test | `SqliteDatabaseCheckTests.Check_DirectoryNotWritable_FailsStorageAndStillChecksTheFile` |
| 11 | ✅ | *(decision D, superseded 2026-11-16, row 30)* A `-wal` holding data is checked through an in-memory image with its committed frames applied, never through an `immutable` open of the main file | Unit test | `SqliteDatabaseCheckTests.Check_WalHoldsData_ChecksItsCommittedStateAndLeavesTheOriginalsUntouched` |
| 12 | ✅ | *(decision D, superseded 2026-11-16, row 30)* A `-wal` holding data beside a directory where nothing can be written is still checked: Storage reports the directory, and the reading layers run | Unit test | `SqliteDatabaseCheckTests.Check_WalHoldsData_NothingCanBeWritten_StillChecksTheFile` |
| 13 | ✅ | Startup returns a failed or inconclusive check as an outcome, without opening the database for writing | Unit test | `DatabaseInitializerTests.InitialiseAsync_LayerFails_ReturnsThatLayersOutcomeWithoutOpeningForWrite` (an index that disagrees with its table, failing at Indexes); `…InitialiseAsync_NotADatabase_ReturnsTheFormatOutcomeWithoutOpeningForWrite` (a file that is not a database, failing at Format; added 2026-11-17, step 6) |
| 14 | ✅ | A Reset sets aside a file with a broken index, backs up a sound one whose `-wal` holds data even when nothing can be written for the check, backs up one with a dangling reference, and always checks at `Full` | Unit test | `DatabaseInitializerTests.ResetAsync_IndexDisagreesWithItsTable_SetsTheFileAside`, `…ResetAsync_WalHoldsData_NothingWritableForTheCheck_BacksTheSoundCopyUp`, `…ResetAsync_DanglingReference_BacksTheFileUp`, `…ResetAsync_FastSetting_StillRunsEveryLayer` |
| 15 | ✅ | Choosing the startup reason is one mapping: this issue's cases are added, and the #348, #350 and #305 cases keep their order and text | Unit test | `StartupDegradationReasonsTests` (one test per case, plus the first-wins order); the existing #348, #350 and #305 `StartupResilienceTests` stay green |
| 16 | ✅ | *(decision A)* No type the Api layer sees from the check comes from the SQLite library | Unit test | `RepositoryStructureTests.TheDatabaseCheckContract_NamesNoEngineType` |
| 17 | ✅ | Startup detects an unwritable data directory before the database is opened, degrades on a broken index naming the layer in the startup check it keeps (generic reason until #457), and treats a dangling reference as decision E decides, reporting it in the same check | Unit test | `StartupResilienceTests.Startup_DataDirectoryNotWritable_DetectedBeforeTheDatabaseIsOpened`, `…Startup_IndexDisagreesWithItsTable_DegradesNamingTheLayer`, `…Startup_DanglingReference_StaysHealthyAndReportsTheLayer`; `DatabaseHealthStateTests.RecordStartupCheck_KeepsTheCheckWithoutChangingHealth`, `…MarkHealthy_KeepsTheStartupCheck` |
| 18 | ✅ | The integrity endpoint reports every layer, with a failing layer instead of throwing, at `Full` whatever the setting | Unit test | `AdminDatabaseIntegrityEndpointTests.Integrity_CorruptDatabase_ReportsTheFailingLayerInsteadOfThrowing`, `…Integrity_FastSetting_StillReportsEveryLayer`, `…Integrity_IndexDisagreesWithItsTable_ReportsTheLayerAndStaysIntact`, `…IntactDatabases_ReportEveryLayerPassed` |
| 19 | ✅ | The new strings, the add-on option and the endpoint documentation are complete | Unit test | Existing `TranslationCompletenessTests` and `OpenApiSpecEndpointTests`, plus `RepositoryStructureTests.EveryAddOnOption_IsTranslatedInEveryLanguage` and `…AddOnDatabaseStartupCheckDefault_MatchesTheApplicationDefault` (built in step 10) |
| 20 | ✅ | Live: a broken index degrades at startup and recovers through a Reset | Live (T2) | `startup-and-degradation/15`; passes on this build, red on `quotinator:canary-490` |
| 21 | ✅ | Live: with `Fast`, the same database starts healthy, and the endpoint still reports the broken index | Live (T2) | `startup-and-degradation/16`; same canary |
| 22 | ✅ | The full build and test suite are clean | Live | `dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`: 0 warnings, 0 errors, all passed |
| 23 | ✅ | T1: the application starts without error | Live (T1) | Step 15: the developer's three Visual Studio runs on 2026-11-15, the last on `f431b7f4`, each reaching *Quotinator ready* |
| 24 | ✅ | *(developer, 2026-11-15)* A Reset's restore point and both set-aside notifications say why the file was set aside: the check layer, its status and finding, or the backup's own error when the check passed | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_RestorePointNamesTheLayerThatShowedIt`, `…ResetAsync_CorruptDatabase_NotificationRecordsTheLayerThatShowedIt`, `…ResetAsync_FileOnlyTheBackupFindsUnreadable_RestorePointCarriesTheBackupError`, `…ResetAsync_FileOnlyTheBackupFindsUnreadable_NotificationRecordsTheBackupError`; `DatabaseActionResultRecorderTests.RecordAsync_SetAsideByTheCheck_RecordsTheLayerThatShowedIt`, `…RecordAsync_SetAsideByTheBackup_RecordsTheBackupError`, the `ResetSetAsideByBackup` rows; `StartupResilienceTests.Startup_DatabaseFileCorrupt_RepairActionNotificationsSayWhichLayerShowedIt` |
| 25 | ✅ | *(developer, 2026-11-15)* Every other surface reporting a Reset says why the file was set aside, or had to be: the response, the refusal, the repair control, the Notifications page and the refused Reset's notification | Unit test | `RestorePointDtoTests` (3); `DatabaseInitializerTests.ResetAsync_CorruptDatabaseThatCannotBeSetAside_RefusalSaysWhichLayerShowedIt`, `…ResetAsync_FileOnlyTheBackupFindsUnreadableThatCannotBeSetAside_RefusalCarriesTheBackupError`; `StartupResilienceTests.Startup_DatabaseFileCorrupt_ResetResponseSaysWhichLayerShowedIt`, `…ResetRefusalSaysWhichLayerShowedIt`; `DatabaseRepairActionsTests.RefusedForAFileThatNeededSettingAside_SaysWhichLayerShowedIt`, `…RefusedForAFileTheBackupFoundUnreadable_GivesTheBackupError`; `NotificationsPageTests.ResetThatSetTheFileAside_SaysWhyAndWhere`, `…ResetThatSetAsideAFileTheBackupFoundUnreadable_GivesTheBackupError`; `DatabaseActionResultRecorderTests.RecordAsync_RefusedForAFileThatNeededSettingAside_RecordsTheLayerThatShowedIt`, `…RecordAsync_RefusedForAFileTheBackupFoundUnreadable_RecordsTheBackupError`, the `ResetNotMovable` rows |
| 26 | ✅ | *(T1, decision F corrected; narrowed 2026-11-16, row 32)* The Schema layer is skipped, naming the table, only when a version table is missing or behind this build; startup over an old database logs no Schema failure; a passed check that skipped a layer says so rather than claiming every layer | Unit test | `SqliteDatabaseCheckTests.Check_RecordedVersionBehindThisBuild_SkipsSchemaNamingTheVersionTable`, `…Check_NoVersionTable_SkipsSchema`, `…Check_RecordedVersionIsThisBuilds_ComparesTheSchema`; `DatabaseInitializerTests.InitialiseAsync_OldDatabase_LogsTheSchemaSkippedRatherThanFailedBeforeMigrating`; `StartupDatabaseCheckTests` (3) |
| 27 | ✅ | *(T2, developer, 2026-11-15)* The integrity endpoint never throws, comparing the schema only when Storage passed, and a check that finds the main database failing degrades the application at any point; a dangling reference alone, or the changelog database, does not | Unit test | `AdminDatabaseIntegrityEndpointTests.Check_LayerFailsAfterStartup_DegradesTheApplication`, `…Check_StorageFails_ReportsWithoutComparingAndDegrades`, `…Check_SchemaBrokenAfterStartup_DegradesTheApplication`, `…Check_ReferencesFailOnly_LeavesTheApplicationHealthy`, `…Check_ChangelogFails_LeavesTheApplicationHealthy`, `…Check_ThatPasses_LeavesHealthAsItWas` |
| 28 | ✅ | *(T2, developer, 2026-11-15; the copy's part superseded 2026-11-16 by row 30)* A failure to read the file is a Storage failure with its fault typed, never an inconclusive layer; a file Storage could not read gets the generic startup reason | Unit test | `SqliteDatabaseCheckTests.Check_DirectoryNotWritable_FailsStorageAndStillChecksTheFile`; `StartupDegradationReasonsTests.For_StorageFoundTheFileUnreadable_IsTheGenericReason`. The copy's tests were removed with the copy |
| 29 | ✅ | *(T2, developer, 2026-11-15)* Free space is measured on the file system that holds the path, not on its root: a full volume mounted at `/data` reads as full | Live (T2) + unit test | `backup/03`, red on `9b59f414`, green in T2 on `272fa048` (2026-11-17: 2,284 KB free read against a 4,572 KB database, `InsufficientDiskSpace`); controls `DiskSpaceProviderTests.GetAvailableFreeSpaceBytes_ExistingDirectory_IsPositive`, `…FolderNotCreatedYet_MeasuresItsNearestExistingParent` |
| 30 | ✅ | *(T2, developer, 2026-11-16)* The check needs no disk space: a `-wal`'s committed frames are applied in memory by the documented format and the image opened with `sqlite3_deserialize`; a full volume or an unwritable directory no longer stops it, so a Reset of a sound database refuses only for its backup, and with `allowNoBackup` skips it | Unit test + Live (T2) | Unit tests green: `WalReplayTests` (11), including `…Apply_WalSQLiteWrote_MatchesWhatACheckpointWrites`, which also proves `sqlite3_deserialize` is in the bundled library; `SqliteDatabaseCheckTests.Check_WalHoldsData_NothingCanBeWritten_StillChecksTheFile` and `…Check_AnyOutcome_LeavesTheFileAndSidecarsUntouched` (now also: nothing left in the data directory); `DatabaseInitializerTests.ResetAsync_WalHoldsData_NothingWritableForTheCheck_BacksTheSoundCopyUp`. `backup/03` step 6 green in T2 on `272fa048` (2026-11-17), after T2 found the backup pre-flight weighing free space against zero while the attempt weighed it against the database's size, so the override never applied; fixed red-first: `DatabaseBackupPreflightTests.CheckBackupReadiness_WithLessFreeDiskSpaceThanTheBackupNeeds_ReportsInsufficientDiskSpace`, `…_AgreesWithTheAttempt`, `…WithNoFreeDiskSpaceAndLessFreedFirstThanTheBackupNeeds_ReportsInsufficientDiskSpace`; `DatabaseBackupQuotaTests.ResetAsync_WithTheOverride_WhenTheDiskHasLessRoomThanTheBackupNeeds_SkipsTheBackup` |
| 31 | ✅ | *(step 17, developer, 2026-11-16)* A migration refused for want of a backup can be completed from inside the application: `POST /admin/database/migrate` takes the backup or refuses naming the obstacle, and with `allowNoBackup` proceeds without one, recorded; it leaves the state a healthy restart would | Unit test + Live (T2) | Unit tests green: `DatabaseInitializerTests` (4 `…WithOverride…`), `AdminMigrateEndpointTests` (7), `DatabaseHealthStateTests` (4 new), `BackupObstacleGuidanceTests` (3 new), `DatabaseRepairActionsTests` (4 new). `backup/07` green in T2 on `272fa048` (2026-11-17, 55 commands): refused with `BudgetExceeded`, confirmed from the page without a backup, and resumed from the endpoint with one |
| 32 | ✅ | *(developer, 2026-11-16)* The schema is compared on an overshoot, by the check's Schema layer and by the initialisation's #305 comparison: only migrations still to run justify a skip. The overshoot stays the degraded reason, reworded to say what is still unknown; the comparison is its detail | Unit test + Live (T2) | Unit tests green: `SqliteDatabaseCheckTests.Check_RecordedVersionAheadOfThisBuild_ComparesTheSchema`; `DatabaseIntegrityCheckTests.SchemaVersionOvershoot_IntegrityChecked`, `…SchemaVersionOvershoot_WithATableDropped_NamesIt`; `StartupDegradationReasonsTests.For_OvershootAndSchemaNotIntact_OvershootWins`, `…OvershootReason_PointsToTheComparisonRatherThanCallingTheShapeUnknown`, `…OvershootNotification_…`. `startup-and-degradation/06` green in T2 on `2fd67742` (2026-11-17, 66 commands): the overshoot's Schema layer compared and passed, `isIntact=true`, `/health` `503` with the reworded reason |
