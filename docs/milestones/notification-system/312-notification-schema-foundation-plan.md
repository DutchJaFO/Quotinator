# #312 — Notification schema: title/body, typed metadata, optional expiry, and app-version provenance

**Status:** In progress (step 5)
**GitHub issue:** #312 (open)
**Depends on:** #278 (done, released v1.8.0 — the mechanism this issue reshapes)

---

## Background

#278 shipped the notification mechanism as a deliberately basic first pass: a `Type`, one `Message`
string, and an always-populated expiry. This milestone makes it complete and useful for the persisted
notifications the migration/reset/reseed/import paths need. This issue is the foundation the rest of the
milestone builds on — #81, #302, #303, #304 and #308 all write or render notifications and all depend on
this shape.

The concrete trigger: two workarounds already in the codebase. `NotificationSeeding.SeedOnceAsync`
dedupes by checking whether a magic prefix appears inside the human-readable message text
(`"WhatsNew:v1.8.3: …"`), and #81's `unreleased` handling extended that trick with an embedded content
hash because unreleased content has no version to key on. Both exist only because there is nowhere
structured to put structured data.

## Authoritative-source cross-check

Checked before designing anything below.

- **ADR 008** (enum-backed columns require CHECK) — governs `MetadataKind`. Confirms two things this
  design relies on: (a) the `CHECK` may be declared **inline on `ALTER TABLE … ADD COLUMN`**, verified
  in that ADR against SQLite's own `lang_altertable.html`, so introducing this column needs no table
  rebuild; (b) its distinguishing question is *who defines the set of possible values*, not which
  project the table lives in. `MetadataKind`'s values are defined by this project's own producers, not
  by an unknown future consumer — the same shape as the already-shipped
  `NotificationDismissTrigger.DatabaseReset`, a `Quotinator.Data`-owned enum whose value is acted on by
  `Quotinator.Api`. So a closed enum with a `CHECK` is correct here, matching that precedent rather than
  the open-vocabulary `EntityType`/`BatchId` case ADR 008 contrasts it against.
  **Known cost, accepted:** adding a future kind needs a Data-side enum member *and* a migration to
  widen the `CHECK` (SQLite cannot widen an existing `CHECK` in place — that needs a table rebuild).
  #304 already carries exactly this cost for its own new `NotificationDismissTrigger.Reseed` member, so
  the pattern and its price are already established within this milestone.
- **ADR 002** (RecordBase on all tables, without exception) — `System_Notification` and
  `System_AppVersion` both already carry it; no new table is introduced by this issue.
- **ADR 015** (domain-prefixed table naming) — both affected tables already use the `System_` prefix
  correctly. No rename.
- **ADR 016** (class naming suffixes and enum placement) — `MetadataKind` goes in
  `src/Quotinator.Data/Enums/`, not alongside the entity. `NotificationResponse` keeps its `Response`
  suffix; its `Message` property is renamed to `Body` in step with the column.
- **ADR 018** (system content is `Quotinator.Data`-owned) — notifications are system content; the schema,
  the enum, and the relocated dedupe helper all belong in `Quotinator.Data`. This is what makes step 6's
  relocation correct rather than merely convenient.
- **ADR 009** (verify migrations against the last released schema) — applies at this issue's own
  verification stage; the last released schema is v1.8.3's.
- **CLAUDE.md's schema migration policy** — migrations are append-only and never edited once applied to
  a real database. Migration *count* is deliberately not optimised here; the milestone consolidates
  before release, per the developer's own direction.

No conflict found — proceeding with the design below.

## Design

### `System_Notification` — new shape

| Column | Change | Notes |
|---|---|---|
| `Title` | **new**, nullable | Short headline. Nullable so existing rows stay valid without backfilling invented text. |
| `Message` → `Body` | **renamed** | `ALTER TABLE … RENAME COLUMN`, supported natively — no rebuild, no data copy. |
| `Metadata` | **new**, nullable | Free-form producer-owned JSON. |
| `MetadataKind` | **new**, nullable, `CHECK` | Names what shape `Metadata` holds. Nullable — a notification with no metadata has no kind. |
| `AppVersionId` | **new**, nullable FK | → `System_AppVersion.Id`. The app version that *added* this notification. Nullable for pre-existing rows. |

`MetadataKind` is deliberately independent of `Type`: `Type` is severity
(`Information`/`Warning`/`Error`/`Success`/`ActionRequired`), `MetadataKind` is payload shape. The same
shape can appear under different severities, and one severity can carry several shapes. Conflating them
would force a new `Type` member every time a producer needed a new payload.

`Metadata` carries one reserved key, `dedupeKey`, read by the shared dedupe helper regardless of kind.
Everything else in the object is the producing feature's own business — including, for #81, which
version a what's-new notification is *about* (distinct from the provenance FK, which is always the
version that wrote the row).

