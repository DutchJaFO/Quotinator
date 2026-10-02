# #413: The operation-ID announcement expires while it still applies, and its text is one unbroken paragraph

**Status:** In progress
**GitHub issue:** #413
**Tiers required:** T1, T2
**Depends on:** #312 and #319, both `Waiting for release`

---

## Next action

**T1 by the developer (row 9).** Steps 1 to 5 are done, rows 1 to 8 are ✅, and step 6's boyscout pass
and changelog entries are in. The `Waiting for release` list is all that follows T1.

---

## Description

A database written by 1.8.3 holds the #279 announcement with an expiry 1.8.3 gave every notification by
default. #312 removed that default but adopted the existing row rather than writing a fresh one, so the
expiry survived the upgrade: an operator upgrading more than 30 days after installing 1.8.3 sees a
breaking change that still applies, marked **Expired**. The same row's body is one unbroken paragraph.

Three requirements, from the issue: the announcement has no expiry (including on a database that
already holds it, with a dismissed row staying dismissed); its body is laid out over several lines in
English, Dutch and German; and neither change writes a second copy.

---

## What the cross-check found

**The dedupe identity includes the content hash.** `NotificationMetadataDto.FullIdentity` is
`[ReleaseState, Version, ContentHash, …IdentityComponents]`, so rewriting the body changes what the
producer is looking for, it matches nothing in history, and it writes a second copy, the third
requirement, broken by satisfying the second. Migration 11 states the intent behind that:
*"If the producer's wording is ever edited, the hashes stop matching and the notification is
re-announced, which is exactly what a content hash is for."*

**No default expiry exists any more.** `NotificationDefaultExpiryHours` appears nowhere in `src/`, and
`SeedOnceAsync`'s `expiresAt` defaults to `null`. A fresh install already produces an announcement
with no expiry, so only the row a 1.8.3 database already holds needs repair.

**`BodyIsMultiLine` drives nothing.** `LayoutFor` is referenced only by `NotificationTableTests`; no
renderer reads it. Bodies keep their line breaks because `.notification-body` is `white-space:
pre-line` for every kind, measured live during #411's T2 pass (`breaksHonoured: true`, `4` line boxes
against `2`). The issue names `BodyIsMultiLine: false` as part of the cause; it is not. The cause is
that the text has no line breaks in it.

So this issue needs no rendering change whatsoever: give the body line breaks and the existing markup
renders them. The flag, and the unread map around it, go back to #308, see its own reopening.

**Re-planned 2026-10-02, against what #348 shipped since this plan was written.** Four things moved:

- **Step 1 is already done.** #348 extracted the body into
  `Quotinator.Api.Startup.OperationIdRenameAnnouncement`, which `Program.cs` reads and
  `OperationIdRenameAnnouncementTests` holds against a migration hash. That is the seam step 1 asked for.
- **The migration is version 27, not 23.** Versions 23 to 26 were taken by #348 and #349
  (`NotificationBackupRefusedMigrations`, `NotificationAnnouncementRewordMigrations`,
  `NotificationBackupQuotaMigrations`, `NotificationBackupMaxMigrations`).
- **Migration 24 already does most of step 3.** `NotificationAnnouncementRewordMigrations.RewordOperationIdRename`
  rewrites that row's `Body`, its `$.contentHash` (to the literal `6FC95BB0`) and its `nl`/`de`
  translation rows, under the identical `json_extract(Metadata, $.announcement) = 'GetAllImportBatches'`
  predicate this plan specifies. So migration 27 starts from migration 24's single-line text, not from
  1.8.3's, and its only new column is `ExpiresAt`.
- **A new migration, not an edit to 24.** The schema migration policy forbids editing an applied
  migration, and migration 24 has been applied to the developer's own database (T1, 2026-10-02:
  *applying 23 pending Data migration(s) (version 3 to 26)*). Squashing 24 and 27 belongs to the
  milestone-close consolidation, not here.

