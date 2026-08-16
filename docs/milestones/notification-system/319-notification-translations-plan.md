# #319 — Notification title and body are not translated

**Status:** Planning
**GitHub issue:** #319 (open)
**Depends on:** #278 (done, released v1.8.0 — the mechanism), #312 (done, this branch — the Title/Body split and the metadata contract that deliberately excludes text)

> **Next action: answer question 1 below, then write the Steps.** This plan is not ready to execute.
> The schema, read-side resolution and backfill are settled, but question 1 decides which request
> signal selects the language — a rule CLAUDE.md draws a hard line around — and it determines the read
> path's signature and most of the tests. Question 2 is a decision for #309, not a blocker here.
> Nothing is blocked on code.

---

## Background

Notification text is written in English by each producer and never translated, so a user running the UI
in Dutch or German reads English notification content inside an otherwise localised interface. Found
during #312's own T1 pass: the Notifications page and startup popup render their chrome in Dutch
("Meldingen", "Waarschuwing", "Vervalt", "Actief") while the notification's title and body stay English.

This is a gap, not a deliberate exemption. #278 built the mechanism with the message passed in as a
plain `string`, and every producer since (#279, #289, #81) followed that shape.

**A notification cannot be localised the way a UI string is.** A UI string resolves at render time
against the current culture. A notification's text is written once, at production time, and may be read
months later — potentially by a user whose language has changed since. Storing pre-rendered text in
whatever language happened to be current at write time freezes that choice permanently.

This project already solved that problem for quotes, and the same approach applies (developer
direction, 2026-08-16): store translations at creation, record the original's language, resolve at read.

## Verified against the code before planning

- **The quote precedent is exactly the shape to copy.** `Sql.Quotes.SelectBase`
  (`src/Quotinator.Core/Queries/Sql.cs:65-89`) resolves three separate translated columns by
  `LEFT JOIN`ing each translation table on `{IdClauses.Join(...)} AND {TextClauses.Equals("…Language", "lang")} AND …IsDeleted = 0`,
  wrapping each in `COALESCE(translated, original)`, and computing
  `CASE WHEN qt.QuoteText IS NOT NULL THEN LOWER(@lang) ELSE q.OriginalLanguage END AS EffectiveLanguage`.
  `@lang` is always bound, `null` when no translation is requested.
- **`QuoteTranslationEntity` is the entity shape to mirror** — `RecordBase`, an FK id, a `Language`
  column, and the translated text (`src/Quotinator.Core/Entities/QuoteTranslationEntity.cs`).
- **#312 already anticipated this issue in `NotificationMetadataDto`'s own remarks**: metadata is
  strictly non-text, and "anything textual — title, body, and the language they are written in — is a
  first-class column on the notification itself, not a field smuggled into this payload." No change is
  needed there; this issue implements what that remark describes.
- **The three existing producers are `Program.cs` (#279's announcement, #289's overshoot) and
  `Startup/WhatsNewNotification.cs` (#81)** — the only `SeedOnceAsync` callers.
- **#309's changelog table is per-language**, so #81's producer genuinely can source all three
  languages: `ChangelogEntity` carries a `Language` column
  (`src/Quotinator.Data/Entities/ChangelogEntity.cs:17`). The issue says "from the per-language
  changelog files it already reads" — since #309 that content is read from the database, not the JSON
  files, but the per-language guarantee the claim depends on does hold.

**One claim in the issue body needs correcting.** It calls the new table `System_NotificationTranslation`,
following ADR 015. That is the right name — but the sibling table it will sit next to is not named that
way, which is question 3 below.

## Design

### Schema

| Change | Detail |
|---|---|
| `System_Notification.OriginalLanguage` | ISO 639-1, `NOT NULL DEFAULT 'en'`. Backfilling existing rows to `en` is a statement of fact — every notification written to date is English — not a guess |
| `System_NotificationTranslation` | New table: `RecordBase` per ADR 002, `System_` prefix and singular per ADR 015. Columns `NotificationId`, `Language`, `Title` (nullable — a notification may have a body and no title), `Body` |

`OriginalLanguage` can be added with `ALTER TABLE … ADD COLUMN` (no CHECK widening involved), so this
does not need a table rebuild. Baseline updated to match in the same commit, per the schema-drift parity
tests. Migration number assigned at implementation time — `DataOwnedMigrations` ends at 11 today.

**No CHECK constraint on `Language`.** ADR 008 governs enum-backed columns; a language code is not
enum-backed here, and `Quotinator_QuoteTranslation.Language` has no CHECK either. Consistent with the
precedent rather than stricter than it.

### Read-side resolution

`Sql.Notifications.SelectColumns` (`src/Quotinator.Data/Queries/Sql.cs:581-584`) becomes a projection
over a `LEFT JOIN`, mirroring `Sql.Quotes.SelectBase` exactly:

- `COALESCE(t.Title, n.Title) AS Title`, `COALESCE(t.Body, n.Body) AS Body`
- join condition `{IdClauses.Join("t.NotificationId", "n.Id")} AND {TextClauses.Equals("t.Language", "lang")} AND t.IsDeleted = 0`
- `CASE WHEN t.Body IS NOT NULL THEN LOWER(@lang) ELSE n.OriginalLanguage END AS EffectiveLanguage`

All four queries (`SelectActive`, `SelectPage`, `SelectById`, and the count) share the projection, so
`@lang` must be bound on every one — `null` when no language is requested, exactly as `Sql.Quotes` does.

`TextClauses.Equals` rather than a hand-written `LOWER(…) = LOWER(…)`, per the case-insensitive-by-default
rule: a translation's `Language` is never canonicalised at capture, so the SQL side needs its own wrap.

**Fallback is per-field, not per-row** — `COALESCE` on `Title` and `Body` independently. A translation
row supplying a body but no title falls back to the original title rather than dropping it.

### Write side

`INotificationWriter.WriteAsync` gains a translations parameter, so a producer supplies every language
in one call rather than the row being enriched afterward (issue requirement 3). Shape:

```
IReadOnlyList<NotificationTranslationDto>   // (Language, Title, Body)
```

A list of a small record rather than a dictionary: `Title` is nullable and independent of `Body`, which
a `Dictionary<string, string>` cannot express, and the `Dto` suffix follows ADR 016's #264 revision the
same way `NotificationMetadataDto` does.

`NotificationSeeding.SeedOnceAsync` threads the same parameter through. Its identity comparison is
untouched — identity is structural, from the metadata payload, and has never involved text.

### Already-persisted rows

**Exactly one notification exists in any released database** — #279's v1.8.3 announcement. #289's
overshoot only writes when a schema version actually overshoots, and #81's what's-new producer has not
shipped in a tagged release. So the backfill is one row, not a corpus.

That row is **already being updated by a migration** — `NotificationLegacyMetadataMigrations`
(migrations 8 and 9) gave it #312's structured metadata and its missing provenance. Its translations
belong with that same work rather than being a separate concern (developer decision, 2026-08-16).

Mechanically that means a migration in the same `NotificationLegacyMetadataMigrations` family, folded
together by the end-of-milestone consolidation pass the overview already plans. It cannot be an edit to
migration 8 or 9 themselves if either has already been applied to a real database — including the
developer's own — per the frozen-migration rule and ADR 015's #254 revision. If neither has, folding it
directly in is simpler and equivalent.

The translated text comes from `UI.*.json`, the same source the #279 producer itself will use.

### Producers

| Producer | Source of translations |
|---|---|
| #279 announcement (`Program.cs`) | `i18ntext/UI.*.json` |
| #289 schema-version overshoot (`Program.cs`) | `i18ntext/UI.*.json` |
| #81 what's-new (`Startup/WhatsNewNotification.cs`) | The `Changelog` table's per-language rows — already translated at source, so nothing is translated here |

Reading `UI.*.json` for all three languages at write time is not what `IApiLocalizer` does — it resolves
one language, from `CurrentUICulture`. This issue needs *every* language at once. Whether that is a new
method on `IApiLocalizer` or a separate reader is a step-level decision, but it must not resolve a
culture: a producer running at startup has no request culture to speak of, which is the whole reason
this issue exists.

### Rendering

Both surfaces resolve through the same read path: the `/notifications` page (`Notifications.razor.cs`,
via `NotificationTable`) and the startup popups (`StartupSuccessModal`/`StartupErrorModal`, via
`NotificationSummary`). Neither renders text directly today — both bind whatever the reader returned —
so if the reader resolves correctly, both surfaces follow without markup changes beyond passing the
language through.

---

## Open questions — for the developer, not to be assumed

1. **Which request signal selects a notification's language — `?lang=` or `Accept-Language`?**
   CLAUDE.md draws a hard line here and explicitly forbids conflating the two: `?lang=` selects *quote
   content*; `Accept-Language` (via `IApiLocalizer`/`CurrentUICulture`) selects *UI strings and error
   messages*. A notification is genuinely both — persisted content shaped like a UI message — so the
   existing rule does not decide it.
   - **(a) `Accept-Language`/`CurrentUICulture`.** Treats notifications as what they read like: UI
     messages that happen to be persisted. The Blazor surfaces get it for free, since they already run
     under the request's culture. `GET /api/v1/notifications` would follow the header like an error
     message does.
   - **(b) `?lang=`, mirroring quotes.** Consistent with the storage model being copied wholesale, and
     with `EffectiveLanguage`/`isTranslated` already being a quote-response idiom. Requires the Blazor
     pages to pass a language explicitly rather than inheriting it.
   - **(c) Both — `?lang=` when supplied, `Accept-Language` otherwise.** Most convenient, but it is
     exactly the conflation CLAUDE.md warns about, and it makes the answer to "what language is this
     notification in" depend on two independent inputs.

   **Recommendation: (a).** The text is UI-surface prose, its strings come from `UI.*.json`, and the
   two places it renders are both UI. Note this would be the first read path where a `System_`-owned
   row resolves by `CurrentUICulture` — worth stating in the ADR-adjacent docs if chosen.

2. **#309's tables are not domain-prefixed — fix in #309 before this issue adds a sibling?**
   `ChangelogContentMigrations.cs:30,48` create `Changelog` and `ChangelogLine`, and
   `Sql.cs:105` creates `ChangelogSchemaVersion` — none carry a prefix, while ADR 015 binds every
   `Quotinator.Data`-owned table to `Import_`/`Audit_`/`System_` with no exception, and both #309's own
   plan doc and ADR 005's revision call it `System_Changelog`. This issue adds
   `System_NotificationTranslation` right next to them, so the inconsistency becomes visible either way.
   It is #309's defect, not this one's — but it is cheapest to fix before more tables land beside it.
   See "Finding" below.

---

## Steps

Numbered on approval of the above. Question 1 decides the read-path signature and therefore most of the
tests, so numbering them first would produce steps that need rewriting.

---

## Finding — not this issue's to fix

**#309 shipped three tables without ADR 015's domain prefix** (`Changelog`, `ChangelogLine`,
`ChangelogSchemaVersion`). ADR 015 states the rule for `Quotinator.Data`'s own tables with no exception,
and #309's own plan doc and ADR 005's revision both name the table `System_Changelog`. #309 is
`Waiting for release` on this branch and has not shipped in any tagged release.

**It is not automatically safe to edit the migration**, per ADR 015's own #254 revision: "unreleased" is
not the test — the test is whether any real database has already applied it, which includes the
developer's own local database. That is why this is raised rather than fixed.

Recorded here because #319 is the issue that would otherwise silently sit next to it. Resolution belongs
to #309 (reopen) or to a new issue, at the developer's direction.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `System_Notification` gains `OriginalLanguage`, existing rows defaulting to `en` | Unit test | Real-SQLite migration test asserting the backfilled value, not just the column's presence |
| 2 | ❌ | `System_NotificationTranslation` exists with `RecordBase`'s columns | Unit test | Existing `RecordBase` schema-conformance test pattern (ADR 002) |
| 3 | ❌ | Baseline and incremental replay produce an identical schema for both tables | Unit test | `DataOwnedBaseline_And_IncrementalReplay_…` parity tests (existing, must still pass) |
| 4 | ❌ | Writing a notification with translations persists one row per language | Unit test | Real SQLite, via `INotificationWriter.WriteAsync` |
| 5 | ❌ | Reading in a translated language returns the translated title and body | Unit test | Real SQLite |
| 6 | ❌ | Reading in an untranslated language falls back to the original text | Unit test | Real SQLite — the transparent-fallback contract |
| 7 | ❌ | A translation supplying a body but no title falls back to the original title only | Unit test | Guards `COALESCE` being per-field, not per-row |
| 8 | ❌ | Language matching is case-insensitive (`NL` resolves the `nl` row) | Unit test | Real SQLite; `TextClauses.Equals`, per the project-wide rule |
| 9 | ❌ | `EffectiveLanguage` reports the language actually returned | Unit test | Both the translated and fallback cases |
| 10 | ❌ | All four notification queries resolve translations, not only the list | Unit test | `SelectActive`, `SelectPage`, `SelectById` — a missed `@lang` binding on one is the likely defect |
| 11 | ❌ | Identity/dedupe is unaffected by text or language | Unit test | `SeedOnceAsync` still suppresses a duplicate whose translations differ |
| 12 | ❌ | The one already-persisted notification (#279's v1.8.3 announcement) gains translations via migration | Unit test | Real-SQLite test from a database at the pre-#319 schema carrying that row — asserts the translations exist, and that a database without the row gains nothing |
| 13 | ❌ | #279's and #289's producers write translations from `UI.*.json` | Unit test | Per-producer |
| 14 | ❌ | #81's producer writes translations from the `Changelog` table's per-language rows | Unit test | Per-producer |
| 15 | ❌ | Every new key exists in all three locale files | Unit test | `TranslationCompletenessTests` (existing) |
| 16 | ❌ | Both surfaces render resolved text | Live (T1) | Notifications page and startup popup, UI switched to `nl` — screenshot, not text extraction |
| 17 | ❌ | Migration applies cleanly to a database at the previous released schema | Live (T2) | ADR 009, plus smoke-test 39e's intermediate-version check |
| 18 | ❌ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 19 | ❌ | Full test suite green | Build | `dotnet test --configuration Release -m:1` |