### `System_AppVersion` — append-only history

Currently a single row upserted in place (added by #81, to answer "what version ran last"). Two changes:

- Gains an **`Application`** column, **separate from `Version`** — never a single concatenated value.
- Becomes **append-only**: one row per distinct `Application`+`Version` ever seen, rather than one row
  overwritten on every upgrade.

This is what makes the provenance FK meaningful. Against the current single-row table, an FK would
silently change meaning the moment the app upgraded and the row was overwritten — every historical
notification would start claiming it came from the new version. Append-only freezes it correctly, and
as a side effect gives a real record of which application versions have accessed this database.

#81's own tracker changes from upsert to append-if-changed; its "last active version" lookup becomes
"the most recent row" rather than "the row".

### Expiry becomes opt-in

`INotificationWriter.WriteAsync` currently applies `Quotinator:NotificationDefaultExpiryHours` whenever
no explicit value is passed, so *every* notification silently expires. A notification about a real,
unresolved condition should not vanish on a timer. After this issue: no expiry unless a producer asks
for one. Existing rows are untouched, and the config key stays for producers that genuinely want it
(#302/#303's reseed-result notifications are the likely users — their own staleness story is theirs to
settle, not pre-decided here).

### Dedupe helper relocation

`NotificationSeeding.SeedOnceAsync` lives in `Quotinator.Api.Startup`, unreachable from
`Quotinator.Core` where #302/#303/#304's producers need it (`Quotinator.Api` → `Quotinator.Core`, never
the reverse). A shared version moves to `Quotinator.Data`, and its check changes from
`Message.Contains(key)` to the structured `Metadata.dedupeKey`. This resolves, once, a decision that
#302, #303 and #304 each currently defer to their own planning phase.

### Action parameters

`INotificationActionExecutor.ExecuteAsync(NotificationDismissTrigger)` receives only the trigger, so an
action cannot be parameterised — `Reseed` can only ever mean the whole database, never "reseed *this*
file". It gains access to the originating notification's metadata. This changes a signature shipped in
#278.

### Not built here: transient notifications

A later milestone wants non-persisted notifications (progress/status for long-running UI-triggered
tasks). Not in scope. The constraint this issue accepts is narrower: the reader and rendering contracts
must not hard-assume every notification is a database row, so that work is an extension rather than a
rewrite.

---

## Steps

### 1. Plan doc, slnx
**Status:** ✅ Done

Written alongside the issue itself, and added to `Quotinator.slnx` under
`/docs/milestones/notification-system/`.

### 2. `MetadataKind` enum + `System_Notification` schema migration
**Status:** ✅ Done — **merged with step 4** (see below)

