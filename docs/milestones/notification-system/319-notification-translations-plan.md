# #319 — Notification title and body are not translated

**Status:** In progress (step 12)
**GitHub issue:** #319
**Tiers required:** T1, T2
**Depends on:** #278, #312

> **Next action: execute the Steps.** This plan is ready. Schema, read-side resolution, language
> selection, the backfill and the producer sources are all settled; nothing here is waiting on a
> decision.

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
  languages: `ChangelogEntryEntity` carries a `Language` column, and `ChangelogLineEntity` carries the
  `AudienceKey` #307's reserved `notification` highlights use. All three locale files carry
  `audienceHighlights.notification` today. The issue says "from the per-language changelog files it
  already reads" — since #309 that content is read from a database, not the JSON files, but the
  per-language guarantee the claim depends on does hold. Note that it is a *separate* database
  (#309, ADR 018), so this is a cross-database read, not a join — see step 9.

**The issue body's `System_NotificationTranslation` name is correct.** It lives in the main database,
where ADR 015's prefix rule applies unambiguously.

## Governing standards

The rules this issue's design is bound by, and where each is satisfied.

| Standard | Bearing on this issue | Where |
|---|---|---|
| [ADR 002](../../architecture-decisions/002-recordbase-on-all-tables.md) | The new table carries `RecordBase` | Step 1, row 2 |
| [ADR 008](../../architecture-decisions/008-enum-backed-columns-require-check-constraints.md) | Governs enum-backed columns only; a language code is not one, so no CHECK | Step 1 |
| [ADR 012](../../architecture-decisions/012-canonicalize-entity-ids-at-capture.md) + `SqlSelectPresentationGuard` | Aliased id columns in the projection go through `IdClauses.SelectColumn` | Step 4 |
| [ADR 015](../../architecture-decisions/015-domain-prefixed-table-naming.md) | `System_` domain, singular | Step 1 |
| [ADR 016](../../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md) | The four suffixes name a wire or persistence boundary; the write-side type crosses neither, so it is unsuffixed | Step 2 |
| [ADR 017](../../architecture-decisions/017-join-capable-reads-use-joinqueryrepository.md) | The read path is a two-table projection returning a concrete POCO, so it executes through `JoinQueryRepository`/`IJoinStrategy` | Step 5 |
| [ADR 020](../../architecture-decisions/020-openapi-tags-are-declared-with-descriptions.md) | No new endpoint group; `ApiTags.Notifications` is already declared with a description | Nothing to do |
| CLAUDE.md — DI policy | Three same-typed join repositories cannot resolve by type, so the reader is registered through the service-provider factory overload | Step 5 |
| CLAUDE.md — case-insensitive comparison | `TextClauses.Equals` on the translation's `Language` | Step 4 |
| CLAUDE.md — string centralisation | SQL stays in `Sql.cs`; strategies only return it | Step 4 |
| CLAUDE.md — API docs in sync | `docs/api-endpoints.md` and `[Description]` for `lang` | Step 7 |
| CLAUDE.md — `var` boyscout | Each file joins `.editorconfig`'s `IDE0008` list as it is first touched | Per step |
| Blazor code-behind rule | Both surfaces already use code-behind | Step 10 |

## Design

### Schema

| Change | Detail |
|---|---|
| `System_Notification.OriginalLanguage` | ISO 639-1, `NOT NULL DEFAULT 'en'`. Backfilling existing rows to `en` is a statement of fact — every notification written to date is English — not a guess |
| `System_NotificationTranslation` | New table: `RecordBase` per ADR 002, `System_` prefix and singular per ADR 015. Columns `NotificationId`, `Language`, `Title` (nullable — a notification may have a body and no title), `Body` |

`OriginalLanguage` can be added with `ALTER TABLE … ADD COLUMN` (no CHECK widening involved), so this
does not need a table rebuild. Baseline updated to match in the same commit, per the schema-drift parity
tests. Delivered as `DataOwnedMigrations` 12 and 13 — one schema change each, per CLAUDE.md.

**No CHECK constraint on `Language`.** ADR 008 governs enum-backed columns; a language code is not
enum-backed here, and `Quotinator_QuoteTranslation.Language` has no CHECK either. Consistent with the
precedent rather than stricter than it.

### Read-side resolution

`Sql.Notifications.SelectColumns` (`src/Quotinator.Data/Queries/Sql.cs:581-584`) becomes a projection
over a `LEFT JOIN`, mirroring `Sql.Quotes.SelectBase` exactly:

- `COALESCE(t.Title, n.Title) AS Title`, `COALESCE(t.Body, n.Body) AS Body`
- join condition `{IdClauses.Join("t.NotificationId", "n.Id")} AND {TextClauses.Equals("t.Language", "lang")} AND t.IsDeleted = 0`
- `CASE WHEN t.Body IS NOT NULL THEN LOWER(@lang) ELSE n.OriginalLanguage END AS EffectiveLanguage`

Three queries share the projection — `SelectActive`, `SelectPage` and `SelectById` — so `@lang` must be
bound on every one, `null` when no language is requested, exactly as `Sql.Quotes` does. `CountAll` is a
bare `SELECT COUNT(*)` that does not use `SelectColumns`, so it neither gains the join nor needs `@lang`.

`TextClauses.Equals` rather than a hand-written `LOWER(…) = LOWER(…)`, per the case-insensitive-by-default
rule: a translation's `Language` is never canonicalised at capture, so the SQL side needs its own wrap.

**Fallback is per-field, not per-row** — `COALESCE` on `Title` and `Body` independently. A translation
row supplying a body but no title falls back to the original title rather than dropping it.

### Write side

`INotificationWriter.WriteAsync` gains a translations parameter, so a producer supplies every language
in one call rather than the row being enriched afterward (issue requirement 3). Shape:

```
IReadOnlyList<NotificationTranslation>   // (Language, Title, Body)
```

A list of a small record rather than a dictionary: `Title` is nullable and independent of `Body`, which
a `Dictionary<string, string>` cannot express. Unsuffixed rather than `…Dto` — see step 2 for why
ADR 016 puts it outside the four-suffix scheme.

`NotificationSeeding.SeedOnceAsync` threads the same parameter through. **Its identity comparison is
untouched, and the separate translation table is what guarantees that.** Stated precisely, because the
looser version — "identity has never involved text" — is not true and invites a false alarm: two of the
three producers hash their body into the payload's `ContentHash` (`NotificationContentHash.Of(...)` in
#279's announcement and in #81's unreleased what's-new seed). What makes that safe is that the
original-language text stays on `System_Notification.Body`, exactly as `Quotinator_Quote.QuoteText` does,
while translations go to `System_NotificationTranslation`. The hashed string is therefore byte-identical
before and after this issue. `SeedOnceAsync` itself compares only the deserialised metadata payload —
`body` is passed straight to `WriteAsync` and never enters the comparison.

That invariant is structural, not incidental: `COALESCE(t.Body, n.Body)` only resolves if the original
stays on the parent row and the translation table holds *other* languages. Never write an
original-language row into `System_NotificationTranslation`.

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

`GetActiveNotificationsAsync` has exactly one consumer, `NotificationSummary.razor.cs:38`, and no REST
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

That row is **already repaired by migrations of its own** — `NotificationLegacyMetadataMigrations`
(8 and 9) gave it #312's structured metadata and its missing provenance, so its translations are the
same kind of work and land the same way: a new migration, never an edit to 8 or 9, both of which have
been applied to real databases and are frozen per ADR 015.

It lives in `NotificationTranslationMigrations` alongside the two schema migrations it depends on,
since it backfills this issue's own table. The end-of-milestone consolidation pass the overview already
plans can fold the family however it prefers.

The translated text comes from `UI.*.json`, the same source the #279 producer itself will use.

### Producers

| Producer | Source of translations |
|---|---|
| #279 announcement (`Program.cs`) | `i18ntext/UI.*.json` |
| #289 schema-version overshoot (`Program.cs`) | `i18ntext/UI.*.json` |
| #81 what's-new body (`Startup/WhatsNewNotification.cs`) | The `Changelog` table's per-language rows — already translated at source, so nothing is translated here |
| #81 what's-new **title** (same file) | `i18ntext/UI.*.json` — the changelog has no title to give |

**#81's titles are not in the changelog, and are hardcoded English today.** `$"What's new in v{release.Version}"`
and `"What's new (unreleased)"` are C# literals in `WhatsNewNotification.cs` — a live string-centralisation
violation this issue is the natural place to fix. They need `UI.*.json` keys, with the version substituted
via `IApiLocalizer.Format`'s `{0}` mechanism rather than interpolated into the resolved string. Only the
*body* comes from the changelog.

Reading `UI.*.json` for all three languages at write time is not what `IApiLocalizer` does — it resolves
one language, from `CurrentUICulture`. This issue needs *every* language at once. Whether that is a new
method on `IApiLocalizer` or a separate reader is a step-level decision, but it must not resolve a
culture: a producer running at startup has no request culture to speak of, which is the whole reason
this issue exists.

**#289's producer stays in scope, and its text will be written twice** (developer decision, 2026-08-29).
#350 rewrites the overshoot notification's wording entirely — it drops the "schema is complete / app
working normally" claims, names restoring a backup as a second remedy alongside Reset, and moves the
notification off the `dbHealth.IsHealthy` gate. #350 is sequenced after this issue, so the three
translations written here are discarded there and re-written against #350's own wording. Accepted
deliberately rather than resequencing: recorded here so the rework reads as a known cost, not as this
plan having missed the conflict. Issue requirement 7 (substituting both recorded schema versions into
each language's text) stays with this issue and is re-applied by #350 to its replacement text.

### Rendering

Both surfaces resolve through the same read path: the `/notifications` page (`Notifications.razor.cs`,
via `NotificationTable`) and the startup popups (`StartupSuccessModal`/`StartupErrorModal`, via
`NotificationSummary`, which itself delegates to `NotificationTable`). Neither renders text directly —
both bind whatever the reader returned — so if the reader resolves correctly, both surfaces follow
without markup changes beyond passing the language through.

**Only `Body` is rendered anywhere today.** `NotificationTable.razor:27` prints `@notification.Body` and
nothing prints `Title`, which has been stored but unrendered since #312. So this issue can only prove a
translated *body* live; proving a translated *title* belongs to #308, which adds per-type layout across
both surfaces. Row 24 is scoped to `Body` for that reason (developer decision, 2026-08-29) — deliberately
not by adding markup here, which would pre-empt the layout #308 exists to settle.

---

## Steps

Red-first throughout, per `docs/testing-policy.md`. Steps 1–3 are storage, 4–6 the read path, 7–9 the
producers, 10 the surfaces.

### 1. Schema

**Status:** ✅ Done

`System_Notification.OriginalLanguage` via `ALTER TABLE … ADD COLUMN` (`NOT NULL DEFAULT 'en'`), and a
new `System_NotificationTranslation` table — `RecordBase` per ADR 002, `System_` prefix and singular per
ADR 015, columns `NotificationId`/`Language`/`Title`/`Body` with `Title` nullable. No CHECK on
`Language`, matching `Quotinator_QuoteTranslation`. Update `DataBaselineSql` in the same commit; the
schema-drift parity tests fail otherwise.

Delivered as **two** migrations, not one — `NotificationTranslationMigrations.AddOriginalLanguageColumn`
(12) and `.CreateNotificationTranslationTable` (13) — per CLAUDE.md's one-schema-change-per-migration
rule; the two are independent, so bundling them would only make a partial failure harder to reason
about. `UNIQUE (NotificationId, Language)` mirrors `Quotinator_QuoteTranslation`'s own constraint, so
one-translation-per-language is enforced by the database rather than by each producer.

In the baseline, `OriginalLanguage` trails `AppVersionId` and the new table trails
`System_Notification`: the parity test compares column *ordinals*, and an `ALTER TABLE … ADD COLUMN`
always appends — the same reason `Title`/`Metadata`/`MetadataKind`/`AppVersionId` already trail
`IsDeleted` there.

**Three existing tests pinned the Data migration count at 11 and went red on 13** —
`ApplyBaselineAsync_NoConsumerBaselineDefined_FallsThroughToIncremental` plus two in
`Quotinator.Core.Tests.DatabaseInitializerTests`. Updated to 13, which is the intended maintenance step
for these: they exist to pin the count, so every migration addition moves them.

`NotificationTranslationTests` lives in `tests/Quotinator.Data.Tests/Repositories/` rather than
`Database/`, since most of the methods the issue names for it are reader/writer behaviour; its one
migration test uses `TempDatabase` + the migration constant directly, the same technique
`NotificationLegacyBackfillMigrationTests` established.

### 2. Entity and translation value type

**Status:** ✅ Done

`NotificationTranslationEntity` mirroring `QuoteTranslationEntity`, plus the
`NotificationTranslation(Language, Title, Body)` record the write side takes.

**The value type is unsuffixed.** ADR 016's four suffixes each name a boundary: `Entity` a table
mapping, `Request`/`Response` an HTTP body, `Dto` a wire-format shape — an on-disk JSON file, or a JSON
blob serialized into and read back out of a database column, which is what `NotificationMetadataDto`
genuinely is. This type crosses none of them: it is never serialized, only carried from a producer into
`WriteAsync`, where each instance becomes a row. ADR 016 answers "which boundary does this type exist
to carry data across", and the answer here is none of the four, so it takes no suffix — the same
position `MasterDataReference` and `SeedBatch` hold.

### 3. Backfill migration for the one existing row

**Status:** ✅ Done

**Migration 14 matches the announcement by `MetadataKind` plus the payload's own `announcement` key,
read with `json_extract` — never by the whole `Metadata` string.** Migration 11 `json_insert`s further
fields into that column, so the value v1.8.3 wrote is not the value this migration meets; a whole-string
comparison matches nothing on any database that ran 11, which is every upgraded one. Found by a T1 pass
showing the announcement still in English beside a correctly translated notification.

**That predicate was corrected in place after the migration had already run, as a one-time exception**
(developer decision, 2026-08-29). It rests on the developer restoring a v1.8.3 backup between test runs,
so no database was left stranded at a version whose script had changed. ADR 015's frozen-migration rule
is unchanged and still governs: "unreleased" is not the test, and the next correction in this position
takes a new version number.

Translations for #279's v1.8.3 announcement — the only notification in any released database — sourced
from `UI.*.json`. A new migration (14), never an edit to 8 or 9: both have been applied to real
databases, and an applied migration is frozen. Conditional on the row existing, exactly as migration 9
is, so a database that never ran v1.8.3 gains nothing.

Placed in `NotificationTranslationMigrations` alongside this issue's own two schema migrations rather
than in the `NotificationLegacyMetadataMigrations` family: it backfills *this issue's* table, and
grouping it with the schema it depends on keeps the three readable together. The end-of-milestone
consolidation pass can fold them however it prefers.

`NotificationOperationIdRenameTitle`/`…Body` added to all three `UI.*.json` files in the same commit;
the migration embeds a frozen copy of the same strings, which is required rather than sloppy — migration
text must not follow a later edit to those keys.

**Row 12's test now reproduces the field bug.** Pointed at migration 14 it fails; at 15 it passes — the
fixture runs #312's own backfills over the seeded row rather than seeding their result, so it meets the
shape an upgrade actually produces.

**This step's tests rest on a mutation check, not on an observed red run** — the weaker of the two, and
the one thing about this step's coverage a reader should not assume. Mutating the migration's `'nl'`
literal to `'xx'` turns two of them red, which proves they are wired to the behaviour; it does not
prove they would fail against the feature's absence. Treat that as the evidence available for rows 1,
2 and 12, not as equivalent to red-first.

### 4. Read-path SQL

**Status:** ✅ Done

`Sql.Notifications.SelectColumns` becomes a projection over a `LEFT JOIN` on
`System_NotificationTranslation`, with `COALESCE` per field and an `EffectiveLanguage` `CASE` — the
`Sql.Quotes.SelectBase` shape. `@lang` bound on `SelectActive`, `SelectPage` and `SelectById`;
a missed binding on one is the likely defect, so row 10 tests each.

Aliasing the tables means every selected id column must go through
`IdClauses.SelectColumn("n.Id", "Id")` / `("n.AppVersionId", "AppVersionId")` — `SqlSelectPresentationGuard`
enforces this mechanically and will fail the build otherwise. The SQL stays in `Sql.cs`; the strategies
in step 5 only return it, which is also what keeps the guard tests scanning it.

### 5. Reader — via `JoinQueryRepository`/`IJoinStrategy`, per ADR 017

**Status:** ✅ Done

Two details the mechanism requires:

- **`NotificationEntity` gained `EffectiveLanguage` as `[Computed]`.** It is a property of one query's
  result, not a column, and `ReflectedColumnMetadata` already excludes `[Computed]` from persisted
  columns exactly as Dapper.Contrib does — so the projection can surface it without the entity
  claiming a column that does not exist.
- **The missing-table catch now also matches `System_NotificationTranslation`.** The two tables arrive
  in separate migrations, so the degraded state #263/#280 protects can now be reached with one present
  and the other not.

Three existing fixtures (`NotificationReaderTests`, `NotificationWriterTests`,
`NotificationSeedingTests`) build their schema from a hand-listed migration sequence and went red the
moment the entity gained a real column their tables lacked; each now applies migrations 12 and 13 too.
`TestNotificationReader` was added so the three-argument construction is written once rather than at
five call sites, where a partially-wired reader would still compile.

**SQL execution goes through `JoinQueryRepository`/`IJoinStrategy`, per ADR 017**: "Any read that joins
two or more tables, or returns a multi-table projection, uses
`JoinQueryRepository<TResult>`/`IJoinStrategy<TResult>` … even when adopting it unlocks no new
capability." Step 4 makes all three notification queries exactly that. The ADR's one exemption
(`ConversationLineCountReader`'s `QueryAsync<dynamic>`) does not apply, since `NotificationEntity` is a
concrete POCO, and "no gain" is explicitly refused as a reason. The join is what brings this reader
into the ADR's scope — single-table reads were outside it.

Three `IJoinStrategy<NotificationEntity>` implementations (active, page, by-id), each returning its
`Sql.Notifications` constant. `CountAll` stays a plain scalar — no join, no projection, outside ADR 017.
`NotificationWriter.DismissAsync` reads through the by-id strategy too, since it returns the same
projection.

**DI: one row type, three repositories constructed in the registration.** The codebase's usual shape is
one distinct `TRow` per strategy (`JoinQueryRepository<SourceRow>`, `<LinkRow>`, …) so DI resolves them
by type. That does not work here — all three return `NotificationEntity`, and three registrations of the
same closed generic would silently collapse to the last one. Inventing three identical row types purely
to satisfy type-based resolution would be worse to read than the problem it solves, so instead the
reader and writer are registered with the service-provider factory overload
(`AddSingleton<INotificationReader>(sp => new NotificationReader(new JoinQueryRepository<…>(…), …))`),
which CLAUDE.md's DI policy names as the correct move whenever the container cannot supply a
constructor argument. No bare `new` at a call site.

**The missing-table catch stays in the reader**, not the repository. `JoinQueryRepository.QueryAsync`
does not catch, and `IsMissingNotificationTable`'s degraded-state behaviour (#263/#280 — the
Notifications page and startup modal stay reachable mid-migration) is load-bearing. ADR 017 explicitly
allows a domain reader above the mechanism, so this composes rather than conflicts.

`INotificationReader`'s two methods take the requested language; `GetActiveNotificationsAsync`'s only
caller is Blazor, which passes `CurrentUICulture`'s value.

**Boyscout:** `NotificationReader.cs` is `var`-heavy and not yet in `.editorconfig`'s `IDE0008` list.
Touching it means converting its declarations to explicit types and adding it to that list in the same
commit.

### 6. Write side

**Status:** ✅ Done

`INotificationWriter.WriteAsync` and `NotificationSeeding.SeedOnceAsync` take
`IReadOnlyList<NotificationTranslationDto>`, persisting one row per language in the same transaction as
the notification. Identity comparison is untouched: `SeedOnceAsync` compares only the metadata payload,
and the payload's `ContentHash` keeps hashing the original-language `Body` on the notification row, which
this issue does not move (row 11 guards this — see "Write side" for why the looser "identity never
involves text" phrasing is wrong). The original language is never written as a translation row.

### 7. Endpoint parameter

**Status:** ✅ Done

`lang` added to `GET /api/v1/notifications` and `POST /api/v1/notifications/{id}/dismiss`, normalised
via `InputValidation.TryNormalizeLang`, falling back to `CurrentUICulture` when absent. `[Description]`
attributes on both, and `docs/api-endpoints.md` updated in the same commit per CLAUDE.md's
keep-API-docs-in-sync rule.

### 8. Response shape

**Status:** ✅ Done

`NotificationResponse` gains `language`/`originalLanguage`/`isTranslated`, populated in `ToResponse`.

### 9. Producers

**Status:** ✅ Done

#279's and #289's producers (`Program.cs`) supply translations from `UI.*.json`; #81's
(`Startup/WhatsNewNotification.cs`) supplies its body from the `Changelog` table's per-language rows and
its title from `UI.*.json` (see "Producers" — the changelog has no title to give, and the two literals
there today are hardcoded English). Reading every language at once is not what `IApiLocalizer` does — it
resolves one, from `CurrentUICulture` — so this needs an all-languages reader that resolves no culture at
all. A startup producer has no request culture, which is the reason this issue exists.

**#81 needs no new changelog API — it needs one guard.** The changelog lives in its own database
(#309, ADR 018), so this is a cross-database read, not a join. `IChangelogReader.GetDocumentAsync(culture)`
resolves one language per call, which is sufficient: call it once per language. What it does *not* do is
fail when a language is missing — it falls back to `en` and returns the English document. The returned
`ChangelogDocument` carries its own `Language`, so compare that against the code requested and **skip
writing the translation row when they differ**. Writing it anyway would persist English text in an `nl`
row, and the read path would then report `language: nl, isTranslated: true` for text that is English —
strictly worse than having no row, since the `COALESCE` fallback would otherwise correctly report
`language: en, isTranslated: false`, which is issue requirement 8 behaving as specified.

### 10. Surfaces

**Status:** ✅ Done

`NotificationSummary` and `NotificationTable` pass `CurrentUICulture`'s language through to the reader.
Neither composes text itself, so no markup change beyond threading the language. `NotificationTable`
renders `Body` only; `Title` stays unrendered until #308, and this step does not add it.

### 11. Docs

**Status:** ✅ Done

Two T2 documents added under `docs/automated-testing/notifications-and-changelog/` and registered in the
suite index and `Quotinator.slnx`: `08` covers read-time resolution on a fresh container, `09` the
upgrade backfill on a released database. Rows 24 and 25 are executed from these — a live row with
nothing to run it from is a promise, not a verification.

New `UI.*.json` keys in all three locales; `data/changelog/changelog.{en,nl,de}.json` `unreleased`
entries in lockstep; `[Subsystem - Phase]` prefixes on any new log lines.

**`.editorconfig` is not part of this step.** Per CLAUDE.md, a file joins the scoped `IDE0008` list
*the moment it is first touched*, with its `var` declarations converted in that same commit — so it
happens inside whichever step first opens each file, never batched here at the end. Listed as a
non-step so a reader does not go looking for it as one.

### 12. Verification

**Status:** 🔄 In progress — unit rows green; T1 and T2 outstanding

Work the table below top to bottom. T2 before T1, per `docs/release-verification.md`.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `System_Notification` gains `OriginalLanguage`, existing rows defaulting to `en` | Unit test | `NotificationTranslationTests.Migration_ExistingRows_DefaultToEnglishOriginalLanguage` — inserts a row at the pre-319 schema, runs migration 12, asserts the backfilled `en` |
| 2 | ✅ | `System_NotificationTranslation` exists with `RecordBase`'s columns | Unit test | `NotificationTranslationTests.NotificationTranslationTable_HasRecordBaseColumns` — all four audit columns plus its own four |
| 3 | ✅ | Baseline and incremental replay produce an identical schema for both tables | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationTranslationSchema` and `..._AgreeOnNotificationOriginalLanguage` |
| 4 | ✅ | Writing a notification with translations persists one row per language | Unit test | Real SQLite, via `INotificationWriter.WriteAsync` |
| 5 | ✅ | Reading in a translated language returns the translated title and body | Unit test | Real SQLite |
| 6 | ✅ | Reading in an untranslated language falls back to the original text | Unit test | Real SQLite — the transparent-fallback contract |
| 7 | ✅ | A translation supplying a body but no title falls back to the original title only | Unit test | Guards `COALESCE` being per-field, not per-row |
| 8 | ✅ | Language matching is case-insensitive (`NL` resolves the `nl` row) | Unit test | Real SQLite; `TextClauses.Equals`, per the project-wide rule |
| 9 | ✅ | `EffectiveLanguage` reports the language actually returned | Unit test | Both the translated and fallback cases |
| 10 | ✅ | All three projection-sharing queries resolve translations, not only the list | Unit test | `SelectActive`, `SelectPage`, `SelectById` — a missed `@lang` binding on one is the likely defect. `CountAll` is excluded: it does not use the projection |
| 11 | ✅ | Identity/dedupe is unaffected by text or language | Unit test | `SeedOnceAsync` still suppresses a duplicate whose translations differ — and a producer whose `ContentHash` covers its body still dedupes, proving the hashed original-language `Body` did not move |
| 12 | ✅ | The one already-persisted notification (#279's v1.8.3 announcement) gains translations via migration | Unit test | `NotificationTranslationTests.Migration_LegacyAnnouncementPresent_GainsDutchAndGermanTranslations`, `..._NoLegacyAnnouncement_WritesNoTranslations`, and `..._AppliedTwice_LeavesOneTranslationPerLanguage` |
| 13 | ✅ | #279's and #289's producers write translations from `UI.*.json` | Unit test | Per-producer |
| 14 | ✅ | #81's producer writes body translations from the `Changelog` table's per-language rows | Unit test | Per-producer |
| 15 | ✅ | #81's producer writes no translation row for a language the changelog lacks | Unit test | Changelog with `en` only; asserts no `nl` row is written and the read path reports `language: en, isTranslated: false` — guards `GetDocumentAsync`'s silent `en` fallback being persisted as a fake Dutch translation |
| 16 | ✅ | #81's titles resolve per language from `UI.*.json`, with the version substituted | Unit test | Per-producer — covers both the per-release title and the unreleased one, which are hardcoded English literals today |
| 17 | ✅ | Every new key exists in all three locale files | Unit test | `TranslationCompletenessTests` (existing) |
| 18 | ✅ | `GET /notifications?lang=nl` returns Dutch text | Unit test | Endpoint test |
| 19 | ✅ | With no `lang`, the endpoint follows the request culture | Unit test | Endpoint test with `Accept-Language: nl` |
| 20 | ✅ | `lang` takes precedence over the request culture when both are present | Unit test | Endpoint test — `Accept-Language: de` plus `?lang=nl` returns Dutch |
| 21 | ✅ | A malformed `lang` is rejected consistently with the quote endpoints | Unit test | `InputValidation.TryNormalizeLang`'s existing contract — same status code as `/quotes` returns for the same input |
| 22 | ✅ | `language`/`originalLanguage`/`isTranslated` are populated correctly | Unit test | Endpoint test, translated and fallback cases |
| 23 | ✅ | The dismiss endpoint resolves text the same way | Unit test | Endpoint test — it echoes the notification back |
| 24 | ❌ | Both surfaces render a resolved **body** | Live (T2) | `notifications-and-changelog/08`, plus the Notifications page and startup popup, UI switched to `nl` — screenshot, not text extraction. Scoped to `Body`: `Title` is rendered nowhere until #308, so it cannot be seen here. T2, not T1 — T1's whole job is confirming the app still starts (`docs/release-verification.md`) |
| 25 | ❌ | Migration applies cleanly to a database at the previous released schema | Live (T2) | `notifications-and-changelog/09`; ADR 009, plus `docs/automated-testing/notifications-and-changelog/03-upgrade-from-an-intermediate-schema-version.md` |
| 26 | ❌ | The application still starts with the new schema and read path in place | Live (T1) | Visual Studio run completes startup — T1's own scope |
| 27 | ✅ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 28 | ✅ | Full test suite green | Build | `dotnet test --configuration Release -m:1` — 3,661 passed, 0 failed across all 10 projects |