**The issue body's own quote of the current text is stale.** Its *Actual behaviour* renders the body with
`keyed by operation ID [em dash] routes and behaviour are unchanged`; #348 reworded that to a semicolon. The defect
it describes is unaffected: the body is still one unbroken paragraph either way.

---

## Decisions

- **The migration rewrites the stored row in place, hash included** (developer decision, 2026-09-22).
  One migration sets `ExpiresAt` to `NULL`, replaces the English body and its `nl`/`de` translation
  rows with the multi-line text, and updates `$.contentHash` to the new value. The producer then finds
  the row it expects and writes nothing. This is a deliberate exception to migration 11's reasoning
  above, and the exception is narrow: the announcement is being *restated*, not replaced by new
  content, so re-announcing it would tell the operator nothing they have not already read.

  **The in-place edit is strictly this migration's, for this one notification** (developer direction,
  2026-09-22). It is not a pattern a later producer may reach for: every other notification keeps the
  rule migration 11 states, edited wording is new content, and new content re-announces. A migration
  that rewrites a stored notification's text again needs its own decision, made on its own merits.
- **`BodyIsMultiLine` belongs to #308, not here** (developer direction, 2026-09-22). It is one of the
  features #308 defines, and #308 is still open: it returns to `In progress` and finishes by proving
  every feature it defines is useful and testable across both surfaces and every variant. The flag, and
  the `LayoutFor` map it sits in, are resolved there, see that plan's steps 14 to 16. This issue changes
  no rendering code at all.
- **The announcement's text moves to one named place.** It is currently a `const` inside a block in
  `Program.cs`, which no test can name, and it now has to agree with a frozen hash in a migration.
  A small `OperationIdAnnouncement` class in `Quotinator.Api.Startup`, the shape #81's
  `WhatsNewNotification` already uses, gives the test something to hash and keeps the body written
  once.

---

## Steps

### 1. Extract the announcement producer into a seam its tests can name

**Status:** ✅ Done, by #348

Delivered as `Quotinator.Api.Startup.OperationIdRenameAnnouncement` (not `OperationIdAnnouncement`, the
name this plan proposed), holding the English body as a `const`. `Program.cs:1117` reads it, and
`OperationIdRenameAnnouncementTests` already holds it against migration 24's frozen hash, which is
exactly the seam this step wanted. Nothing to do here.

### 2. Write the multi-line text in all three languages

**Status:** ✅ Done

The statement, one line per renamed operation ID, then the scope note, in the new class for English,
and in `i18ntext/UI.{en-GB,nl,de}.json` under the existing `NotificationOperationIdRename*` keys, whose
current values are the single-line text. English lives in both places by the design #319 settled: the
notification's own text is English and the hash is taken over it, while the localizer supplies every
language for the translation rows.

### 3. Add the migration that clears the expiry and restates the body

**Status:** ✅ Done, as version 27 in `NotificationAnnouncementLineBreakMigrations`

A new `DataOwnedMigrations` entry (version 23, `System_Notification` is Data-owned, as migrations 8,
11 and 14 were), in `NotificationLegacyMetadataMigrations`, scoped by
`json_extract(Metadata, '$.announcement') = 'GetAllImportBatches'` exactly as migration 14 scopes its
own backfill. It does four things to that row: `ExpiresAt = NULL`, `Body` to the multi-line English
text, `json_set($.contentHash)` to the new hash, and the `nl`/`de` rows in
`System_NotificationTranslation` to their multi-line text.

`IsDismissed` is untouched, a dismissed row stays dismissed, per the issue.

Data-only, so the baseline needs no counterpart: a fresh database has no legacy row to repair, and its
producer writes the new text directly. Idempotent by construction, every statement assigns a fixed
value to a row selected by a fixed predicate, so replaying it changes nothing.

The hash is a frozen literal, as migration 11's is, because SQLite cannot compute one and migration
text must not follow a later edit. Step 4's guard test is what keeps the literal honest.

### 4. Write the tests, red first

**Status:** ✅ Done

