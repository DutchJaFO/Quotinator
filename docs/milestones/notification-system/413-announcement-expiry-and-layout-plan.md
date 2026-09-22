# #413 — The operation-ID announcement expires while it still applies, and its text is one unbroken paragraph

**Status:** Planning
**GitHub issue:** #413
**Tiers required:** T1, T2
**Depends on:** #312, #319 — both `Waiting for release`

---

## Next action

Step 1: extract the announcement producer into a seam its tests can name.

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
producer is looking for, it matches nothing in history, and it writes a second copy — the third
requirement, broken by satisfying the second. Migration 11 states the intent behind that:
*"If the producer's wording is ever edited, the hashes stop matching and the notification is
re-announced, which is exactly what a content hash is for."*

**No default expiry exists any more.** `NotificationDefaultExpiryHours` appears nowhere in `src/`, and
`SeedOnceAsync`'s `expiresAt` defaults to `null`. A fresh install already produces an announcement
with no expiry, so only the row a 1.8.3 database already holds needs repair.

**`BodyIsMultiLine` drives nothing.** `LayoutFor` is referenced only by `NotificationTableTests`; no
renderer reads it. Bodies keep their line breaks because `.notification-body` is `white-space:
pre-line` for every kind — measured live during #411's T2 pass (`breaksHonoured: true`, `4` line boxes
against `2`). The issue names `BodyIsMultiLine: false` as part of the cause; it is not. The cause is
that the text has no line breaks in it.

So this issue needs no rendering change whatsoever: give the body line breaks and the existing markup
renders them. The flag, and the unread map around it, go back to #308 — see its own reopening.

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
  rule migration 11 states — edited wording is new content, and new content re-announces. A migration
  that rewrites a stored notification's text again needs its own decision, made on its own merits.
- **`BodyIsMultiLine` belongs to #308, not here** (developer direction, 2026-09-22). It is one of the
  features #308 defines, and #308 is still open: it returns to `In progress` and finishes by proving
  every feature it defines is useful and testable across both surfaces and every variant. The flag, and
  the `LayoutFor` map it sits in, are resolved there — see that plan's steps 14–16. This issue changes
  no rendering code at all.
- **The announcement's text moves to one named place.** It is currently a `const` inside a block in
  `Program.cs`, which no test can name, and it now has to agree with a frozen hash in a migration.
  A small `OperationIdAnnouncement` class in `Quotinator.Api.Startup` — the shape #81's
  `WhatsNewNotification` already uses — gives the test something to hash and keeps the body written
  once.

---

## Steps

### 1. Extract the announcement producer into a seam its tests can name

**Status:** ⬜ Not started

`Quotinator.Api.Startup.OperationIdAnnouncement`, holding the English body, the title, and the
`SeedAsync` call `Program.cs` makes inline today. No behaviour change: the same payload, the same
`SeedOnceAsync` call, the same non-fatal guard around it. `Program.cs` calls the new class.

### 2. Write the multi-line text in all three languages

**Status:** ⬜ Not started

The statement, one line per renamed operation ID, then the scope note — in the new class for English,
and in `i18ntext/UI.{en-GB,nl,de}.json` under the existing `NotificationOperationIdRename*` keys, whose
current values are the single-line text. English lives in both places by the design #319 settled: the
notification's own text is English and the hash is taken over it, while the localizer supplies every
language for the translation rows.

### 3. Add the migration that repairs an already-stored row

**Status:** ⬜ Not started

A new `DataOwnedMigrations` entry (version 23 — `System_Notification` is Data-owned, as migrations 8,
11 and 14 were), in `NotificationLegacyMetadataMigrations`, scoped by
`json_extract(Metadata, '$.announcement') = 'GetAllImportBatches'` exactly as migration 14 scopes its
own backfill. It does four things to that row: `ExpiresAt = NULL`, `Body` to the multi-line English
text, `json_set($.contentHash)` to the new hash, and the `nl`/`de` rows in
`System_NotificationTranslation` to their multi-line text.

`IsDismissed` is untouched — a dismissed row stays dismissed, per the issue.

