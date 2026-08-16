# #312 — Notification schema: title/body, typed metadata, optional expiry, and app-version provenance

**Status:** Waiting for release
**GitHub issue:** #312 (open)
**Depends on:** #278 (done, released v1.8.0 — the mechanism this issue reshapes)

> **Next action: nothing until the release ships.** Every step and every verification row is green, and
> the changelog entries are written. The issue stays open until the tagged release actually goes out,
> per the two-gate closing rule — its Definition of done is ticked except the closing-comment box.

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

**`Metadata` is the notification's machine-readable "why", not a dedupe mechanism that happens to be
structured.** It has three consumers, and no reserved key of any kind:

1. **Identity** — whether this notification already exists (step 5).
2. **Presentation** — a renderer reads `MetadataKind` and the payload to show the notification *as what
   it is*: a what's-new entry can badge its version, a schema-overshoot warning can display both
   recorded versions as data rather than as a sentence. This is #308's input.
3. **Action parameters** — what an executable action operates on (step 7), so `Reseed` can mean
   "reseed *this* file" instead of only ever meaning the whole database.

**`Metadata` holds strictly non-text data** (developer direction, 2026-08-16) — structured values that
help render the notification and parameterise its actions: identifiers, version numbers, counts, ids.
Never user-facing prose, and never the notification's language. Anything textual, including the language
its text is written in, is a first-class column on the notification row instead. That boundary is what
keeps notification text translatable through the same mechanism quotes already use, rather than frozen
inside a JSON blob in whichever language was current at write time. The payloads this issue ships comply
by construction (`Announcement`, `Version`, the two schema versions are all identifiers and values), but
the rule is stated so the next producer does not put a message in there.

The original design here reserved a `dedupeKey` string for consumer 1. That was dropped: it was a
fossil of #278, where identity had to be a token findable inside prose. Encoding identity as a string
made the information *less* accessible than the data it was derived from — the reason a UI could not
tell why a notification existed without parsing its message text. Expressing identity as the payload's
own fields serves all three consumers from one representation. See step 5.

For #81 specifically, the payload's `Version` (which release the notification is *about*) stays
distinct from the provenance FK (which version *wrote* it) — a catch-up run writes several
notifications about different releases, all written by one version.

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

**Two codebase conventions were introduced alongside this step, both by developer decision, and both
recorded in CLAUDE.md rather than here.** Neither is specific to #312 — this issue is only where they
were first applied.

1. **`IDE0008` is now compiler-enforced per touched file.** The explicit-types boyscout rule was
   silently missed across this issue's first commits; a path-scoped `.editorconfig` section now fails
   the build instead of relying on memory. Scoped, never solution-wide — escalating globally surfaces
   14,286 warnings at once. The list is a ratchet: entries are only added, and once the remainder is
   small enough it moves solution-wide and the section is deleted.
2. **Column names in `Sql.*` come from `nameof(TEntity.Property)`.** `Sql.AppVersion` is the worked
   example. Boyscout-scoped like the above. Table names, Dapper parameter names, and migration SQL stay
   literal, each for a reason CLAUDE.md states.

Applying both to this issue's eight touched files cleared 426 `var` declarations that `dotnet format`
could not auto-fix, and a follow-on wave of `IDE0028`/`IDE0305` warnings that explicit types exposed
where `var` had been masking them.

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

### 5. Relocate the seeding helper into `Quotinator.Data`, identified structurally
**Status:** ✅ Done — **merged with step 6**, for the same reason steps 2 and 4 were merged: moving the
helper breaks every caller the moment it moves, so splitting them would leave an intermediate commit
that does not build.

`NotificationSeeding` moves to `Quotinator.Data.Notifications`. ADR 018 places notifications on this
side as system content; the practical driver is that `Quotinator.Core`'s own producers (#302/#303/#304)
cannot reach into `Quotinator.Api` — the dependency runs Api → Core, never the reverse — so a helper
left in the Api layer would have had to be reimplemented per producer. Those three issues each deferred
this decision to their own planning phase; it is settled here once.

**The `dedupeKey` string was dropped entirely, on developer direction mid-implementation.** The plan
called for keying on a `Metadata.dedupeKey` field, which fixed #278's false-substring match but kept the
fossil: callers still hand-composed `"WhatsNew:v" + version`. The developer's point was that a key had
to be a string only because it once lived inside message text, and that encoding identity as a string
makes the information *less* usable than the data it came from. Identity is now
`NotificationMetadataDto.Kind` plus each type's own `IdentityComponents`.