Every test below runs against current code before any of steps 1 to 4 land, and each must fail for the
reason it exists. The migration tests build their fixture the way
`NotificationLegacyBackfillMigrationTests` already does: a row in the 1.8.3 shape, with an expiry, the
single-line body, the old hash, and `nl`/`de` translation rows.

### 5. Extend the live document

**Status:** ✅ Done, red then green 2026-10-02

*Upgrading a v1.8.3 database enriches its notification rather than duplicating it*
(`notifications-and-changelog/04`) already upgrades a real 1.8.3 database and asserts the count stays
`1` and the original `expiresAt` is retained. That retention assertion is what this issue reverses, so
the document's step 2 changes with the behaviour: `expiresAt` is now empty, `isTranslated` still works,
the body carries line breaks in each language, and the count is still `1`.

Run it red against a build from the commit before this issue's first change, `git worktree add`,
`docker build -t quotinator:canary413`, per `docs/testing-policy.md`'s red-first rule for automated
documents, then remove the container, image and worktree.

### 6. Close out

**Status:** 🔄 Boyscout pass and changelog done; the `Waiting for release` list waits on T1

Boyscout pass over the touched files (`.editorconfig` scoped sections for the `.cs` files this issue
edits), the changelog `unreleased` entry in all three languages, and the `Waiting for release`
checklist.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | An upgraded 1.8.3 row has no expiry | Unit test | `NotificationLegacyBackfillMigrationTests.Migration27_LegacyAnnouncementRow_ClearsItsExpiry`, red with migration 27 absent from the fixture chain |
| 2 | ✅ | A dismissed row stays dismissed | Unit test | `NotificationLegacyBackfillMigrationTests.Migration27_DismissedLegacyRow_StaysDismissed`. Cannot fail by removing migration 27, so shown red by mutating it to also set `IsDismissed = 0` |
| 3 | ✅ | The upgraded row's body carries its line breaks, in every language | Unit test | `NotificationLegacyBackfillMigrationTests.Migration27_LegacyAnnouncementRow_BodyCarriesItsLineBreaks` and `..._TranslationsCarryTheirLineBreaks` (`nl`, `de`), each asserting four lines. Asserted as a line count, not against the words: the text itself is held to the producer's constant by row 5, where that constant is visible |
| 4 | ✅ | The upgrade writes no second copy | Unit test | A pair, so that neither test needs a copy of the text: `Migration27_LegacyAnnouncementRow_HashDescribesTheStoredBody` (the stored hash is the hash of the stored body) with row 5 (that hash is the producer's). Both shown red by mutating migration 27 to leave the old hash |
| 5 | ✅ | The migration's frozen hash matches the text the producer ships | Unit test | `OperationIdRenameAnnouncementTests.Migration27_WritesTheContentHashTheProducerComputes`, plus `Migration27_WritesEveryLineOfTheBodyTheProducerWrites` and `..._OfTheTranslationTheProducerWrites` per language |
| 6 | ✅ | Every language's announcement body is multi-line | Unit test | `OperationIdRenameAnnouncementTests.EveryLanguagesBody_IsLaidOutOverSeveralLines` (`en-GB`, `nl`, `de`), beside `TheProducersBody_GivesEachRenamedOperationIdItsOwnLine`. Held here rather than in `TranslationCompletenessTests`, whose subject is key completeness rather than one key's shape |
| 7 | ✅ | A real 1.8.3 upgrade shows one active, multi-line announcement | Live (T2) | *Upgrading a v1.8.3 database enriches its notification rather than duplicating it*, green 2026-10-02: count `1`, `sameRow=True`, `cleared=True`, `bodyLines after = 4`, and `lines=4` in all three languages |
| 8 | ✅ | That document would have caught the defect | Live (T2) | The same document against `quotinator:canary413`, built from `0924b058`: step 2 red on `cleared=False` and `bodyLines after = 1`, step 3 red on `lines=1` for every language, while count and `sameRow` stayed correct |
| 9 | ❌ | The application starts | Live (T1) | The developer starts it in Visual Studio and it reaches `Quotinator ready` |

---

## Observed effect

Not yet established, this section records what the fix produces once step 5 has run.
