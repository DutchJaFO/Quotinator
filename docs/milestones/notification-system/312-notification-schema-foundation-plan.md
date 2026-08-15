# #312 — Notification schema: title/body, typed metadata, optional expiry, and app-version provenance

**Status:** Planning
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
**Status:** Not started

### 2. `MetadataKind` enum + `System_Notification` schema migration
**Status:** Not started

`Title`, `Message`→`Body`, `Metadata`, `MetadataKind` (inline `CHECK`), `AppVersionId`. Baseline SQL
updated in the same commit, per the schema-drift parity rule.

### 3. `System_AppVersion`: `Application` column + append-only conversion
**Status:** Not started

Includes revising #81's `AppVersionTracker` from upsert to append-if-changed, and its "last active
version" read to "most recent row".

### 4. Entity, writer, reader, and REST response updates
**Status:** Not started

`NotificationEntity`, `INotificationWriter.WriteAsync` (opt-in expiry, new fields), `NotificationReader`,
`NotificationResponse`/`ToResponse` (`Message`→`Body`, plus `title`/`metadata`/`metadataKind`).

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
| 6 | ❌ | `System_AppVersion` stores `Application` and `Version` as separate columns | Unit test | Schema-shape test |
| 7 | ❌ | Recording the same application+version twice appends no second row; a different version appends a new one | Unit test | `AppVersionTrackerTests` |
| 8 | ❌ | "Last active version" resolves to the most recent row, not an overwritten single row | Unit test | `AppVersionTrackerTests` |
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
