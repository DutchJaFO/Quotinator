# ADR 016 — Class-naming suffixes and enum placement

**Status:** Accepted
**Date:** 2026-08-01
**GitHub issues:** #227, #264

---

## Context

Found while drafting ADR 015 (domain-prefixed table naming): deciding what a persistence class should
be called exposed that this project's C# type naming is inconsistent well beyond just entities — every
family of "class whose job is carrying data across one specific boundary" has the same drift, and
enums are scattered into whichever folder their surrounding feature happened to land in rather than
living together.

**Persistence classes** (`Quotinator.Core.Entities`/`Quotinator.Data.Entities`) — some carry `Entity`
(`QuoteEntity`, `ConversationEntity`, ...), most don't (`Character`, `Person`, `Source`, `ImportBatch`,
`SystemAuditEntry`, ...). Already covered by this ADR — see Decision below.

**HTTP response bodies** (`Quotinator.Core.Models`) — mostly `...Response` (`QuoteResponse`,
`CharacterResponse`, `ImportResultResponse`, 20+ others). Two genuinely top-level response types are
missing the suffix (`PagedResult<T>`, `FilteredQuoteResult<T>`), while one member-only type has it and
shouldn't (`SeedFilePreviewResponse`, a list element inside `SeedPreviewResponse`, not its own endpoint
body) — see Decision for why "is this a member of a larger type" is the deciding question, not "does
it look substantial."

**HTTP request bodies** — almost no consistency to drift from yet, since only one exists today
(`ConflictDecisionRequest`), but `Quotinator.Data.Import.ImportRequestSettingsDto` shows the confusion
this causes once a second one appears: it's named with **both** `Request` and `Dto` in the same name,
and extends a base class (`SourceImportSettingsDto`) that carries only `Dto`.

