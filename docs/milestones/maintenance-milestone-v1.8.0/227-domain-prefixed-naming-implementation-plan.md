# #227 — Import-table naming standardization: research and reference mapping

**Status:** Released
**GitHub issue:** #227
**Tiers required:** N/A (this issue itself is docs/decisions-only — see "Where the actual
implementation happens" below for the sub-issues that carry T1/T2)
**Depends on:** Nothing

---

## Background

The naming *decision* is made — [ADR 015](../architecture-decisions/015-domain-prefixed-table-naming.md)
(table domain prefixes) and [ADR 016](../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md)
(class suffixes, enum placement). This doc is the reference research behind implementing them: the
full table/class rename mapping and the migration-squash strategy, kept here as the one place that
research lives rather than re-derived independently by each sub-issue.

**#227 itself is fully decomposed, not implemented directly (2026-08-01, developer confirmed).** Its
own issue body bundled two unrelated things — naming standardization and a new
`FileResource`/`FileResourceLine` feature — and splitting the naming work further exposed that "table
rename" and "class rename" can't be separated from each other (a migration and its matching `[Table]`
attribute change must ship together), but *can* be split by project. #227's entire scope is now six
sub-issues:

- [#251](https://github.com/DutchJaFO/Quotinator/issues/251) — design + implement
  `FileResource`/`FileResourceLine` (granularity decision, schema, pruning/retention mechanism)
- [#252](https://github.com/DutchJaFO/Quotinator/issues/252) — confirm whether #153's
  `SourceFileOverride` registry should be superseded by #251, once #251 exists
- [#253](https://github.com/DutchJaFO/Quotinator/issues/253) — rename every `Quotinator.Data`-owned
  table/entity (migration, baseline, `Sql.cs`, `GetUserTables` pattern)
- [#254](https://github.com/DutchJaFO/Quotinator/issues/254) — rename every `Quotinator.Core`-owned
  table/entity (migration, baseline, `Sql.cs`)
- [#255](https://github.com/DutchJaFO/Quotinator/issues/255) — move every enum into a dedicated
  `Enums/` folder (independent, zero schema impact)
- [#256](https://github.com/DutchJaFO/Quotinator/issues/256) — fix `Response`/`Dto`/class-suffix
  violations (independent, zero schema impact)

The mapping and research below is authoritative for #253/#254 specifically — they should reference
this doc rather than re-deriving the table list.

---

## Migration-squash boundary (same technique as #155)

Checked against the last real tag, `v1.8.2` (2026-07-31) — only migrations added *after* that tag are
safe to rewrite/squash; anything at or before it is frozen forever.

| | At `v1.8.2` (frozen) | Now (unreleased) |
|---|---|---|
| `Quotinator.Data` (`DataOwnedMigrations`) | versions 1–2 | versions 3–4 added since (#150's CHECK constraints) |
| `Quotinator.Core` (`QuotinatorMigrations.All`) | versions 1–4 | version 5 added since (#150's CHECK constraint) |

**Plan:** squash each side's unreleased migration(s) *together with* the full rename DDL into one new
migration, replacing what's there now — not appended after it (#150's own migrations 3/4 and 5 have
never shipped, so per #155's precedent they can be rewritten, not just added to).

- `Quotinator.Data`: migrations 1–2 stay exactly as they are. New migration 3 = today's migrations 3+4
  (CHECK constraints) + every Data-owned table rename below. Net: 3 Data migrations total, same as
  today's 4 minus one.
- `Quotinator.Core`: migrations 1–4 stay exactly as they are. New migration 5 = today's migration 5
  (CHECK constraint) + every Core-owned table rename below. Net: 5 Consumer migrations total, unchanged
  count.

Both `DataBaselineSql` and `QuotinatorMigrations.Baseline` (the fresh-install path) must be updated to
the final names directly, in the same commit — per CLAUDE.md's baseline-drift rule, verified by the
existing schema-drift tests.

---

## Full rename mapping

### `Quotinator.Data`-owned (→ `Import_`/`Audit_`/`System_`)

| Table today | Table after | Class today | Class after | Notes |
|---|---|---|---|---|
| `System_AuditEntries` | `Audit_Entry` | `SystemAuditEntry` | `AuditEntryEntity` | |
| `System_ChangeLog` | `Audit_Change` | `SystemChangeLog` | `ChangeEntity` | Renamed from "ChangeLog"/"ChangeLogEntry" to avoid the `ChangeLogEntryEntity` stutter ADR 016 flagged — matches `System_AuditEntries`→`Audit_Entry`'s own singular-noun pattern (a row is "a change", not "a change log entry") |
| `System_ImportConflicts` | `Import_Conflict` | *(none formal today)* | `ImportConflictEntity` | New formal entity class — today read via ad hoc query models, not a `[Table]`-attributed class |
| `System_ImportActions` | `Import_Action` | `SystemImportAction` | `ImportActionEntity` | |
| `System_SourceFileOverrides` | `Import_SourceFileOverride` | `SourceFileOverride` | `SourceFileOverrideEntity` | |
| `ImportBatches` | `Import_Batch` | `ImportBatch` (already in `Quotinator.Data`) | `ImportBatchEntity` | **Reclassified**: table creation moves from Core's migration list into Data's; tracking moves from `System_ConsumerSchemaVersion` to `System_SchemaVersion` |
| `System_SchemaVersion` | *(unchanged)* | *(none)* | *(none)* | Residual/generic — stays `System_` |
| `System_ConsumerSchemaVersion` | *(unchanged)* | *(none)* | *(none)* | Residual/generic — stays `System_` |

### `Quotinator.Core`-owned (→ `Quotinator_`)

| Table today | Table after | Class today | Class after |
|---|---|---|---|
| `Quotes` | `Quotinator_Quote` | `QuoteEntity` | *(unchanged)* |
| `Sources` | `Quotinator_Source` | `Source` | `SourceEntity` |
| `Characters` | `Quotinator_Character` | `Character` | `CharacterEntity` |
| `People` | `Quotinator_Person` | `Person` | `PersonEntity` |
| `Series` | `Quotinator_Series` | `Series` | `SeriesEntity` |
| `Universe` | `Quotinator_Universe` | `Universe` | `UniverseEntity` |
| `Conversations` | `Quotinator_Conversation` | `ConversationEntity` | *(unchanged)* |
| `StageDirections` | `Quotinator_StageDirection` | `StageDirectionEntity` | *(unchanged)* |
| `SoundCues` | `Quotinator_SoundCue` | `SoundCueEntity` | *(unchanged)* |
| `ConversationLines` | `Quotinator_ConversationLine` | `ConversationLineEntity` | *(unchanged)* |
| `CharacterSources` | `Quotinator_CharacterSource` | `CharacterSourceEntity` | *(unchanged)* |
| `QuoteTranslations` | `Quotinator_QuoteTranslation` | `QuoteTranslationEntity` | *(unchanged)* |
| `SourceTranslations` | `Quotinator_SourceTranslation` | `SourceTranslation` | `SourceTranslationEntity` |
| `CharacterTranslations` | `Quotinator_CharacterTranslation` | `CharacterTranslation` | `CharacterTranslationEntity` |
| `StageDirectionTranslations` | `Quotinator_StageDirectionTranslation` | `StageDirectionTranslationEntity` | *(unchanged)* |
| `SoundCueTranslations` | `Quotinator_SoundCueTranslation` | `SoundCueTranslationEntity` | *(unchanged)* |
| `QuoteGenres` | `Quotinator_QuoteGenre` | `QuoteGenreEntity` | *(unchanged)* |

17 Core tables renamed, all 17 already had (or now gain) an `Entity`-suffixed class. 6 of the 8 Data
rows are renamed; the 2 residual version tables are not.

### Enum folder moves (no renames, only relocation — per ADR 016)

- `Quotinator.Data.Enums/`: `CompletenessStatus`, `ImportBatchStatus`, `ImportBatchType`,
  `ChangeAction`, `InitiatorType` (all currently in `Entities/` or `Models/`)
- `Quotinator.Core.Enums/`: `ConversationLineType` (+ `ConversationLineTypeJsonConverter`),
  `FilteredResultStatus`, `Genre`, `QuoteType` (+ `QuoteTypeJsonConverter`), `DownloadTarget`,
  `DuplicateResolutionPolicy` (+ `DuplicateResolutionPolicyJsonConverter`), `FieldResolutionChoice`,
  `SeedBatchOrigin`, `SeedFileIssue`, `SourceRefreshOutcome`

### Response/Dto fixes (per ADR 016, independent of the table rename but same cleanup pass)

- `SeedFilePreviewResponse` → `SeedFilePreview` (over-suffixed member type)
- `SourceQuote` → `SourceQuoteDto`, `SourceQuoteTranslation` → `SourceQuoteTranslationDto`
- `ChangelogRoot` → `ChangelogRootDto` — confirmed in scope (2026-08-01): `Quotinator.Changelog` is a
  separate project, but ADR 016's class-suffix rule is project-wide, and this is a one-line rename
- `ImportRequestSettingsDto` → `ImportSettingsDto` (drops the erroneous `Request`, decided in ADR 016)
- `PagedResult<T>`/`FilteredQuoteResult<T>` → deferred per ADR 016 (the `BaseResponse<T>` design isn't
  decided yet) — **not in this plan's scope**, tracked as a follow-on, not blocking the table rename

---

## Why the C# call-site surface is smaller than it looks

- **The generic repository layer needs no manual query changes.** `RepositorySql`/
  `EntityColumnMetadata` (`Quotinator.Data.Repositories`) read the table name reflectively from each
  entity's `[Table("...")]` attribute — updating the attribute value alone is sufficient; every
  `SqliteRestorableRepository<T>`/`SqliteRepositoryBase<T>` call site adapts automatically.
- **Hand-written SQL is the real editing surface.** `Quotinator.Core/Queries/Sql.cs` (886 lines)
  references the 17 Core table names ~321 times combined (`FROM`/`JOIN`/column-qualifier prefixes);
  `Quotinator.Data/Queries/Sql.cs` references the Data-owned table names in its own hand-written
  queries. This is mechanical (mostly literal find-replace, one table name at a time) but large — the
  single biggest chunk of this issue's actual work.
- **Every `REFERENCES TableName(Id)` clause in the schema DDL** (55 in `QuotinatorMigrations.cs` alone,
  spanning both frozen migrations — untouched — and the baseline/new migration — must be updated) needs
  the new table name. SQLite enforces these (`PRAGMA foreign_keys` is toggled on deliberately around
  Reset), so getting every one right matters, not just cosmetic.
- **Class renames are IDE-mechanical** (rename symbol, or careful project-wide find-replace + compiler
  to catch anything missed) — DI registrations, generic type arguments
  (`SqliteRestorableRepository<Character>` etc.), and every `new Character { ... }` call site all need
  the new name, but none of it is logic-bearing; the compiler finds every miss.

---

## Decisions resolved while planning #227 (2026-08-01, developer confirmed)

- **`Sql.SystemChangeLog`'s class rename**: `Audit_Change` / `ChangeEntity` (matches
  `Audit_Entry`/`AuditEntryEntity`'s own singular-noun pattern, avoids the `ChangeLogEntryEntity`
  stutter ADR 016 flagged) — reflected in the mapping table above and in #253.
- **`FileResource`/`FileResourceLine`** split out to #251/#252 rather than bundled into the rename.
- **`ChangelogRoot`** confirmed in scope for the `Dto` suffix fix (#256) despite living in a separate
  project — ADR 016's rule is project-wide.
- **Split by project, not by table-vs-class** — #253 (`Quotinator.Data`)/#254 (`Quotinator.Core`) each
  carry their own migration + baseline + entity renames + `Sql.cs` updates together, since a migration
  can't ship without its matching `[Table]` attribute change in the same deployment. #255 (enums) and
  #256 (Response/Dto) are genuinely independent — zero schema impact — so they're their own issues.

## Where the actual implementation happens

This doc is reference research only from here — each sub-issue gets its own plan doc when picked up,
using the mapping and migration-squash analysis above as the starting point rather than re-deriving it.
Full verification (build, test suite, `GetUserTables` pattern, T1/T2) is tracked per sub-issue, not
here — see each issue's own Definition of Done.