Data-only, so the baseline needs no counterpart: a fresh database has no legacy row to repair, and its
producer writes the new text directly. Idempotent by construction — every statement assigns a fixed
value to a row selected by a fixed predicate, so replaying it changes nothing.

The hash is a frozen literal, as migration 11's is, because SQLite cannot compute one and migration
text must not follow a later edit. Step 4's guard test is what keeps the literal honest.

### 4. Write the tests, red first

**Status:** ⬜ Not started

Every test below runs against current code before any of steps 1–4 land, and each must fail for the
reason it exists. The migration tests build their fixture the way
`NotificationLegacyBackfillMigrationTests` already does: a row in the 1.8.3 shape, with an expiry, the
single-line body, the old hash, and `nl`/`de` translation rows.

### 5. Extend the live document

**Status:** ⬜ Not started

*Upgrading a v1.8.3 database enriches its notification rather than duplicating it*
(`notifications-and-changelog/04`) already upgrades a real 1.8.3 database and asserts the count stays
`1` and the original `expiresAt` is retained. That retention assertion is what this issue reverses, so
the document's step 2 changes with the behaviour: `expiresAt` is now empty, `isTranslated` still works,
the body carries line breaks in each language, and the count is still `1`.

Run it red against a build from the commit before this issue's first change — `git worktree add`,
`docker build -t quotinator:canary413` — per `docs/testing-policy.md`'s red-first rule for automated
documents, then remove the container, image and worktree.

### 6. Close out

**Status:** ⬜ Not started

Boyscout pass over the touched files (`.editorconfig` scoped sections for the `.cs` files this issue
edits), the changelog `unreleased` entry in all three languages, and the `Waiting for release`
checklist.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | An upgraded 1.8.3 row has no expiry | Unit test | `NotificationLegacyBackfillMigrationTests.Migration023_LegacyAnnouncementRow_ClearsItsExpiry` — `ExpiresAt` is `NULL` after the migration, against a fixture whose row carries one |
| 2 | ❌ | A dismissed row stays dismissed | Unit test | `NotificationLegacyBackfillMigrationTests.Migration023_DismissedLegacyRow_StaysDismissed` — `IsDismissed` is `1` before and after |
| 3 | ❌ | The upgraded row's body carries its line breaks, in every language | Unit test | `NotificationLegacyBackfillMigrationTests.Migration023_LegacyAnnouncementRow_RewritesBodyAndTranslations` — the row's `Body` and both translation rows each contain `\n` and the renamed operation IDs on their own lines |
| 4 | ❌ | The upgrade writes no second copy | Unit test | `NotificationSeedingTests.SeedOnce_AgainstAMigratedAnnouncementRow_WritesNothing` — history holding the migrated row, producer payload from `OperationIdAnnouncement`, result `null` |
| 5 | ❌ | The migration's frozen hash matches the text the producer ships | Unit test | `OperationIdAnnouncementTests.TheMigrationHash_MatchesTheShippedBody` — `NotificationContentHash.Of(OperationIdAnnouncement.Body)` equals the literal in migration 23 |
| 6 | ❌ | Every language's announcement body is multi-line | Unit test | `TranslationCompletenessTests.OperationIdRenameBody_CarriesLineBreaksInEveryLanguage` — each of `UI.en-GB`, `UI.nl`, `UI.de` holds `\n` in that key |
| 7 | ❌ | A real 1.8.3 upgrade shows one active, multi-line announcement | Live (T2) | *Upgrading a v1.8.3 database enriches its notification rather than duplicating it*, step 2: count `1`, `expiresAt` empty, body contains a line break, `metadataKind=announcement` |
| 8 | ❌ | That document would have caught the defect | Live (T2) | The same document against `quotinator:canary413`, built from the commit before this issue's first change: step 2 fails on `expiresAt` and on the line break |
| 9 | ❌ | The application starts | Live (T1) | The developer starts it in Visual Studio and it reaches `Quotinator ready` |

---

## Observed effect

Not yet established — this section records what the fix produces once step 5 has run.
