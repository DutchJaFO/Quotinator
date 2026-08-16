# #319 — Notification title and body are not translated

**Status:** Planning
**GitHub issue:** #319 (open)
**Depends on:** #278 (done, released v1.8.0 — the mechanism), #312 (done, this branch — the Title/Body split and the metadata contract that deliberately excludes text)

> **Next action: execute the Steps.** This plan is ready. Schema, read-side resolution, language
> selection and the backfill are all settled (developer decisions, 2026-08-16). The one open question
> below is a decision for #309, not a blocker for this issue — it only affects whether a neighbouring
> table gets renamed before or after this one lands.

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
  changelog files it already reads" — since #309 that content is read from a database, not the JSON
  files, but the per-language guarantee the claim depends on does hold. Note that it is a *separate*
  database (#309, ADR 018), so this is a cross-database read, not a join — see step 9 and "Finding".

**The issue body's `System_NotificationTranslation` name is correct.** It lives in the main database,
where ADR 015's prefix rule applies unambiguously.

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

### Language selection — two inputs, one precedence rule

Decided 2026-08-16: **the setting that determines the UI language determines a notification's language,
and the REST endpoints additionally take a `lang` parameter, the way the quote endpoints do.**

| Consumer | Language used |
|---|---|
| Blazor Notifications page, startup popups | `CultureInfo.CurrentUICulture` — the culture the `LanguageSelector` cookie and `Accept-Language` already resolve to |
| `GET /api/v1/notifications`, `POST /api/v1/notifications/{id}/dismiss` | `lang` when supplied; `CurrentUICulture` otherwise |

**This is a deliberate extension of CLAUDE.md's language rule, not a violation of it** — worth stating
plainly, because that rule reads as forbidding exactly this shape. It separates two cases: `?lang=`
selects *quote content*, `Accept-Language` selects *UI strings and error messages*. A notification is a
third case the rule predates: persisted content that reads as a UI message. It gets the content
treatment on the API (a `lang` parameter, transparent fallback, an echoed effective language) and the
UI treatment everywhere it renders. The prohibition that still stands unchanged is the specific one:
`?lang=` must never drive *error message* language.

`lang` goes through `InputValidation.TryNormalizeLang` — the single choke point every `?lang=`-accepting
endpoint already calls (`QuoteEndpoints.cs:98`, `ConversationEndpoints.cs:95`) — before reaching a SQL
comparison or being echoed back. It lowercases the value, which is why the SQL side still needs its own
`TextClauses.Equals` wrap for the never-canonicalised `Language` column.

`GetActiveNotificationsAsync` has exactly one consumer, `NotificationSummary.razor.cs:26`, and no REST
endpoint — so the active-notification path takes `CurrentUICulture` only, with no parameter to add.

### Response shape

`NotificationResponse` gains the three fields CLAUDE.md's "API response language" section requires of
every read endpoint that accepts `lang`, matching `QuoteResponse`:

- `language` — the language actually returned
- `originalLanguage` — the notification's own `OriginalLanguage`
- `isTranslated` — `true` when the two differ

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

## Open question — for the developer, not to be assumed

1. **What are the table-naming rules when an application has more than one database?** ADR 015 assumes
   one, and the changelog database is effectively a second user-domain database. Needs a clarification
   ADR; not a blocker for this issue, whose own table lives in the main database. See "Finding" below.

---

## Steps

Red-first throughout, per `docs/testing-policy.md`. Steps 1–3 are storage, 4–6 the read path, 7–9 the
producers, 10 the surfaces.

### 1. Schema

**Status:** ⬜ Not started

`System_Notification.OriginalLanguage` via `ALTER TABLE … ADD COLUMN` (`NOT NULL DEFAULT 'en'`), and a
new `System_NotificationTranslation` table — `RecordBase` per ADR 002, `System_` prefix and singular per
ADR 015, columns `NotificationId`/`Language`/`Title`/`Body` with `Title` nullable. No CHECK on
`Language`, matching `Quotinator_QuoteTranslation`. Update `DataBaselineSql` in the same commit; the
schema-drift parity tests fail otherwise.

### 2. Entity and translation DTO

**Status:** ⬜ Not started

`NotificationTranslationEntity` mirroring `QuoteTranslationEntity`, plus the
`NotificationTranslationDto(Language, Title, Body)` record the write side takes.

### 3. Backfill migration for the one existing row

**Status:** ⬜ Not started

Translations for #279's v1.8.3 announcement — the only notification in any released database — sourced
from `UI.*.json`, in the `NotificationLegacyMetadataMigrations` family that already updates that row.
A new migration rather than an edit to 8 or 9 if either has already been applied anywhere, including
locally. Must be conditional on the row existing, exactly as migration 9 already is.

### 4. Read-path SQL

**Status:** ⬜ Not started

`Sql.Notifications.SelectColumns` becomes a projection over a `LEFT JOIN` on
`System_NotificationTranslation`, with `COALESCE` per field and an `EffectiveLanguage` `CASE` — the
`Sql.Quotes.SelectBase` shape. `@lang` bound on all of `SelectActive`, `SelectPage` and `SelectById`;
a missed binding on one is the likely defect, so row 10 tests each.

### 5. Reader signature

**Status:** ⬜ Not started

`INotificationReader`'s three methods take the requested language. `GetActiveNotificationsAsync`'s only
caller is Blazor, so it takes `CurrentUICulture`'s value from the component rather than growing a
parameter with no second caller — decide at implementation which reads better.

### 6. Write side

**Status:** ⬜ Not started

`INotificationWriter.WriteAsync` and `NotificationSeeding.SeedOnceAsync` take
`IReadOnlyList<NotificationTranslationDto>`, persisting one row per language in the same transaction as
the notification. Identity comparison is untouched — it is structural, from the metadata payload, and
has never involved text (row 11 guards this).

### 7. Endpoint parameter

**Status:** ⬜ Not started

`lang` added to `GET /api/v1/notifications` and `POST /api/v1/notifications/{id}/dismiss`, normalised
via `InputValidation.TryNormalizeLang`, falling back to `CurrentUICulture` when absent. `[Description]`
attributes on both, and `docs/api-endpoints.md` updated in the same commit per CLAUDE.md's
keep-API-docs-in-sync rule.

### 8. Response shape

**Status:** ⬜ Not started

`NotificationResponse` gains `language`/`originalLanguage`/`isTranslated`, populated in `ToResponse`.

### 9. Producers

**Status:** ⬜ Not started

#279's and #289's producers (`Program.cs`) supply translations from `UI.*.json`; #81's
(`Startup/WhatsNewNotification.cs`) from the `Changelog` table's per-language rows. Reading every
language at once is not what `IApiLocalizer` does — it resolves one, from `CurrentUICulture` — so this
needs an all-languages reader that resolves no culture at all. A startup producer has no request
culture, which is the reason this issue exists.

### 10. Surfaces

**Status:** ⬜ Not started

`NotificationSummary` and `NotificationTable` pass `CurrentUICulture`'s language through to the reader.
Neither renders text itself today, so no markup change beyond threading the language.

### 11. Docs and housekeeping

**Status:** ⬜ Not started

New `UI.*.json` keys in all three locales; `data/changelog/changelog.{en,nl,de}.json` `unreleased`
entries in lockstep; every touched file added to `.editorconfig`'s scoped `IDE0008` list with its `var`
declarations converted; `[Subsystem - Phase]` prefixes on any new log lines.

### 12. Verification

**Status:** ⬜ Not started

Work the table below top to bottom. T2 before T1, per `docs/release-verification.md`.

---

## Finding — ADR 015 assumes one database per application

**No table needs renaming, and no ADR is wrong.** This section originally recorded #309's
`Changelog`/`ChangelogLine`/`ChangelogSchemaVersion` as an ADR 015 violation. That was wrong, and is
corrected in place rather than deleted, because misreading it is the evidence for what the actual gap is.

**The separation is decided at ADR level, not in a code comment.** ADR 018's "Database placement"
section rules that file-authored system content moves to a separate database when it has no
transactional coupling to domain writes, names `System_Changelog` as its reference example, and adds
that every database gets the same migration capability without exception. `ChangelogContentMigrations`'
class doc restates the naming consequence: a dedicated, single-purpose database has nothing to
disambiguate from, since ADR 015's prefixes substitute for SQLite's lack of schema qualification *within
one flat namespace*.

**ADR 005 is not wrong either** (developer clarification, 2026-08-16). Its `System_Changelog` naming was
correct under the assumption in force when it was written — one database per application. ADR 018 later
moved the content to its own database, and the implementation dropped the prefix as a consequence of
that move. Neither document accounted for an application having more than one database, because at the
time none did.

**The changelog database is effectively a second user-domain database** (developer clarification,
2026-08-16), not a system database — which is the part the existing vocabulary has no answer for. ADR
015 defines `Import_`/`Audit_`/`System_` for `Quotinator.Data`'s own tables and one prefix
(`Quotinator_`) for the consuming application's domain tables, all within a single namespace. It says
nothing about what naming applies in a second database, or whether a database holding one domain needs a
prefix at all.

**So the open question is an ADR-level one:** when an application has more than one database, what are
the naming and ownership rules per database? That is a clarification ADR (or an ADR 015 revision), not a
correction to anything already written. #309's plan doc would follow whatever it settles.

**Nothing in this issue depends on the outcome.** `System_NotificationTranslation` lives in the main
database, where ADR 015 applies unambiguously.

**One thing to carry into step 9:** the changelog being a separate database means #81's producer reads
its per-language rows across a database boundary, not with a join. Worth confirming at implementation
that the existing changelog reader already exposes what that producer needs.

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
| 16 | ❌ | `GET /notifications?lang=nl` returns Dutch text | Unit test | Endpoint test |
| 17 | ❌ | With no `lang`, the endpoint follows the request culture | Unit test | Endpoint test with `Accept-Language: nl` |
| 18 | ❌ | `lang` takes precedence over the request culture when both are present | Unit test | Endpoint test — `Accept-Language: de` plus `?lang=nl` returns Dutch |
| 19 | ❌ | A malformed `lang` is rejected consistently with the quote endpoints | Unit test | `InputValidation.TryNormalizeLang`'s existing contract — same status code as `/quotes` returns for the same input |
| 20 | ❌ | `language`/`originalLanguage`/`isTranslated` are populated correctly | Unit test | Endpoint test, translated and fallback cases |
| 21 | ❌ | The dismiss endpoint resolves text the same way | Unit test | Endpoint test — it echoes the notification back |
| 22 | ❌ | Both surfaces render resolved text | Live (T1) | Notifications page and startup popup, UI switched to `nl` — screenshot, not text extraction |
| 23 | ❌ | Migration applies cleanly to a database at the previous released schema | Live (T2) | ADR 009, plus smoke-test 39e's intermediate-version check |
| 24 | ❌ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 25 | ❌ | Full test suite green | Build | `dotnet test --configuration Release -m:1` |