The objection to a fully structural comparison is that the read side must know every payload shape — it
does not, because `MetadataKind` is already a column. `NotificationMetadataKinds` maps kind → payload
type, so a stored row deserializes back into its producer's own type without the reader knowing who
wrote it. That is the round-trip the column exists to enable. `NotificationMetadataKindsTests` guards
the map: a new enum member without a registered type fails a test, because at runtime it would instead
be a silent bug — the row reads back unidentifiable and re-announces itself on every restart, with no
exception and no log line.

Payloads: `AnnouncementMetadataDto` (#279), `SchemaVersionOvershootMetadataDto` (#289, carrying both
recorded versions as data), `WhatsNewMetadataDto` (#81, `Version` for a tagged release or `ContentHash`
for `unreleased`). Comparison is case-insensitive on string components, per this project's rule for
identifier-valued comparisons.

### 6. Migrate #279's and #289's shipped producers onto the new shape
**Status:** ✅ Done — landed with step 5 (see above).

All three producers (#279, #289, #81) now build a typed payload instead of a message string, and all
three stamp `AppVersionId`. That required moving `RecordCurrentAsync` **ahead** of the producers in
`Program.cs`, where it previously followed them: a foreign key cannot reference a row that does not
exist yet. Safe because `lastActiveVersion` is captured earlier still, before migrations run, so #81's
catch-up range is unaffected by recording the current version sooner.

#81 also loses its trailing-delimiter workaround — the delimiter existed solely to stop
`WhatsNew:v1.9.1` matching inside `WhatsNew:v1.9.10`, and comparing version values structurally has no
substring relationship to get wrong.

### 7. `INotificationActionExecutor` gains metadata access
**Status:** ✅ Done

`ExecuteAsync(NotificationDismissTrigger)` becomes
`ExecuteAsync(NotificationDismissTrigger, NotificationMetadataDto? metadata = null)`. `CanExecute` is
unchanged — whether an action exists at all is a property of the trigger, not of any one notification's
payload.

**The payload, not the `NotificationEntity`.** Passing the entity would have been simpler at the call
site, which already holds it, but this issue's own "not built here" section commits to transient,
non-persisted notifications remaining possible in a later milestone — a contract taking the entity
would hard-assume every notification is a database row, which is exactly what that section says not to
do.

`Notifications.razor.cs` resolves the payload via `NotificationMetadataKinds.TryDeserialize`, so the
page hands the action the producer's own type without knowing which producer wrote it.

**`DatabaseReset` ignores the payload today, and that is the honest state.** A schema-version overshoot
is resolved by truing up the whole database, so there is nothing for a payload to narrow. The parameter
exists so #304's `Reseed` — which genuinely needs "reseed *this* file" — is a new `case` in the existing
switch rather than an interface change rippling through every caller. Per the project's "prove it or
remove it" rule the seam is not taken on faith: `ExecuteAsync_WithMetadata_DeliversItAndStillPerformsTheAction`
asserts the payload arrives by reference, and `..._WithoutMetadata_StillPerformsTheAction` covers the
null case every pre-#312 row presents.

### 8. Tests
**Status:** ✅ Done — written alongside each step rather than batched here.

Steps 2/4, 3, 5/6 and 7 each landed with their own tests, which is why this step has no separate body:
`AppVersionTrackerTests` (append-only history, ordering), `NotificationSeedingTests` (structural
identity, real SQLite), `NotificationMetadataKindsTests` (the kind→type registry guard),
`NotificationWriterTests`/`NotificationReaderTests` (schema round-trip), `WhatsNewNotificationTests` and
`NotificationActionExecutorTests` (producers and the action seam).

### 9. Full verification (T1, T2)
**Status:** ✅ Done — ran twice, both tiers each time; every pass found real bugs, all fixed and
re-verified on the final build

**T2 / ADR 009 — migration path from a genuine v1.8.3 database.** `ghcr.io/dutchjafo/quotinator:1.8.3`
created the starting database (`schema created at baseline (data v3, app v5)`), then the current build
ran against that same volume: `applying 4 pending "Data" migration(s) (version 3 → 7)` →
`schema updated (data v7, app v5)`, no exception, and no repeat on restart. Deliberately not the
accumulated dev database, per ADR 009 and this project's "no smoke tests on a dev/shared DB" rule.

**The T2 pass found a real bug the entire unit suite missed.** Stored payloads read
`{"announcement":"GetAllImportBatches","Kind":0}` — `Kind` was an abstract property carrying
`[JsonIgnore]` on the base, but `System.Text.Json` reads attributes from the most-derived declaration,
so every derived override silently dropped it. The result duplicated the `MetadataKind` column inside
the payload, as a bare enum ordinal, which is exactly the "two copies that can disagree" the attribute
existed to prevent. No test caught it because round-tripping succeeded either way — the extra property
deserialized straight back into an ignored member. Only reading the stored bytes exposed it.

Fixed by removing the override entirely: `Kind` is now set through the base constructor, so there is no
derived declaration to lose the attribute and a new producer cannot reintroduce the bug.
`SerializedPayloadNeverContainsTheKindDiscriminator` asserts on the serialized text across every
registered kind — the assertion class that was missing.

Re-verified after the fix against a fresh v1.8.3 database: `{"announcement":"GetAllImportBatches"}`,
and the `AppVersionId` FK joins to `Quotinator.Api 1.8.3`. `System_AppVersion` holds exactly one row
(`Quotinator.Api | 1.8.3 | 1`) and a restart appends none.

**Smoke tests updated, not just added** (`docs/smoke-tests.md`): new section 39 covers the migration
path, stored payload, provenance join, append-only history, and structural dedupe. Section 33 was
**wrong** after this issue — it asserted `totalCount: 0` and "No production code path writes a real
notification yet", both untrue now that #279/#289/#81 are live producers; a fresh container carries
exactly one notification, verified.

**Found while verifying, routed to #308 rather than fixed here:** `NotificationTable.razor` renders
`<td>@notification.Body</td>`, and HTML collapses whitespace — so #81's newline-joined highlight lists
already render as a single run-on line. That is a live rendering bug, not only a cosmetic wish, and
#308 (multi-line/rich message layout) is where it belongs.

**T1 found a startup-killing bug that T2 structurally could not.** The app terminated during startup
with `no such column: Application`.

`Program.cs` read the last active version *before* running migrations — #81's original ordering, chosen
so a missing `System_AppVersion` table would read as null. #312 changed that query to select
`Application` and order by `SequenceNumber`, columns migrations 6 and 7 add. On a database where the
table already exists but those columns do not — **any database at data v4 or v5**, which is every
machine that ran a build between #81 and #312 — the query threw straight past the missing-table catch
and killed the process.

T2 could not have caught it: it upgrades from v1.8.3, where the table does not exist at all, so the
catch legitimately applies and the read returns null. **The gap was verifying only the last *released*
schema.** ADR 009 mandates that as a floor; unreleased intermediate versions exist on every developer
machine and needed covering too.

Fixed by moving the read to *after* migrations, which is the correct order rather than a workaround:
migrations 6 and 7 only add columns and backfill `SequenceNumber`, never touching a recorded `Version`,
so the answer is identical either side of them while only the later position is guaranteed a schema
matching the query. Still strictly before `RecordCurrentAsync`, which is what would overwrite it.
Widening the catch to swallow `no such column` was rejected — it would leave the same trap armed for
the next column added to this query, and CLAUDE.md's "no exception-based recovery" rule is precisely
about not inferring schema state from thrown exceptions.

Verified both directions in Docker against a database promoted to data v4: the pre-fix image reproduces
`Unhandled exception … no such column: Application`; the fixed image logs
`applying 3 pending "Data" migration(s) (version 4 → 7)` → `schema updated (data v7, app v5)` →
`Quotinator ready`, with the pre-existing row surviving (`NULL | 1.8.4 | 1`) and the current version
appended (`Quotinator.Api | 1.8.3 | 2`). Regression-guarded by
`AppVersionTrackerTests.GetLastActiveAsync_DatabaseAtPre312Shape_ReadsExistingRowOnceMigrated`, and by
new smoke-test section 39e, which exists specifically so the next migration touching a
read-before-migrate table verifies the intermediate state and not only the released one.

**T1's re-run then found a second defect, and this one affected every released install.** The
notifications page showed the #279 announcement **twice**.

Cause: #312 moved identity out of message text into structured metadata, so a row written before it
carries none, cannot be identified, and #279's producer writes a fresh copy on the first startup after
upgrading.

**The impact assessment was initially wrong, and the correction is the point.** A 45-second check
against a fresh v1.8.3 container returned zero notifications, which was reported as proof that no
released build writes one — making this look like development-machine debris. The developer challenged
it against a deployed instance that plainly showed an active notification. Re-testing with a longer wait
showed v1.8.3 *does* write it: the producer runs after first-boot seeding of ~800 quotes, so the earlier
check simply ran too early. **Every existing v1.8.3 install was affected.** Reproduced end-to-end: a real
v1.8.3 database upgraded to this build yielded 2 rows.

Fixed by Data migration 8 (`NotificationLegacyMetadataMigrations.BackfillAnnouncementMetadata`), which
gives that one shipped notification the metadata #312 expects, plus the `Title` it predates. Matching
its body text is sound *in a migration specifically* — the text shipped in v1.8.3 and can never change
retroactively, and migration SQL is frozen once applied, so the match is against a fixed historical fact
rather than a live value. That is precisely why the runtime path must never do it, and the migration
says so at the point of the code. `Metadata IS NULL` keeps it narrow: an already-identified row is never
rewritten. Data-only, so the baseline needs no counterpart — a fresh database has no legacy row.

Verified against a real v1.8.3 database carrying its notification: `applying 5 pending "Data"
migration(s) (version 3 → 8)` → `Quotinator ready` → **1 notification, not 2**, retaining v1.8.3's
original `ExpiresAt` (proving the original row was enriched in place, not replaced) while gaining the
title and kind. Unit-guarded by `NotificationSeedingTests.SeedOnceAsync_LegacyRowBackfilledByMigration8_DoesNotWriteADuplicate`
and `Migration8_RowThatAlreadyHasMetadata_IsLeftUntouched`; smoke-test section 39f, which states the
"wait long enough for v1.8.3 to write it" trap explicitly, since falling into it is what let this reach
T1.

**Known leftover, deliberately not cleaned up:** a machine that ran an intermediate #312 build before
migration 8 existed already has the duplicate. Migration 8 prevents new ones but does not delete it —
it is a real row the operator may have read, and removing user-visible history to tidy a transition is
not a migration's business. Dismiss it.

**T1 then passed on the fixed build** (developer, 2026-08-16), and again on the final build after steps
10–14 — each of which changes what is stored, so neither pass could stand in for the other.

**T2 re-run after steps 10–12** — full chain from a genuine v1.8.3 database: `applying 7 pending "Data"
migration(s) (version 3 → 10)` → `Quotinator ready`, the shipped announcement still one row retaining
its original expiry, and stored payloads carrying an explicit release state with no `"version":null`.

**Run with the version temporarily bumped to 1.8.4, and that is not incidental.** This build's own
version is 1.8.3, so migration 9's backfilled row and the row the app records for itself would have been
the same row — the migration would appear to work whether or not it did anything. Bumping
`Directory.Build.props` for the image build (restored immediately afterwards) separates them: the
history reads `1.8.3 / 1` then `1.8.4 / 2`, the legacy announcement attributes to 1.8.3, and the
what's-new written during that same startup attributes to 1.8.4 — provenance recording who wrote a row
rather than who is running. Smoke-test section 39g states the requirement so the next reader does not
repeat the ambiguous version of this check.

### 10. `Unreleased`/`Released` becomes an explicit state
**Status:** ✅ Done

`WhatsNewMetadataDto` currently distinguishes the two by `Version` being null. **Inferring a state from
a missing value is the fault**: null is indistinguishable from "not set", "failed to parse", and "the
producer forgot", so every future reader has to know the convention to interpret the row, and any of
those three accidents silently reads as "unreleased".

- `WhatsNewReleaseState` (`Quotinator.Data/Enums/`, per ADR 016), two members: `Released`,
  `Unreleased`. Named after the payload it belongs to rather than a bare `ReleaseState`, matching the
  `Notification*` prefixing every sibling enum in that folder already uses.
- `WhatsNewMetadataDto` gains it as a `required` property, serialized as a string per CLAUDE.md's JSON
  policy. `IdentityComponents` becomes `[ReleaseState, Version, ContentHash]`, so a released and an
  unreleased entry can never collide even if their other components coincided.
- `Version` still stays null for the unreleased case — it genuinely has no version — but nothing has to
  *infer* anything from that any more.
- Serialization omits nulls (`JsonIgnoreCondition.WhenWritingNull`), so a stored payload stops carrying
  `"version":null`. Identity is unaffected: an absent property deserializes back to null.

Scoped to what's-new here, and widened to every payload by step 13.

A `required` property that a stored row predates cannot be deserialized, so an existing what's-new row
would go unidentified and re-announce itself. That only affects databases carrying rows written by an
intermediate #312 build — #81 has never shipped — but "only developer machines" is exactly the
assumption step 9's second T1 defect punished, so it is fixed rather than assumed harmless. Migration 10
sets `releaseState` on those rows from the very convention that wrote them (a `version` key present
means `Released`), which is a fixed historical fact about already-written rows in the same way
migration 8's body-text match is.

### 11. Provenance for the legacy notification, and the `1.8.3` row it points at
**Status:** ✅ Done

Migration 8 backfilled `Metadata`/`MetadataKind`/`Title` on the legacy notification but left
`AppVersionId` null, so the two stored rows disagree: the what's-new entry has provenance, the
v1.8.3-era announcement does not.

**Its writer is knowable, not a guess.** v1.8.3 was the last official release, and it is the only
version that could have written that row.

- A new migration — **not** an edit to 8, which has already been applied to a real database and is
  frozen by CLAUDE.md's never-edit-an-applied-migration rule — inserts a `System_AppVersion` row for
  `Quotinator.Api` / `1.8.3`, then points the legacy notification's `AppVersionId` at it.
- **The insert is conditional on that notification actually being present**, which is what keeps it
  from fabricating history. A database created fresh by an intermediate #312 build takes the baseline
  path, records data v7 or v8, and then upgrades incrementally through this migration — it never ran
  v1.8.3, and must not gain a row claiming it did. A notification carrying #279's payload with a *null*
  `AppVersionId` is precisely a row written before the provenance column existed, i.e. by v1.8.3.
- **A fresh database must not get the row either.** That falls out of the existing design rather than
  needing a guard: a genuinely empty database takes the one-step baseline path and never replays
  migrations at all, so a migration is by construction upgrade-only. Stated in the migration's own
  comment, since it is load-bearing and invisible from the SQL.
- **Sequence number below the existing minimum, not `MAX + 1`.** v1.8.3 predates every row the table can
  already hold: `System_AppVersion` did not exist in v1.8.3 (#81 introduced it, unreleased), so any
  pre-existing row was written by a later build. Appending at the end would make `GetLastActiveAsync`
  answer "1.8.3" on a machine that has since run newer builds, and #81's catch-up range would replay
  releases it already announced.
- Useful side effect on the genuine v1.8.3 upgrade path, where the table *is* empty: it gains a correct
  `lastActiveVersion`, so #81's catch-up works on the first post-upgrade start rather than falling back
  to current-version-only.

### 12. Provenance becomes as hard to forget as identity
**Status:** ✅ Done

`NotificationMetadataDto.IdentityComponents` is abstract, so no payload can exist without an identity —
the compiler enforces it. `AppVersionId` is an optional `Guid?` that defaults to null, so provenance is
trivially omitted, and *was* omitted by migration 8. The asymmetry is the defect.

Provenance stays a first-class column, never a metadata field, per the developer's boundary. The fix is
to `INotificationWriter.WriteAsync`'s and `NotificationSeeding.SeedOnceAsync`'s signatures: the
parameter keeps its `Guid?` type — null is a legitimate answer when `RecordCurrentAsync` failed — but
loses its default, so a caller has to state it. Guarded mechanically as well as by the compiler, since
a future edit could re-add the default: a reflection test asserts the parameter has none.

### 13. Release state, version and content hash become common to every payload
**Status:** ✅ Done

Found by the developer reading the stored rows after step 12's T1 pass: the announcement's payload said
only `{"announcement":"GetAllImportBatches"}` while the what's-new entry beside it stated a release
state and a content hash. Two kinds disagreeing about what a payload states is the defect — an
announcement belongs to a release just as much as a what's-new entry does.

- All three properties move onto `NotificationMetadataDto`. `WhatsNewMetadataDto` is left with no
  fields of its own, which is the correct outcome rather than an accident: they were always the common
  case, discovered through the one producer that needed them first.
- `WhatsNewReleaseState` becomes `NotificationReleaseState` and gains `NotApplicable`, because a
  schema-version overshoot is not about a release at all. Stating that is not the null-inference this
  issue removed — it is a claim a producer makes. Borrowing the running version instead would also make
  the same unresolved overshoot re-announce itself on every upgrade, since the version now participates
  in identity.
- `NotApplicable` is the zero value, so a payload that somehow reached storage without stating its
  state cannot silently claim to describe a tagged release.
- Identity is `[ReleaseState, Version, ContentHash]` plus whatever the payload adds, compared on the
  base rather than assembled by each derived type — the same reasoning that moved `Kind` and the
  null-omission rule off the derived types.
- `NotificationContentHash` replaces the hashing #81 did inline. A second copy of "SHA-256, take eight
  hex characters" that drifted would not fail; it would silently re-announce every affected
  notification.
- Migration 11 backfills the remaining stored shapes. The announcement's values are historical fact,
  not a guess: v1.8.3 shipped the renames, and its body text shipped with that release, which is what
  makes hashing it here sound. The hash is a literal because SQLite has no hashing function — and
  because migration SQL is frozen regardless, which is the same reason migration 8's body-text match is
  sound.

**Editing the announcement's wording will re-announce it to everyone**, since the producer's hash then
stops matching the frozen one. That is what a content hash is for, but it is worth knowing before
touching that string.

### 14. Expiry has no configured default at all
**Status:** ✅ Done

Step 4 made expiry opt-in but left `NotificationWriter` taking a `defaultExpiryHours` it no longer used,
with an XML doc still claiming it was applied when no expiry was given. Expiry is always optional
(developer decision, 2026-08-16), so there is nothing for that value to mean: the constructor
parameter, the `Quotinator:NotificationDefaultExpiryHours` config key and
`QueryParamDefaults.NotificationDefaultExpiryHours` are all removed, and the DI registration goes back
to the plain two-type overload. Never an HA add-on option, so no `addon/`/`addon-beta/` mirroring
applies.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `System_Notification` gains `Title`, `Body` (renamed from `Message`), `Metadata`, `MetadataKind`, `AppVersionId` | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` compares every column's name, type, nullability and ordinal against the migrated database — the shape assertion this row asks for, rather than a second test duplicating it |
| 2 | ✅ | Baseline and incremental replay produce an identical `System_Notification` schema | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` |
| 3 | ✅ | `MetadataKind`'s `CHECK` accepts every enum member and rejects an unknown value, on both the baseline and incremental paths | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` |
| 4 | ✅ | A notification written with no explicit expiry has no expiry | Unit test | `NotificationWriterTests.WriteAsync_NoExpirySpecified_DoesNotExpire` — re-reads the stored row rather than trusting the returned entity, since "defaulted on write" and "defaulted on read" are different bugs |
| 5 | ✅ | A notification written with an explicit expiry keeps it | Unit test | `NotificationWriterTests.WriteAsync_ExplicitExpirySpecified_UsesExplicitValueNotDefault` |
| 6 | ✅ | `System_AppVersion` stores `Application` and `Version` as separate columns | Unit test | `AppVersionTrackerTests.RecordCurrentAsync_FirstCall_StoresApplicationAndVersionSeparately` — asserts the two stored columns directly, not just the reassembled record; schema parity covered by `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAppVersionSchema` |
| 7 | ✅ | Recording the same application+version twice appends no second row; a different version appends a new one | Unit test | `AppVersionTrackerTests.RecordCurrentAsync_SamePairTwice_AppendsOnlyOnce`, `..._NewVersion_AppendsWithoutOverwritingHistory`, `..._SameVersionDifferentApplication_AppendsSeparately` |
| 8 | ✅ | "Last active version" resolves to the most recent row, not an overwritten single row | Unit test | `AppVersionTrackerTests.GetLastActiveAsync_SeveralVersionsWithinOneTimestamp_ReturnsTheOneWrittenLast`, `RecordCurrentAsync_EachCall_TakesTheNextSequenceNumber`, `GetLastActiveAsync_RowWithNewerTimestampButOlderSequence_DoesNotWin` |
| 9 | ✅ | A notification's `AppVersionId` still points at the version that wrote it after the app version changes | Unit test | `NotificationWriterTests.WriteAsync_AppVersionId_StillPointsAtTheWritingVersionAfterAnUpgrade` — real SQLite, joins the notification back to `System_AppVersion` after a newer version is recorded. **Was missing entirely** until the developer asked for the verification set to be audited; the guarantee had only ever been checked live in Docker, despite being the reason `System_AppVersion` became append-only |
| 10 | ✅ | A notification is identified by its structured metadata, not by message text | Unit test | `NotificationSeedingTests.SeedOnceAsync_SameIdentityTwice_WritesOnce`, `..._DifferentIdentity_WritesAgain`, `..._SameValuesDifferentKind_BothWrite` — real SQLite, not fakes, since the behaviour is a payload surviving a round-trip through the column |
| 11 | ✅ | An identifier appearing in body text but not in metadata does **not** suppress a write | Unit test | `NotificationSeedingTests.SeedOnceAsync_IdentityAppearsInBodyButNotMetadata_StillWrites`; `..._VersionIsSubstringOfAnother_BothWrite` covers the specific `1.9.1`/`1.9.10` case |
| 12 | ✅ | #279's and #289's producers still write exactly once across restarts after migration | Unit test | `ProgramNotificationSeedingRegressionTests` (unchanged, still green through the full startup path); `WhatsNewNotificationTests` updated for #81 |
| 13 | ✅ | Every `NotificationMetadataKind` has a registered payload type, and each type reports the kind it is registered under | Unit test | `NotificationMetadataKindsTests` — without this a new kind is a silent re-announce-forever bug |
| 14 | ✅ | `INotificationActionExecutor.ExecuteAsync` receives the originating notification's metadata | Unit test | `NotificationActionExecutorTests.ExecuteAsync_WithMetadata_DeliversItAndStillPerformsTheAction`, `..._WithoutMetadata_StillPerformsTheAction` |
| 15 | ✅ | `GET /api/v1/notifications` returns `title`/`body`/`metadata`/`metadataKind`, and no longer returns `message` | Unit test | `NotificationEndpointsTests.GetNotifications_ResponseCarriesTitleBodyAndMetadata_AndNoLongerMessage` — asserts the serialized JSON, not a deserialized DTO, since the requirement is about the wire format a client sees. Written only when the developer noticed this row was still ❌: live Docker evidence existed, but the stated method was a unit test and none had been written |
| 16 | ✅ | An unreleased what's-new entry is identified by an explicit state, not by a null version | Unit test | `NotificationSeedingTests.SeedOnceAsync_UnreleasedEntry_StoresAnExplicitStateAndNoNullVersion` — asserts the stored payload text, since the requirement is about what a later reader finds in the column. Confirmed live in T2: `{"releaseState":"Unreleased","contentHash":"2EE673F9"}` |
| 17 | ✅ | A released and an unreleased entry never share an identity | Unit test | `NotificationSeedingTests.SeedOnceAsync_ReleasedAndUnreleasedWithIdenticalComponents_BothWrite` — identical content hash on both, so only the state can separate them |
| 18 | ✅ | A stored payload contains no null-valued properties | Unit test | `NotificationMetadataKindsTests.SerializedPayload_NeverContainsANullValuedProperty`, across every registered kind and through `NotificationMetadataKinds.Serialize` — the same entry point a real write uses, so it cannot pass against a serializer configuration the app does not actually apply |
| 19 | ✅ | A notification cannot be written without stating which version wrote it | Unit test | `NotificationWriterTests.WriteAsync_AppVersionIdParameter_HasNoDefault` — reflection over `INotificationWriter.WriteAsync` and `NotificationSeeding.SeedOnceAsync`, because the guarantee is a compile-time one and a later edit could restore the default with no test noticing |
| 20 | ✅ | Migration applies cleanly to a real copy of the last released (v1.8.3) database | Live (T2) | `docker run ghcr.io/dutchjafo/quotinator:1.8.3` → `schema created at baseline (data v3, app v5)`; current build → `applying 8 pending "Data" migration(s) (version 3 → 11)`, `schema updated (data v11, app v5)`, `Quotinator ready`, no exception, and no repeat on `docker restart` |
| 21 | ✅ | Stored payload carries no duplicate `Kind`, and `AppVersionId` joins to the writing version | Live (T2) | Against the migrated db: `Metadata` = `{"announcement":"GetAllImportBatches"}`, join yields `Quotinator.Api 1.8.3`. Regression-guarded by `NotificationMetadataKindsTests.SerializedPayload_NeverContainsTheKindDiscriminator` |
| 22 | ✅ | `System_AppVersion` stays append-only across a restart | Live (T2) | One row (`Quotinator.Api / 1.8.3 / 1`) before and after `docker restart` |
| 23 | ✅ | Startup survives an upgrade from an *intermediate* (unreleased) schema version, not only from the last release | Live (T2) | Database promoted to data v4: pre-fix image reproduces `Unhandled exception … no such column: Application`; fixed image reaches `Quotinator ready`, legacy row preserved and current version appended. Re-confirmed at data v9 → v10 while verifying row 27. Smoke-test section 39e; unit-guarded by `AppVersionTrackerTests.GetLastActiveAsync_DatabaseAtPre312Shape_ReadsExistingRowOnceMigrated` |
| 24 | ✅ | Upgrading a v1.8.3 database does not duplicate its existing notification | Live (T2) | Real v1.8.3 database carrying the #279 announcement, upgraded through `version 3 → 11`: the announcement is still one row, retaining v1.8.3's original `ExpiresAt` (`2026-09-15`), and still one row after a restart. Smoke-test 39f; unit-guarded by `NotificationSeedingTests.SeedOnceAsync_LegacyRowBackfilledByTheMigrations_DoesNotWriteADuplicate` and `Migration8_RowThatAlreadyHasMetadata_IsLeftUntouched` |
| 25 | ✅ | Upgrading a v1.8.3 database creates a `1.8.3` `System_AppVersion` row, sorted before existing history, and links the legacy notification to it | Unit test + Live (T2) | `NotificationLegacyBackfillMigrationTests.Migration9_LegacyAnnouncementPresent_CreatesTheV183RowAndLinksTheNotificationToIt` and `..._DatabaseWithLaterHistory_PlacesV183BeforeIt`. Live: run with the version temporarily bumped to 1.8.4, so the migration's row and the running build's own row are distinguishable — `1.8.3 / 1` then `1.8.4 / 2`, with the legacy announcement attributed to 1.8.3 and the startup's own what's-new to 1.8.4. Smoke-test 39g, which states that version-bump requirement, since with both at 1.8.3 the two causes are indistinguishable |
| 26 | ✅ | A database that never ran v1.8.3 gains no `1.8.3` row, and a fresh database has exactly one `System_AppVersion` row — its own version, no history | Unit test + Live (T2) | `NotificationLegacyBackfillMigrationTests.Migration9_NoLegacyAnnouncement_InsertsNothing` and `..._AnnouncementAlreadyCarryingProvenance_IsLeftUntouched`; `DatabaseInitializerOwnershipTests.DataOwnedBaseline_FreshDatabase_RecordsNoAppVersionHistory`. Live: fresh volume → `schema created at baseline (data v10, app v5)`, one row (`Quotinator.Api / 1.8.4 / 1`) |
| 27 | ✅ | A what's-new row written before the release state existed is backfilled rather than re-announced | Unit test + Live (T2) | `NotificationLegacyBackfillMigrationTests.Migration10_*` (three tests: backfill, already-stated row untouched, other kinds untouched). Live: injected `{"version":"1.8.4"}` row and rolled the counter back → `applying 1 pending "Data" migration(s) (version 9 → 10)` → `{"version":"1.8.4","releaseState":"Released"}`, the already-stated unreleased row unchanged. Smoke-test 39h |
| 28 | ✅ | Every payload states a release state, and one about no release says so rather than borrowing a version | Unit test + Live (T2) | `NotificationLegacyBackfillMigrationTests.Migration11_LegacySchemaOvershoot_StatesThatNoReleaseApplies`; the required property makes omission a compile error, which `NotificationMetadataKindsTests` exercises across every registered kind. Live: the announcement reads `{"announcement":"GetAllImportBatches","releaseState":"Released","version":"1.8.3","contentHash":"E55328BB"}` |
| 29 | ✅ | The backfilled legacy announcement is recognised by the producer that would otherwise re-announce it | Unit test + Live (T2) | `NotificationLegacyBackfillMigrationTests.Migration11_BackfilledAnnouncement_IsRecognisedByTheProducer` — compares against a payload built exactly as `Program.cs` builds it, so a hash computed differently on either side fails rather than passing on a matching pair of wrong values. `..._GainsTheCommonReleaseFields` pins the hash itself. Live: 2 notifications before and after `docker restart`, never 3 |
| 30 | ✅ | No configured default expiry exists anywhere | Build | `grep -rn "NotificationDefaultExpiryHours\|defaultExpiryHours" src/ tests/` returns nothing; `NotificationWriter` is registered through the plain two-type overload again |
| 31 | ✅ | T1 — app starts in Visual Studio with no error; `/notifications` renders migrated rows correctly | Live (T1) | Developer confirmed 2026-08-16 on the final build: `schema is up to date (data v11, app v5)` → `Quotinator ready`, no exception, 799 quotes intact. Re-run was required because this build adds migrations 9, 10 and 11 on top of the chain the earlier pass covered |
| 32 | ✅ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 33 | ✅ | Full test suite green | Build | `dotnet test --configuration Release -m:1` — 3,441 passed, 0 failed |

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
