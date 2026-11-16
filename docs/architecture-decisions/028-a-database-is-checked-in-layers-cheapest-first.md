# ADR 028 — A database is checked in layers, cheapest first

**Status:** Accepted
**Date:** 2026-11-14
**GitHub issues:** #490, #493

---

## Context

The application has to judge whether a database file is sound in three places: at startup, before a
Reset, and when an operator asks. Each place used to judge it differently:
- startup opened the file and caught whatever broke;
- a Reset read every page, a choice made inside an issue and never recorded as a decision;
- the integrity endpoint compared only schema metadata.

The three could disagree about the same file. None could say *what* was wrong in terms an operator or the
user interface could act on.

Two constraints shape the answer.
- **The engine can be replaced.** Every engine-specific fact the rest of the application depends on is one
  more thing to rewrite if the engine changes (ADR 003, design goal 5).
- **An engine's behaviour is relied on only where its documentation states it.** A check that rests on how
  an engine was observed to react proves only what that engine version did on that machine. It proves
  nothing about the next release, a different build of the native library, or a different platform.

## Decision

### One check, in layers, engine-neutral

**Every database is judged by one check, run in layers from the cheapest to the most thorough.** The first
layer that fails is the result. Every later layer reports that it was not run, so the report always has
the same shape.

The layers name *what* is checked, never how an engine checks it:

| Layer | What it answers | Run at `Fast` |
|---|---|---|
| Storage | Is the file there, can it be read, and can its directory be written? | Yes |
| Format | Is it a database of this engine, by its documented file format? | Yes |
| Journal | Is the engine's write-ahead file, if there is one, valid by its documented format? | Yes |
| Pages | Can every page be read, and is every record well-formed? | Yes |
| Indexes | Does every index agree with its table, and does every unique constraint hold? | No |
| References | Does every declared reference point at a row that exists? | No |
| Schema | Does the schema match this build's, unless migrations are still to run? | Yes |

A layer an engine has no counterpart for reports that it passed, with a finding saying it does not apply.
It is never left out.

**The contract is engine-neutral, and only its implementation names the engine.**
- The interface, the result types and the enums live in `Quotinator.Data`.
- No type the rest of the application sees from the check comes from an engine library. A guard test
  enforces this.
- An engine's own diagnostic is carried only as verbatim text: logged and shown, never parsed, and never
  used to decide anything. This follows ADR 027's rule that a failure is never interpreted.

This is consistent with ADR 003. There is one implementation, and nothing selects between engines: no
configuration branch, no provider switch (design goal 3). The interface is the contract and the
implementation is engine-specific (design goal 2). The engine-specific reads stay inside `Quotinator.Data`
and never replace handling an exception (design goal 5). Replacing the engine means writing a new
implementation that maps its own documented checks to the same seven layers, and adding a section to this
ADR for that engine. Nothing above `Quotinator.Data` changes.

### Outcomes

Each layer reports one of five statuses:
- **Passed.**
- **Failed.**
- **Inconclusive:** the layer's test ran and gave neither a pass nor a fail. An inconclusive layer is
  **never** a pass.
- **Not run:** an earlier layer failed or was inconclusive.
- **Skipped:** the depth excluded it, or, for Schema, migrations are still to run and will change the schema
  (below). A skip for a reason states it as the finding.

**Something the check could not do is a result, never an inconclusive one (developer, 2026-11-15).** A file
that could not be read is what Storage found: it fails, with the reason as its finding. Inconclusive is kept
for a test that ran and answered neither way.

**One exception to "the first failure stops the check": a directory that cannot be written.** Every later
layer only reads, and needs nothing written (below), so Storage reports that failure and the reading layers
still run, and the report can say whether the file itself is sound. A file that cannot be read stops the
check as usual.

### Only documented behaviour is relied on

**No layer, fixture or decision rests on engine behaviour that is not documented.** Where the engine's
documentation is silent, the check assumes the worst case, and this ADR's engine section records which case
that is. Measuring how long a layer takes is allowed, because a measured cost is not a behaviour the design
relies on. Measured figures are indicators only, never thresholds.

**Test fixtures damage a file only through its documented format:** bytes the format defines, never a
reliance on how the engine reacts to misuse.

### The check changes nothing

**A check leaves the database file and every file beside it exactly as it found them.** It writes nothing,
starts no recovery on the original, and holds no lock afterwards. **It needs no disk space either (developer,
2026-11-16).** Where a layer can only be run soundly once what the engine would recover is applied, that is
done in memory, from the files' documented formats, and the layer reads the result. A full volume therefore
never stops the check; it stops only what writes, such as a backup, through that operation's own obstacles.

### Who calls it, and at what depth

| Caller | Depth | What a failure does |
|---|---|---|
| Startup, before anything opens the database for writing, and again after creating or migrating it | Set by `Quotinator:DatabaseStartupCheck` (`Full` by default) | It is returned as an outcome (ADR 022) and degrades startup with a reason |
| A Reset | Always `Full` | It decides whether the old file is backed up or set aside (below) |
| The integrity endpoint | Always `Full` | It is reported layer by layer and never thrown, and it degrades the application as startup would |