**Non-HTTP wire-format DTOs** (JSON file shapes — `manifest.json`, curated quote files, rule files,
deserialized via `JsonSerializer.Deserialize<T>` per CLAUDE.md's "JSON parsing policy") — some carry
`Dto` (`ManifestDto`, `ManifestFileEntryDto`, `ManifestGithubDto`, `ManifestPolicyDto`,
`SourceImportSettingsDto`), others don't, including the two classes CLAUDE.md's own JSON parsing policy
names as the *canonical example* of this exact pattern: `SourceQuote` and `ChangelogRoot`.

**Enums** are mixed directly into whichever folder their feature happened to add them to —
`Quotinator.Data.Entities` contains three enums (`CompletenessStatus`, `ImportBatchStatus`,
`ImportBatchType`) sitting alongside actual persistence classes, which directly contradicts this same
ADR's own definition of what belongs in `Entities` (a value type is not a table mapping). Others sit in
`Models` (`ConversationLineType`, `FilteredResultStatus`, `Genre`, `QuoteType`, `ChangeAction`,
`InitiatorType`) or `Import` (`DownloadTarget`, `DuplicateResolutionPolicy`, `FieldResolutionChoice`,
`SeedBatchOrigin`, `SeedFileIssue`, `SourceRefreshOutcome`, and more) — no single place to look to find
"every enum this project defines."

None of this was ever a deliberate decision — it accumulated the same way the `Entity`-suffix drift did:
each class named individually, by whichever session added it, with no written rule to check against.

---

## Decision

**Four class-naming suffixes, one per boundary a class crosses. A class carries exactly one, chosen by
which boundary it exists for — never zero, and never more than one.**

| Suffix | Applies to | Namespace |
|---|---|---|
| `Entity` | A direct persistence-layer mapping onto a database table | `*.Entities` |
| `Request` | The *top-level* body type of an HTTP endpoint's incoming request — never a member of that type | `*.Models` |
| `Response` | The *top-level* body type of an HTTP endpoint's outgoing response — never a member of that type | `*.Models` |
| `Dto` | A wire-format object for a boundary that is **not** HTTP and **not** a direct entity-column mapping: an on-disk JSON file shape, **or** a JSON blob serialized into and read back out of a database column — both via `JsonSerializer.Deserialize<T>` | `*.Import` or the owning feature's own namespace |

**`Entity`** — every class in `Quotinator.Data.Entities`/`Quotinator.Core.Entities` (or a future
consumer's equivalent namespace), unconditionally — the suffix is a property of *being a persistence
entity*, not of avoiding a specific collision, and applies regardless of which table-naming domain
(ADR 015) the underlying table uses. The base name is the table's own unprefixed singular name per
ADR 015. Applied retroactively to every existing entity class, not just new ones:
`Character` → `CharacterEntity`, `Person` → `PersonEntity`, `Source` → `SourceEntity`,
`SourceTranslation` → `SourceTranslationEntity`, `CharacterTranslation` → `CharacterTranslationEntity`,
`Series` → `SeriesEntity`, `Universe` → `UniverseEntity`, `ImportBatch` → `ImportBatchEntity`,
`SourceFileOverride` → `SourceFileOverrideEntity`, `SystemAuditEntry` → `AuditEntryEntity`,
`SystemImportAction` → `ImportActionEntity`. `SystemChangeLog`'s exact base name (its table is
`Audit_ChangeLogEntry` per ADR 015) is left to the implementation plan — "Entry" + "Entity" back to
back reads awkwardly, and the plan may choose a different base noun for the table itself rather than
force a stuttering class name; this ADR fixes the rule, not that one name.

**`Request`**/**`Response`** — only the class that is *directly* a minimal-API handler's request or
response body carries the suffix. A member of that class — a property, a list element type — is never
suffixed, no matter how substantial it is. If that same member type is later *also* needed as its own
endpoint's request or response, a new, separate wrapper type is created for that purpose rather than
suffixing the shared type itself:

```
OrderRequest             // top-level request body — has a member of type OrderRecord
{
    IReadOnlyList<OrderRecord> Records { get; init; }
}

OrderResponse            // top-level response body — same member type, still unsuffixed
{
    IReadOnlyList<OrderRecord> Records { get; init; }
}

OrderRecord               // plain member type — never suffixed on its own

// Only if OrderRecord *also* needs its own independent endpoint:
OrderRecordResponse : BaseResponse<OrderRecord>
OrderRecordRequest  : BaseRequest<OrderRecord>
```

**`Response` also applies to `T` when an endpoint's response body is a bare `IReadOnlyList<T>`/array
with no enclosing wrapper object.** An endpoint returning `IReadOnlyList<OrderRecord>` directly makes
`OrderRecord` itself `OrderRecordResponse` — the element *is* the top-level body in that case, so the
"never suffix a member" rule above does not apply to it.

**A class needed on both sides of a boundary split never carries one shared suffix.** If the two sides
have the same shape, a shared unsuffixed base class holds the common properties and each boundary gets
its own thin suffixed subclass adding nothing — `OrderRecordResponse : OrderRecord` and
`OrderRecordDto : OrderRecord`. If the two sides need to diverge (different fields, different
validation), they become two fully independent types instead; a base class is only ever a shortcut for
identical shapes, never a requirement.

**Prefer a generic base type (`BaseResponse<T>`/`BaseRequest<T>`) for the concrete wrapper**, per
DRY/SOLID — most `Response`/`Request` types need the same handful of standard features (e.g. paging
metadata, a data payload), and a shared generic base expresses that once instead of re-implementing it
on every concrete type. `PagedResult<T>` is exactly this kind of reusable wrapper already — it and its
sibling `Quotinator.Data.Models.PagedItems<T>` (kept separate for the reason CLAUDE.md documents) are
candidates to become (or inform) this project's actual `BaseResponse<T>`, with per-endpoint concrete
types (`QuotePagedResponse : PagedResponse<QuoteResponse>`, etc.) replacing today's pattern of
returning the generic type directly from an endpoint. The exact base-type design and which existing
generic wrapper becomes it are implementation-planning work — this ADR fixes the naming/placement rule
and the preferred pattern, not the final class hierarchy.

**Two concrete corrections this research found, both resolved here since they're single-name fixes:**
- `SeedFilePreviewResponse` is a *member* of `SeedPreviewResponse.Files`, never its own endpoint body —
  it's over-suffixed today and becomes `SeedFilePreview`.
- `ImportSummary`, `ImportConflictEntry`, `ImportRowError` (members of `ImportResultResponse`) and
  `BulkDecideRowError` (a member of `BulkDecideResponse`) are already correctly unsuffixed under this
  rule — no change needed, despite looking inconsistent next to their suffixed parent types.

**`Dto`** — every JSON-file-shape class carries it, including the two CLAUDE.md itself names as the
canonical example and currently doesn't follow its own rule: `SourceQuote` → `SourceQuoteDto` (or
similar), `ChangelogRoot` → `ChangelogRootDto`. Existing `Dto`-suffixed classes are already correct.

**The one active double-suffix bug this research found — `ImportRequestSettingsDto` — must resolve to
exactly one suffix.** It's deserialized from a JSON import-settings file (matching every other `Dto` in
`Quotinator.Data.Import`), not an HTTP request body, so it keeps `Dto` and drops `Request`:
`ImportSettingsDto`, matching its base class `SourceImportSettingsDto`'s own naming. Decided here since
it's a straightforward single-name fix, not left to the plan.

**Out of scope, unchanged:** exceptions keep .NET's own `Exception` suffix convention; static
utility/service classes (`FieldMergeResolver`, `ManifestSeedPlanner`, ...) aren't data-carrying types
and don't take any of these four suffixes; genuine domain value objects used throughout business logic
rather than only at a boundary (`ManifestPolicy`, `SeedBatch`, `SeedFile`, `ConflictResolutionRule`,
`SourceAliasRule`, `SafeValue<T>`, `MasterDataReference`) are not forced into this scheme either — the
four suffixes above answer "which boundary does this type exist to carry data across," not "is this
type Y a plain record."

### Enums live in their own folder, never mixed with classes

Every enum defined by a project moves into that project's own `Enums/` folder — namespace
`Quotinator.Core.Enums` / `Quotinator.Data.Enums` (or the equivalent in a future consumer project) —
regardless of which feature or subsystem it belongs to conceptually. This includes enums currently
sitting in `Entities/` (`CompletenessStatus`, `ImportBatchStatus`, `ImportBatchType` — the clearest
existing violation, since an enum is definitionally not a persistence-entity class), `Models/`
(`ConversationLineType`, `FilteredResultStatus`, `Genre`, `QuoteType`, `ChangeAction`,
`InitiatorType`), and `Import/` (`DownloadTarget`, `DuplicateResolutionPolicy`,
`FieldResolutionChoice`, `SeedBatchOrigin`, `SeedFileIssue`, `SourceRefreshOutcome`, and any other enum
found there during implementation). A `*JsonConverter` class that exists solely to (de)serialize one
specific enum (`QuoteTypeJsonConverter`, `ConversationLineTypeJsonConverter`,
`DuplicateResolutionPolicyJsonConverter`) moves alongside its enum into the same `Enums/` folder, since
it has no independent purpose without it.

---

## Consequences

**Every current entity class and file is renamed** as part of the same major refactor ADR 015 already
declared — `Character.cs` → `CharacterEntity.cs`, etc. Tracked in the same implementation plan.

**Every Response/Dto class missing its suffix is renamed, and every over-suffixed member type
(`SeedFilePreviewResponse` → `SeedFilePreview`) is corrected, in the same pass** — this is one
project-wide class-naming cleanup, not four separate efforts run at different times.

**Every enum (and its dedicated JSON converter, if any) moves into a new `Enums/` folder per project.**
This also cleans up the `Entities`/`Model`/`Import` folders to contain exactly what their own naming
rule says they should — a byproduct of this cleanup, not a separate goal.

**Future collisions become structurally impossible without per-case judgment**, the same reasoning as
the original `Entity`-only version of this ADR: a type's suffix says unambiguously which boundary it's
for, so two types "about the same concept" (an entity and its response DTO, a wire-format DTO and its
in-memory domain value) never collide by name.

**This should be recorded in `CLAUDE.md`/`docs/database-conventions.md` alongside ADR 015**, so the
full naming convention (table domain prefix, class suffix family, enum folder placement) is
discoverable together.

