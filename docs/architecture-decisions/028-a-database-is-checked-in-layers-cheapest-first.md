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
| Storage | Is the file there, can it be read, can its directory be written, and is there room for what checking it needs? | Yes |
| Format | Is it a database of this engine, by its documented file format? | Yes |
| Journal | Is the engine's write-ahead file, if there is one, valid by its documented format? | Yes |
| Pages | Can every page be read, and is every record well-formed? | Yes |
| Indexes | Does every index agree with its table, and does every unique constraint hold? | No |
| References | Does every declared reference point at a row that exists? | No |
| Schema | Does the schema match this build's, once the database records this build's version? | Yes |

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
- **Skipped:** the depth excluded it, or, for Schema, the comparison cannot mean anything yet (below). A
  skip for a reason states it as the finding.

**Something the check could not do is a result, never an inconclusive one (developer, 2026-11-15).** A copy
that could not be made, or a directory without room for one, is what Storage found: it fails, with the reason
as its finding. Inconclusive is kept for a test that ran and answered neither way.

**One exception to "the first failure stops the check": a directory that cannot be written or has no room,
when the reading layers need nothing written.** Every later layer only reads, so Storage reports that failure
and the reading layers still run, and the report can say whether the file itself is sound. When the reading
layers can run only on a copy, and the copy cannot be made, they do not run. A file that cannot be read stops
the check as usual.

### Only documented behaviour is relied on

**No layer, fixture or decision rests on engine behaviour that is not documented.** Where the engine's
documentation is silent, the check assumes the worst case, and this ADR's engine section records which case
that is. Measuring how long a layer takes is allowed, because a measured cost is not a behaviour the design
relies on. Measured figures are indicators only, never thresholds.

**Test fixtures damage a file only through its documented format:** bytes the format defines, never a
reliance on how the engine reacts to misuse.

### The check changes nothing

**A check leaves the database file and every file beside it exactly as it found them.** It writes nothing,
starts no recovery on the original, and holds no lock afterwards. Where a layer can only be run soundly by
letting the engine recover something, it runs on a copy. Making that copy is Storage's: the room it needs is
measured before it is attempted, and a copy that cannot be made, for want of room or a writable folder, or
because the operating system refused it, is a Storage failure.

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

**Schema is compared only when every version table records this build's version (developer, 2026-11-15).**
The layer reads the version tables first, through the check's own read-only connection. A table that is
missing, behind this build or ahead of it means the comparison cannot mean anything yet, so the layer
reports itself skipped and names the table, the recorded version and this build's. Ahead of this build, the
schema is one the build does not know, the case #350 degrades on. A database with no version tables is always
compared. T1 found why this matters: comparing before migrating, then disregarding the result, logged a
certain and harmless difference as an error.

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
- **Storage found no room, so the backup cannot be taken either (developer, 2026-11-15).** Room for the
  check's copy is room a backup needs too, so a Reset refuses as it does for any backup obstacle (ADR 025):
  the operator frees space, or retries with `allowNoBackup` and proceeds without a restore point, with a
  warning. With the override, a file whose reading layers ran and passed is replaced as a sound copy is
  without a backup; one whose reading layers could not run is set aside, since nothing showed it sound, and
  moving it needs no room. The refusal is the expected path, since the room is measured first; an operating
  system that refuses the copy anyway, because something else took the space or the need was underestimated,
  is answered the same way.

## The SQLite implementation

This section is specific to SQLite. It holds every engine fact the check relies on, with the page that
documents it (read 2026-11-14), and every case that page leaves open, with the worst case assumed.

### How each layer is checked

| Layer | How SQLite checks it | Documented in |
|---|---|---|
| Storage | File system calls, a write probe in the directory, and the volume's free space measured against the copy the query layers need when the `-wal` holds data (below) | (not an engine fact) |
| Format | The 100-byte header begins with the 16 bytes `SQLite format 3\0`. The page size (2 bytes, big-endian, at offset 16) is a power of two from 512 to 32768, or 1 for 65536. | [fileformat.html §1.3](https://www.sqlite.org/fileformat.html#the_database_header) |
| Journal | When a `-wal` of non-zero length exists, its 32-byte header holds: the magic `0x377f0682` or `0x377f0683`, version 3007000, a page size equal to the database's, and valid checksums over its first 24 bytes, in the byte order the magic selects | [fileformat.html §4.1](https://www.sqlite.org/fileformat.html#wal_file_format), [§4.2](https://www.sqlite.org/fileformat.html#checksum_algorithm) |
| Pages | `PRAGMA quick_check`. A sound result is the single row `ok`. | [pragma.html](https://www.sqlite.org/pragma.html#pragma_quick_check) |
| Indexes | `PRAGMA integrity_check`. Unlike `quick_check`, it verifies UNIQUE constraints and that index content matches table content. | [pragma.html](https://www.sqlite.org/pragma.html#pragma_integrity_check) |
| References | `PRAGMA foreign_key_check`. `integrity_check` does not find foreign key errors. This pragma is documented without any condition on the `foreign_keys` setting, so the check never sets that. | [pragma.html](https://www.sqlite.org/pragma.html#pragma_foreign_key_check) |
| Schema | The comparison of the schema against this build's (#305), once every version table records this build's version | (project-defined) |

### Where the query layers run

- **No `-wal`, or an empty one:** the query layers run on a read-only `immutable` open of the main file.
  [uri.html](https://www.sqlite.org/uri.html) documents that `immutable` skips all locking and change
  detection.
- **A `-wal` holding data:** the database and its sidecars are copied, and the copy is opened normally, so
  SQLite recovers the copy's `-wal`. [wal.html](https://www.sqlite.org/wal.html) documents three relevant
  facts:
  - the `-wal` is part of the database's persistent state;
  - it must be kept with the database when the database is copied;
  - the first connection to open the database runs that recovery.

  The originals are never opened.
- **The copy goes in a reserved folder inside the data directory,** never the system temporary folder.
  SQLite locks the files it opens, and the Home Assistant add-on's AppArmor profile grants the system
  temporary folder read and write but not file locking. The room it needs, the database and its `-wal`
  together, is measured against the volume's free space before the copy is made. Too little room, a folder
  that cannot be written, or a copy the operating system refuses fails Storage, with what refused it as the
  finding, and the query layers do not run.

### Not documented, and the worst case assumed

- **What an `immutable` open does with an existing `-wal`.** *Worst case:* it ignores the `-wal`, and
  transactions committed before a crash are missed. So no query layer runs on an `immutable` open while
  the `-wal` holds data.
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
- **The check may need disk space.** A copy is needed only when a `-wal` still holds data, which happens
  after an unclean stop or while the application runs. With too little room, Storage fails: startup degrades
  rather than opening the database unchecked, the endpoint reports it and degrades the application, and a
  Reset refuses unless the operator accepts going without a backup.
- **The engine's documentation is pinned.** A newer engine release that documents more may allow a layer
  to be narrowed or a worst case to be dropped. That is a change to this ADR's engine section, made only
  once the new behaviour is documented.
- **A new check of database soundness belongs in a layer,** not in a parallel check beside this one.