**A failed check degrades the application at any point, not only at startup (developer, 2026-11-15).** When
the integrity endpoint's check of the main database finds a failure that would have stopped startup, or a
schema that is not intact, the application is degraded with the reason startup gives for the same failure. A
check that passes changes nothing, and the changelog database never degrades the application (ADR 018).

**A database is validated in the state it starts in (developer, 2026-11-15).** Startup takes one of three
paths, and the check follows each:

- **No database:** there is no file to check, so the check before writing establishes only that the data
  directory can be written. The database is created, then checked, then seeded.
- **An old database:** the check before writing stops startup on a failure in Storage to Indexes, the
  layers that judge the file itself. Schema is skipped, not compared: until the migrations run, the schema
  differs from this build's by design, so a comparison could only say so. After migrating, the check runs
  again, compares the schema, and that result is startup's.
- **An up-to-date database:** the check before writing compares the schema, and is startup's result.

**Schema is skipped only when migrations are still to run (developer, 2026-11-15, narrowed 2026-11-16).**
The layer reads the version tables first, through the check's own read-only connection. A table that is
missing or behind this build means the migrations about to run will change the schema, so a comparison now
could only report differences they will remove: the layer reports itself skipped and names the table, the
recorded version and this build's. T1 found why this matters: comparing before migrating, then disregarding
the result, logged a certain and harmless difference as an error. **A table ahead of this build does not
skip it.** Nothing will change that schema, and comparing it is what shows what the overshoot consists of:
objects a newer build added are reported as extras, which leave it intact, and objects it dropped or changed
are named. The overshoot is still the fault the application degrades with (#350); the comparison is its
detail. A database with no version tables is always compared. The startup initialisation's own schema
comparison (#305) follows the same rule.

**`Fast` gives up Indexes and References.** An index that disagrees with its table, a broken unique
constraint or a dangling reference is then not detected at startup. A Reset and the endpoint still detect
them, because they always run `Full`.

**The changelog database keeps its own fallback.** A failed check of the changelog database falls back to
the JSON files and never degrades the main database (ADR 018).

**References does not degrade startup until enforcement is explicit.** Every other layer's failure
degrades startup, and so does an inconclusive check. References is the exception for now: a violation is
logged and reported, but startup stays healthy. The reason is that a database the application wrote itself
may hold violations while foreign key enforcement depends on how the native library was compiled. Once #493
makes enforcement explicit on every connection, a violation can only mean damage, and References degrades
startup like the other layers.

### Which way a failure sends a Reset

A Reset backs up a sound copy and sets aside a file that is not one (ADR 025, ADR 027).

- **Not a sound copy, so set aside:** Storage could not read the file, or Format, Journal, Pages or Indexes
  failed or was inconclusive. An inconclusive layer is not proof of a sound copy. Setting a file aside moves
  it, so it needs no free space.
- **A sound copy, so backed up:** only References or Schema failed. Their data can still be read and
  restored, even though the database cannot be used as it is.
- **No room for the backup of a sound copy (developer, 2026-11-16).** With no room there can be no backup,
  so a Reset refuses as it does for any backup obstacle (ADR 025), says why, and proceeds only once the
  operator confirms with `allowNoBackup`, without a restore point.

## The SQLite implementation

This section is specific to SQLite. It holds every engine fact the check relies on, with the page that
documents it (read 2026-11-14), and every case that page leaves open, with the worst case assumed.

### How each layer is checked

| Layer | How SQLite checks it | Documented in |
|---|---|---|
| Storage | File system calls and a write probe in the directory | (not an engine fact) |
| Format | The 100-byte header begins with the 16 bytes `SQLite format 3\0`. The page size (2 bytes, big-endian, at offset 16) is a power of two from 512 to 32768, or 1 for 65536. | [fileformat.html §1.3](https://www.sqlite.org/fileformat.html#the_database_header) |
| Journal | When a `-wal` of non-zero length exists, its 32-byte header holds: the magic `0x377f0682` or `0x377f0683`, version 3007000, a page size equal to the database's, and valid checksums over its first 24 bytes, in the byte order the magic selects | [fileformat.html §4.1](https://www.sqlite.org/fileformat.html#wal_file_format), [§4.2](https://www.sqlite.org/fileformat.html#checksum_algorithm) |
| Pages | `PRAGMA quick_check`. A sound result is the single row `ok`. | [pragma.html](https://www.sqlite.org/pragma.html#pragma_quick_check) |
| Indexes | `PRAGMA integrity_check`. Unlike `quick_check`, it verifies UNIQUE constraints and that index content matches table content. | [pragma.html](https://www.sqlite.org/pragma.html#pragma_integrity_check) |
| References | `PRAGMA foreign_key_check`. `integrity_check` does not find foreign key errors. This pragma is documented without any condition on the `foreign_keys` setting, so the check never sets that. | [pragma.html](https://www.sqlite.org/pragma.html#pragma_foreign_key_check) |
| Schema | The comparison of the schema against this build's (#305), unless a version table is missing or behind this build | (project-defined) |

### Where the query layers run

- **No `-wal`, or an empty one:** the query layers run on a read-only `immutable` open of the main file.
  [uri.html](https://www.sqlite.org/uri.html) documents that `immutable` skips all locking and change
  detection.
- **A `-wal` holding data:** the `-wal` is part of the database's persistent state
  ([wal.html](https://www.sqlite.org/wal.html)), so the query layers run on an in-memory image of the
  database with the `-wal`'s committed frames applied. Both files are read into memory, never opened by
  SQLite, and nothing is written to disk:
  - **Which frames count** ([fileformat.html §4.1](https://www.sqlite.org/fileformat.html#wal_file_format)):
    a frame is valid if and only if its salts match the `-wal` header's, and its checksum matches the one
    computed consecutively over the header's first 24 bytes and the first 8 bytes and content of every
    frame up to and including it (§4.2's algorithm). A frame whose second field is not zero is a commit
    record, and that field is the database's size in pages after the commit.
  - **Which version of a page is read** ([§4.5](https://www.sqlite.org/fileformat.html#reader_algorithm)):
    "the last valid instance of page P that is followed by a commit frame or is a commit frame itself";
    otherwise the page from the main file. The image is sized to the last commit's page count.
  - **Opening it:** header bytes 18 and 19 are set to 1, as
    [c3ref/deserialize.html](https://www.sqlite.org/c3ref/deserialize.html) documents for a database in WAL
    mode, which otherwise fails with `SQLITE_CANTOPEN`, and the image is opened read-only with
    `sqlite3_deserialize`.
  - **What it costs:** memory for the database and its `-wal` while the check runs.

### Not documented, and the worst case assumed

- **What an `immutable` open does with an existing `-wal`.** *Worst case:* it ignores the `-wal`, and
  transactions committed before a crash are missed. So no query layer runs on an `immutable` open while
  the `-wal` holds data.
- **Whether a reader stops at the first invalid frame.** *Worst case:* it does not, and a later frame is
  read. So the image takes exactly what §4.1 and §4.5 define, and no frame is used unless it is valid by the
  consecutive checksum, which includes every frame before it.
- **Whether the bundled library includes `sqlite3_deserialize`.** It "is omitted if SQLite is compiled
  with the SQLITE_OMIT_DESERIALIZE option", and nothing documents how the bundled library was compiled. A
  test proves it is present.
- **What SQLite does with a `-wal` whose header is invalid.** *Worst case:* it discards the `-wal`
  silently, losing committed transactions. So an invalid `-wal` header fails Journal, and the file is not a
  sound copy.
- **A `-wal` whose page size differs from the database's.** *Worst case:* it belongs to another database.
  So Journal fails.
- **Whether a file longer or shorter than its in-header size is valid.** The in-header size (offset 28)
  is valid only if it is non-zero and the change counter (offset 24) equals the version-valid-for number
  (offset 92). Otherwise SQLite takes the size from the file itself
  ([fileformat.html §1.3.7](https://www.sqlite.org/fileformat.html#in_header_database_size)). Neither
  length mismatch is documented as a fault, so Format makes no claim from the file's length.
  - A truncated file is expected to fail at Pages, and its test confirms that against a file whose
    in-header size is valid.
  - **The limit:** when the in-header size is not valid, no documented check guarantees that a truncation
    is detected.
- **Whether foreign key enforcement is on.** SQLite documents it as off unless the library is compiled
  with `SQLITE_DEFAULT_FOREIGN_KEYS` ([foreignkeys.html](https://www.sqlite.org/foreignkeys.html)). The
  provider sends no `PRAGMA foreign_keys` unless the connection string names it
  ([Microsoft.Data.Sqlite connection strings](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings)).
  *Worst case:* a database the application wrote itself holds dangling references, which is why References
  does not yet degrade startup (#493).
- **A deliberate way to damage a file.** [howtocorrupt.html](https://www.sqlite.org/howtocorrupt.html)
  describes accidental causes only, and `sqlite_dbpage`, which can write any page, is not compiled in by
  default ([dbpage.html](https://www.sqlite.org/dbpage.html)). So test fixtures write bytes the file format
  defines.

## Consequences

- **One judgement everywhere.** Startup, a Reset and the endpoint can no longer disagree about a file. A
  failure names its layer, so the degraded reason, the endpoint and a future Maintenance page (#491) can
  each say what is wrong.
- **Startup costs more.** A `Full` check of the bundled databases takes milliseconds. Larger databases and
  slower storage cost more, which is what `Fast` is for. Its cost is that some damage goes undetected until
  a Reset or the endpoint looks.
- **The check needs memory, never disk space.** When a `-wal` still holds data, after an unclean stop or
  while the application runs, the check holds the database and its `-wal` in memory. A full volume does not
  stop it, so a full volume is reported by what it actually stops: a backup.
- **The engine's documentation is pinned.** A newer engine release that documents more may allow a layer
  to be narrowed or a worst case to be dropped. That is a change to this ADR's engine section, made only
  once the new behaviour is documented.
- **A new check of database soundness belongs in a layer,** not in a parallel check beside this one.