`NotificationMetadataKind` (`Quotinator.Data.Enums`, per ADR 016) with three members —
`Announcement`, `SchemaVersionOvershoot`, `WhatsNew` — covering the producers that exist today (#279,
#289, #81). Data-owned migration 5 (`NotificationSchemaMigrations.SplitMessageAndAddMetadata`) does the
whole reshape in five `ALTER TABLE` statements, no rebuild, with `MetadataKind`'s `CHECK` declared
inline on `ADD COLUMN` exactly as ADR 008 verified is permitted. `RegisterEnumHandler<NotificationMetadataKind>()`
added to `DatabaseConfiguration.Configure()`.

**Steps 2 and 4 were done as one unit, not separately — a column rename cannot land half-done.** The
plan listed the schema (step 2) and the entity/writer/reader/REST updates (step 4) as separate steps,
but `RENAME COLUMN Message TO Body` breaks `Sql.Notifications`, `NotificationEntity`,
`NotificationWriter`, `NotificationSeeding`, `NotificationTable.razor` and `ToResponse` the instant it
applies. Splitting them would have produced an intermediate commit that does not build. They are
therefore one commit; the step numbering is left as-is rather than renumbered, since the plan's
*content* was right and only its commit granularity was wrong.

**Baseline column order is deliberately untidy, and the parity test is why.** `ADD COLUMN` always
appends, so the incremental path leaves `Title`/`Metadata`/`MetadataKind`/`AppVersionId` *after*
`IsDeleted`. `DumpTableSchemaAsync` compares each column's ordinal, so the baseline had to reproduce
that real result rather than a prettier grouping. Verified by running the parity test, not by
reasoning — `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` passes.
The milestone's end-of-cycle migration consolidation is what restores a clean ordering.

### 3. `System_AppVersion`: `Application` column + append-only conversion
**Status:** ✅ Done

Two Data-owned migrations, per CLAUDE.md's "one schema change per migration where possible" — both in
`AppVersionHistoryMigrations`. Migration 6 adds the `Application` column and a unique index over
`(Application, Version)`; migration 7 adds `SequenceNumber` and its own uniqueness index. The indexes
are what make the table an append-only history, rather than a convention the tracker merely follows.
`AppVersionEntity` gains both columns; the baseline was updated to match, with both trailing after
`IsDeleted` for the same `ADD COLUMN`-appends reason step 2 documents.

`IAppVersionTracker` changed shape, not just behaviour: both methods now return an `AppVersionRecord`
(`Id`, `Application`, `Version`) rather than a bare version string, because step 4's provenance FK needs
the row's id and nothing else could supply it. `RecordCurrentAsync(application, version)` is
append-if-new — an identical pair returns the existing row. `IVersionService` gains `Application`,
resolved from the entry assembly rather than hardcoded, since `Quotinator.Tools.DbInspector` opens the
same database.

`Application` is nullable rather than `NOT NULL DEFAULT`: rows written by #81's version-only tracker
genuinely predate the concept, and inventing a name for them would fabricate history. Those rows stay
readable and simply never match a lookup, so the first boot after this migration appends a properly
attributed row instead of retroactively claiming the legacy one.

**A real ordering defect surfaced here, caught by a test rather than by inspection — and its first fix
was wrong.** `Sql.AppVersion.SelectMostRecent` originally tie-broke on `Id DESC`. `DateCreated` is
stored at second resolution (`SafeDateHandler`'s formats), so rows written inside the same second all
carry an identical timestamp and the tie-break decided the answer — with a random GUID. The test failed
in the full-suite run and passed in isolation, purely on where the second boundary fell.

The first fix ordered on SQLite's implicit `rowid`, which is correct today but is an implementation
detail whose values become reusable once a table's highest row is removed — so a future change to how
this table is pruned would corrupt the ordering silently rather than fail. **The developer rejected it:
`rowid` cannot be trusted to stay sane, and an explicit column is the right answer.** Migration 7 adds
`SequenceNumber`, assigned by the tracker as `MAX + 1` inside the insert's own transaction and covered
by a uniqueness index, so a concurrent write fails loudly instead of producing two rows claiming the
same position.

`SequenceNumber` rather than `OrderId`, for a mechanical reason: `SqlSelectPresentationGuard` requires
every `*Id`-suffixed column in a `SELECT` list to be `LOWER()`-wrapped and carries exactly one exemption
in the whole codebase, so an INTEGER `OrderId` would have had to become the second — an exemption from a
casing rule that only means anything for text ids.

`SelectMostRecent` now orders on `SequenceNumber` alone; `DateCreated` is not consulted at all, so its
resolution cannot make the answer arbitrary again. The migration's backfill does read `rowid`, and that
is deliberate and bounded: once, at migration time, to give pre-existing rows the only insertion-order
signal they carry — a read at a known-sane moment seeding a column authoritative from then on, not an
ongoing dependency. Three tests pin the result: several versions written inside one timestamp, each call
taking the next number, and a row with a far-future `DateCreated` but a lower sequence that must not win.

### 4. Entity, writer, reader, and REST response updates
**Status:** ✅ Done — landed with step 2 (see above for why they are inseparable)

`NotificationEntity` gains `Title`/`Metadata`/`MetadataKind`/`AppVersionId` and renames `Message`→`Body`;
`Sql.Notifications.SelectColumns` updated (with `AppVersionId` wrapped in `IdClauses.SelectColumn`, per
the project's read-time id-presentation rule); `INotificationWriter.WriteAsync` takes the new fields and
**no longer defaults the expiry**; `NotificationResponse`/`ToResponse` expose `title`/`body`/`metadata`/
`metadataKind`.

**Three test fixtures were genuinely wrong afterwards, each for a different reason — all found by
running the suite, none by inspection:**

1. `NotificationReaderTests`/`NotificationWriterTests` built their table from
   `NotificationMigrations.CreateNotificationTable` alone — the v1.8.0 shape. They now replay the real
   sequence (v1.8.0 create → #81's `System_AppVersion` → #312's reshape), which is both correct and
   more honest about what a real database contains.
2. `DowngradeToLegacyNamesAsync` (`Quotinator.Core.Tests`) reaches its "legacy" state by running the
   *full* migration chain and then undoing parts of it — but it never undid migration 5's rename, so
   the replay hit `no such column: "Message"` trying to rename `Body` again. Fixed by dropping
   `System_Notification` and `System_AppVersion` in that helper, exactly matching the existing
   precedent its own comment already documents for `Audit_Change`/`Import_Conflict`/`Import_Action`/
   `Import_SourceFileOverride`, and for the identical reason: `ALTER TABLE … RENAME` is not idempotent.
3. `WriteAsync_NoExpirySpecified_AppliesConfiguredDefault` asserted the *old* always-expire behaviour.
   Rewritten as `WriteAsync_NoExpirySpecified_DoesNotExpire`, and strengthened while there — it now
   re-reads the stored row rather than trusting the returned entity, since "defaulted on write" and
   "defaulted on read" are different bugs and only the stored row distinguishes them.

Migration-count assertions in three pre-existing tests moved 4 → 5, as with every prior Data migration.

### 5. Relocate the dedupe helper into `Quotinator.Data`, keyed on `Metadata.dedupeKey`
**Status:** Not started

### 6. Migrate #279's and #289's shipped producers onto the new shape
**Status:** Not started

### 7. `INotificationActionExecutor` gains metadata access
**Status:** Not started

### 8. Tests
**Status:** Not started

### 9. Full verification (T1, T2)
**Status:** Not started

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `System_Notification` gains `Title`, `Body` (renamed from `Message`), `Metadata`, `MetadataKind`, `AppVersionId` | Unit test | Schema-shape test against a migrated database |
| 2 | ❌ | Baseline and incremental replay produce an identical `System_Notification` schema | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` (existing test, must still pass) |
| 3 | ❌ | `MetadataKind`'s `CHECK` accepts every enum member and rejects an unknown value, on both the baseline and incremental paths | Unit test | Behavioural round-trip, matching the existing `AcceptSameNotificationCheckConstraintValues` pattern |
| 4 | ❌ | A notification written with no explicit expiry has no expiry | Unit test | `NotificationWriter` test |
| 5 | ❌ | A notification written with an explicit expiry keeps it | Unit test | `NotificationWriter` test |
| 6 | ✅ | `System_AppVersion` stores `Application` and `Version` as separate columns | Unit test | `AppVersionTrackerTests.RecordCurrentAsync_FirstCall_StoresApplicationAndVersionSeparately` — asserts the two stored columns directly, not just the reassembled record; schema parity covered by `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAppVersionSchema` |
| 7 | ✅ | Recording the same application+version twice appends no second row; a different version appends a new one | Unit test | `AppVersionTrackerTests.RecordCurrentAsync_SamePairTwice_AppendsOnlyOnce`, `..._NewVersion_AppendsWithoutOverwritingHistory`, `..._SameVersionDifferentApplication_AppendsSeparately` |
| 8 | ✅ | "Last active version" resolves to the most recent row, not an overwritten single row | Unit test | `AppVersionTrackerTests.GetLastActiveAsync_SeveralVersionsWithinOneTimestamp_ReturnsTheOneWrittenLast`, `RecordCurrentAsync_EachCall_TakesTheNextSequenceNumber`, `GetLastActiveAsync_RowWithNewerTimestampButOlderSequence_DoesNotWin` |
| 9 | ❌ | A notification's `AppVersionId` still points at the version that wrote it after the app version changes | Unit test | Real-SQLite provenance test — write, record a newer version, re-read |
| 10 | ❌ | Dedupe matches on `Metadata.dedupeKey`, not on message text | Unit test | Relocated helper's own tests |
| 11 | ❌ | A dedupe key that appears in body text but not in metadata does **not** suppress a write | Unit test | Guards against the old message-substring behaviour surviving accidentally |
| 12 | ❌ | #279's and #289's producers still write exactly once across restarts after migration | Unit test | Their existing regression tests, updated |
| 13 | ❌ | `INotificationActionExecutor.ExecuteAsync` receives the originating notification's metadata | Unit test | `NotificationActionExecutorTests` |
| 14 | ❌ | `GET /api/v1/notifications` returns `title`/`body`/`metadata`/`metadataKind`, and no longer returns `message` | Unit test | Endpoint test asserting the live response shape |
| 15 | ❌ | Migration applies cleanly to a real copy of the last released (v1.8.3) database | Live (T2) | ADR 009 verification against a v1.8.3 database |
| 16 | ❌ | T1 — app starts in Visual Studio with no error; `/notifications` renders migrated rows correctly | Live (T1) | Developer confirms in Visual Studio |
| 17 | ❌ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 18 | ❌ | Full test suite green | Build | `dotnet test --configuration Release` |

---

## Relationship to existing issues

- **#278** — the shipped mechanism this issue reshapes; its `Message`-based dedupe, always-on expiry,
  and unparameterised action executor are all replaced here.
- **#279, #289** — the two shipped producers migrated onto the new shape by step 6.
- **#308** — renders the richer content this issue introduces; its own body currently states "no change
  to `NotificationEntity.Message`'s storage shape", which this issue supersedes.
- **#81** — its `System_AppVersion` table is extended here (append-only, `Application` column), and its
  producer moves off the message-prefix/content-hash dedupe onto `Metadata`.
- **#302, #303, #304** — each currently defers the "relocate the dedupe helper" decision to its own
  planning phase; step 5 settles it once for all three. #304's own `Reseed` trigger additionally
  benefits from step 7's action parameters.
- **#305** — independent; no interaction.
