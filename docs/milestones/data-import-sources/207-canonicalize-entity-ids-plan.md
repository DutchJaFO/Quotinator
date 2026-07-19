# #207 — Canonicalize file-authored explicit entity ids at capture

**Status:** Planning
**GitHub issue:** #207
**Tiers required:** T1, T2
**Depends on:** none (ADR 012 already committed; built against current `Quotinator.Core`/`Quotinator.Data`)

---

## Spec requirements

**Part A — Source/Person/Character (the original masterdata-404 finding):**

1. Add `Quotinator.Data.Helpers.EntityIdCanonicalizer` — a single reusable helper that turns a raw
   externally-supplied id string into this project's canonical form, and that a malformed input can be
   detected against without throwing (a capture site needs to fall back gracefully, not crash an entire
   import over one bad id). Must support both canonical directions (see Part B) — Source/Person/Character
   target uppercase; Quote targets lowercase.
2. Canonicalize `SourceEntry.Id`/`PersonEntry.Id` through it at the single earliest point each is
   captured in `ImportActionPlanner` — both the Add path and the correction-match path (which today
   uses the file's own casing for `sourceIndex`/`personIndex`, not the matched row's actual stored id).
3. Confirm whether Character shares the same gap.
4. Audit every `Ensure*ExistsAsync` helper and every `Sql.*.Insert`/`Update` binding an entity id as a
   raw `string` for the same gap, to confirm nothing downstream of (2) can still introduce non-canonical
   casing.
5. Verify against the Quote→Source join specifically — not only the masterdata `GetById` endpoint that
   originally surfaced this.
6. Build the cross-entity regression guard ADR 012 requires, proving the invariant holds for every
   explicit-id-capable entity, not just Source.

**Part B — Quotes.Id (scope expansion, found while planning Part A):**

7. Canonicalize a file-authored `SourceQuote.Id` to lowercase (matching `QuoteIdentity.StableId`'s own
   pinned, must-never-change lowercase convention) at the single earliest capture point in
   `ImportActionPlanner.PlanAsync`'s quote loop — mirroring Part A's pattern exactly, just targeting the
   opposite canonical case.
8. Audit and `UPPER()`-wrap every `Quotes.Id`/`QuoteId`-matching query that compares against a
   caller-or-file-supplied value, matching the same case-insensitive-by-default policy this project
   already applies to every other id column (CLAUDE.md's "GUID/enum/id comparisons are case-insensitive
   by default"). `Sql.Quotes.SelectById()` — the query behind `GET /api/v1/quotes/{id}` — currently has
   **no** case-insensitive wrapping at all, unlike Source/People's already-partially-mitigated
   equivalents; this is a live-user-facing gap, not just an internal-consistency one.
9. Audit every place a `Quotes.Id`-derived value is bound as a `Guid`-typed Dapper parameter (which
   `GuidHandler` force-uppercases) versus a plain `string` parameter (which doesn't) — a mismatch between
   the two for the *same* logical id is its own, independent source of inconsistency, separate from
   whatever casing the original file used.

---

## Background — why this issue exists

See ADR 012 (`docs/architecture-decisions/012-canonicalize-entity-ids-at-capture.md`) for the full
incident writeup. In short: `Sources.Id`, `Quotes.SourceId`, and `CharacterSources.SourceId` are all
written from the same in-memory `ImportActionPlanner.sourceIndex` value, bound as plain `string` Dapper
parameters (never `Guid`-typed, so `GuidHandler`'s uppercase normalization never runs). A file-authored
lowercase explicit id therefore reaches storage exactly as typed. This is accidentally self-consistent
(the Quote→Source join still matches, since both sides carry the same wrong casing) until a `Guid`-typed
lookup — which `GuidHandler` force-uppercases — silently fails to find the non-canonical row. That's how
`GET /api/v1/masterdata/sources/{id}` was found 404ing for a Source that resolved correctly via
`GET /api/v1/quotes/{id}`.

**Verified before starting** (per this project's standing rule):

- **Confirmed as claimed**: `ImportActionPlanner.PlanSourcesAsync`'s correction-match branch sets
  `sourceIndex[$"{s.Title}|{typeStr}"] = matchedId;` where `matchedId = s.Id!` (`ImportActionPlanner.cs`
  line 354/366) — the file's own casing, not a canonicalized form. The Add-path fallback,
  `var addId = s.Id ?? EntityIdentity.SourceId(s.Title, typeStr);` (line 517), has the identical
  exposure. `PlanPeopleAsync` mirrors this exactly at lines 570 (`personIndex[p.Name] = p.Id;`, matched
  branch) and 638 (same, Add branch) — `PersonEntry.Id` is `required` (never optional), so every Person
  Add or correction goes through this path.
- **Confirmed as claimed**: `Sql.Sources.SelectExistingById`/`Sql.People.SelectExistingById` (and their
  sibling `UpdateFieldsById`/`SelectCompletenessById`/`UpdateCompletenessById`/`CountActiveReferences`
  queries) are already `UPPER(Id) = UPPER(@id)`-wrapped (#180's own fix) — so the *existence lookup*
  already correctly finds a row regardless of the file's casing. The bug is specifically that `matchedId`
  then uses the file's raw casing instead of asking the row what its own actual id is, poisoning
  `sourceIndex`/`personIndex` for any same-batch quote that resolves against it.
- **Confirmed as claimed**: `Sql.Quotes.Insert` (`SqliteImportActionService.cs` line 804-814) binds
  `SourceId = payload.SourceId` as a plain string, sourced from `QuoteActionPayload.SourceId`, which
  traces back to `ResolveSourceAsync`'s return value — itself either `sourceIndex`'s value (if resolved
  same-batch) or a direct DB-read existing id (already canonical, since it comes from a `SELECT`, not a
  file). This confirms the join-consistency mechanism described in ADR 012 is real, not theoretical.
- **New finding, resolves Spec requirement 3 — Character does NOT share this gap, verified not assumed**:
  `EntityIdentity.CharacterId(sourceId, name)` calls the shared `StableId` helper
  (`Quotinator.Core.Import.EntityIdentity.cs`), which in turn calls `QuoteIdentity.Normalise` on every
  input piece before hashing — and `Normalise` is `s.Trim().ToLowerInvariant()` with whitespace
  collapsed (`QuoteIdentity.cs` line 17-18). Because the hash input is *always* lowercased before
  `SHA256.HashData` runs, a Character's derived stable id is **invariant to the casing of the `sourceId`
  string passed in** — whether `sourceId` was `"F0000190-..."` or `"f0000190-..."` makes no difference to
  the resulting hash, hence no difference to the Character's own id. Character also has no
  file-authored explicit id path at all (no `characters[]` section exists in the schema) — the only id
  computation is `EntityIdentity.CharacterId`, which calls `EntityIdentity`'s own private `StableId`
  helper, whose final line is `new Guid(hash[..16]).ToString("D").ToUpperInvariant()` — always
  canonical-uppercase by construction. (Not to be confused with the separate `QuoteIdentity.StableId`,
  used only for Quote ids, whose own final `.ToString()` has no `ToUpperInvariant()` — irrelevant here
  since Characters never go through it, but worth noting precisely rather than eliding the distinction.)
  No fix needed for Character; this finding is the answer to Spec requirement 3, not a placeholder for
  later work.
- **Confirming precedent found in existing code, not previously cited in ADR 012**:
  `EntityIdentity.StableId`'s own doc comment (`EntityIdentity.cs` lines 36-42) already states the exact
  mechanism ADR 012 formalizes, for the auto-derived-id case specifically: "Uppercased — unlike
  `QuoteIdentity.StableId` (whose lowercase output is pinned by a production-data regression test and
  must never change)... matching that convention here is what lets a later lookup's `Guid`-typed
  round-trip compare equal to what was actually written, since SQLite's default TEXT comparison is
  case-sensitive." This confirms the codebase's own prior authors already reasoned through this exact
  invariant for the id-generation path — #207 closes the same gap for the id-*capture* path (a
  file-authored id, not a generated one), which this comment doesn't cover. `QuoteIdentity.StableId`'s
  own deliberately-pinned lowercase convention is a separate, already-settled, out-of-scope design
  decision for Quote ids specifically — not touched by this issue.
- **Correction to ADR 012's own sketch, found while designing the guard test**: the ADR describes the
  cross-entity guard as `[DynamicData]`-driven "asserting the full, real invariant end to end" through
  the import pipeline for all five entities. Attempting this concretely, the five entity DTOs
  (`SourceEntry`/`PersonEntry`/`SourceStageDirection`/`SourceSoundCue`/`SourceConversation`) have no
  common shape a single generic pipeline-level test method can drive without heavy indirection (delegates
  captured across `[DynamicData]`'s static-method boundary, which runs before per-test instance state like
  `_dbPath` exists). Redesigned as a **storage-layer** invariant guard instead — canonicalize a lowercase
  id, insert it directly into each of the five tables via minimal raw SQL, then assert a `Guid`-typed
  `SELECT` finds it — genuinely `[DynamicData]`-driven, one method, still proves the real invariant
  (`GuidHandler`-typed lookups find canonically-written rows), just below the full pipeline rather than
  through it. The full pipeline is still exercised, but by *separate*, entity-specific tests (Source and
  Person specifically, since those are the two whose planner logic actually changes) — see Step 6.
- **Scope expansion, found while investigating the "StableId" naming inconsistency directly** — a
  file-authored `Quotes.Id` has the *identical* capture-time gap as Source/Person, confirmed by tracing
  the actual code, not assumed:
  - `ImportActionPlanner.PlanAsync`'s quote loop uses `q.Id` directly and unconditionally
    (`seenQuotes[q.Id] = q;` line 87, `EntityId = q.Id` at lines 102/144/164) — never canonicalized.
  - `SqliteQuoteService.GetById(string id, ...)` (line 50) binds `id` as a **plain `string`** parameter
    into `Sql.Quotes.SelectById()`, whose WHERE clause is `q.Id = @id` with **no `UPPER()` wrapping at
    all** (`Sql.cs` line 92-93) — confirmed by direct read, not inferred from the Source/People pattern.
    This means `GET /api/v1/quotes/{id}` today only succeeds if the URL's casing happens to exactly
    match however that specific quote's id was originally typed in its source file — worse than
    Source/People's situation, which at least has partial `UPPER()` read-side tolerance.
  - `QuoteIdentity.StableId` (the auto-generated fallback, used only when a quote entry omits its own
    `id`) is deliberately, permanently lowercase — pinned by "a production-data regression test" per
    `EntityIdentity.cs`'s own comment (see the prior finding above). But that pin only governs the
    *auto-generated* path; a file-authored `id` (the overwhelmingly common case — nearly every quote
    entry in this project's own bundled/curated data supplies one) is never checked or conformed against
    it. So `Quotes.Id` is not actually the consistent lowercase convention the `QuoteIdentity.StableId`
    comment implies — it is, in practice, "whatever casing each import happened to use," exactly
    Source/Person's bug, just targeting lowercase instead of uppercase, and with a weaker read-side
    mitigation (none) than Source/People have.
  - A further, independent inconsistency (not fully resolved during planning, flagged for the
    implementation step to investigate and enumerate all instances of, not designed to a specific fix
    here): some call sites bind a quote id as a `Guid`-typed parameter (e.g.
    `QuoteSeedWriter.InsertGenresAsync(connection, resolved, Guid.Parse(resolved.Id), ...)` —
    `GuidHandler` force-uppercases this), while others (`Sql.Quotes.Insert`'s own `Id = resolved.Id`)
    bind the same logical value as a plain `string` (no forcing). Two call sites touching the *same*
    quote's id can therefore each apply a *different* casing transform to it, independent of whatever the
    source file originally said — a second, distinct source of inconsistency layered on top of the
    file-authored-casing one. Folded into Part B per explicit developer decision (not filed separately)
    given its direct relationship to the same root cause and mechanism.

---

## Approach

### `EntityIdCanonicalizer` (`Quotinator.Data.Helpers`, alongside `GuidHandler`/`SafeValue<T>`)

Two target casings are needed — Source/Person/Character canonicalize to uppercase (matching
`EntityIdentity.StableId`'s own convention); Quote canonicalizes to lowercase (matching
`QuoteIdentity.StableId`'s pinned convention, which cannot change — see Background). `Guid.ToString("D")`
is already lowercase by .NET's own default, so the lowercase form needs no explicit transform, mirroring
exactly how `QuoteIdentity.StableId` itself produces its output today:

```csharp
public static class EntityIdCanonicalizer
{
    /// <exception cref="FormatException">rawId is not a valid Guid.</exception>
    public static string CanonicalizeUppercase(string rawId) => Guid.Parse(rawId).ToString("D").ToUpperInvariant();

    /// <exception cref="FormatException">rawId is not a valid Guid.</exception>
    public static string CanonicalizeLowercase(string rawId) => Guid.Parse(rawId).ToString("D");

    public static bool TryCanonicalizeUppercase(string rawId, out string? canonical)
    {
        if (Guid.TryParse(rawId, out var parsed)) { canonical = parsed.ToString("D").ToUpperInvariant(); return true; }
        canonical = null;
        return false;
    }

    public static bool TryCanonicalizeLowercase(string rawId, out string? canonical)
    {
        if (Guid.TryParse(rawId, out var parsed)) { canonical = parsed.ToString("D"); return true; }
        canonical = null;
        return false;
    }
}
```

The non-throwing `Try*` forms are new relative to ADR 012's original one-method sketch — needed because
`ImportActionPlanner` must not let one malformed id throw and abort an entire batch's planning; see the
next section for exactly how the fallback is used. Two casing directions rather than a single
parameterized method (`Canonicalize(rawId, uppercase: true)`) is a deliberate readability choice — a call
site's own name states its intent (`CanonicalizeLowercase` for a quote id) rather than a boolean flag a
reader has to trace back to a constant.

### Capture-point fix: canonicalize once per entry, use everywhere

**`PlanSourcesAsync`** — `SourceEntry.Id` is `string?` (optional; enrichment-shaped entries omit it).
Canonicalize once at the top of the loop body:

```csharp
var canonicalId = s.Id is { } rawId && EntityIdCanonicalizer.TryCanonicalizeUppercase(rawId, out var canonical)
    ? canonical
    : s.Id; // malformed or absent: pass through unchanged — not this issue's job to add new validation
```

Every later reference to `s.Id`/`explicitId` for lookup, `matchedId`, and `addId` uses `canonicalId`
instead:

```csharp
var existing = canonicalId is { } explicitId
    ? await connection.QuerySingleOrDefaultAsync<...>(Sql.Sources.SelectExistingById, new { id = explicitId }, transaction)
    : null;
...
var matchedId = canonicalId!;
...
var addId = canonicalId ?? EntityIdentity.SourceId(s.Title, typeStr);
```

A well-formed lowercase id is now canonical everywhere it's used in this iteration — the lookup (already
tolerant, unaffected), `sourceIndex`, and the staged `EntityId`. A malformed id behaves exactly as it
does today (passes through unchanged, `SelectExistingById` finds nothing, natural-key/Add path takes
over) — deliberately unchanged, since general id-format validation is out of this issue's scope.

**`PlanPeopleAsync`** — `PersonEntry.Id` is `required string`, so the same pattern applies without the
`s.Id is { }` null-check:

```csharp
var canonicalId = EntityIdCanonicalizer.TryCanonicalizeUppercase(p.Id, out var canonical) ? canonical! : p.Id;
```

used everywhere `p.Id` currently appears (`SelectExistingById`'s parameter, `personIndex[p.Name] =`,
every staged `EntityId =`).

### Part B: `PlanAsync`'s quote loop — capture-point fix (lowercase target)

Same shape as Source/Person, targeting `CanonicalizeLowercase` instead:

```csharp
foreach (var q in quotes)
{
    var canonicalQuoteId = EntityIdCanonicalizer.TryCanonicalizeLowercase(q.Id, out var canonical) ? canonical! : q.Id;
    // every later reference to q.Id in this iteration (seenQuotes[q.Id], EntityId = q.Id, resolved.Id,
    // and the SourceQuote instances threaded through QuoteFieldMerge) uses canonicalQuoteId instead.
```

`SourceQuote` is a `record`/init-only type — the exact mechanics of substituting `canonicalQuoteId` back
onto `q` (a `with` expression producing a corrected copy vs. threading the canonical string alongside `q`
through the rest of the loop) are an implementation-time detail, not a design question; whichever reads
more clearly once the surrounding loop body is in front of the implementer.

### Part B: query audit — every `Quotes.Id`/`QuoteId`-matching site

Full inventory from a direct grep of `Sql.cs` (not exhaustive design of each fix here — the exact wrap
is mechanical once the inventory is confirmed correct; recorded so the implementation step has a checked
starting list rather than re-deriving it):

| Location | Current | Needs `UPPER()`? |
|---|---|---|
| `Quotes.SelectById()` (~line 92) | `WHERE q.Id = @id` | **Yes** — the live, user-facing `GET /quotes/{id}` gap |
| `Quotes.SelectRawById()` (~line 118) | `WHERE q.Id = @id` | Yes — internal merge/conflict-resolution use, same defense-in-depth precedent as Sources/People's internally-used-but-still-wrapped siblings |
| `Quotes.SelectCompletenessById`/`UpdateCompletenessById` (lines 33, 37) | `WHERE Id = @id` | Yes, matching Sources/People's already-wrapped siblings |
| `Quotes.UpdateOnNewestWins` (lines 43-45) | `WHERE Id=@id` | Yes |
| `QuoteGenres.DeleteForQuote`/`LoadForQuote`/the two Insert variants (lines 157-171) | `WHERE QuoteId = @id` / `@QuoteId` | Yes |
| `QuoteTranslations.DeleteForQuote`/Insert (lines 178-183) | `WHERE QuoteId = @id` | Yes |
| `ConversationLines`'s two quote-referencing queries (lines 536, 551, 555) | `WHERE cl.QuoteId = @quoteId` etc. | Yes |

Queries already confirmed correctly wrapped and needing no change: `Characters.CountActiveReferences`-
style siblings that reference `PersonId`/`SourceId` (lines 296, 363) — already `UPPER()`-wrapped from
prior work; not `QuoteId`-related, listed only to confirm they were checked, not missed.

`SelectBase`'s own internal JOINs (`Quotes` → `Sources`/`Characters`/`People`/translations/`Series`/
`Universe`) are FK joins between two internally-computed values, not a caller-supplied comparison — once
Part A's capture-point fix makes both sides consistently canonical, these need no wrapping, matching the
same "fix at capture, joins inherit correctness for free" reasoning as Part A.

### `SqliteImportActionService.cs` — no change needed for Part A

Because canonicalization happens once, upstream, at the point `ImportActionPlanner` first captures the
id, `action.EntityId`/`payload.SourceId`/every `Ensure*ExistsAsync` call site downstream already receives
the canonical value with zero code changes on their part — this is the entire point of ADR 012's
"single earliest point of capture" principle. Step 4 (audit) is therefore primarily verification, not
new code: confirm no *other* capture point exists that bypasses `ImportActionPlanner` (grepped — none
found; masterdata endpoints are read-only, `DecideAsync`'s conflict-resolution path only resolves
*ambiguous fields* on an already-staged action, never introduces a new id).

---

## Steps

### 1. `EntityIdCanonicalizer` + its tests

**Status:** Not started.

New file `src/Quotinator.Data/Helpers/EntityIdCanonicalizer.cs`. New test file
`tests/Quotinator.Data.Tests/Helpers/EntityIdCanonicalizerTests.cs`:
`CanonicalizeUppercase_LowercaseGuid_ReturnsUppercaseD`, `CanonicalizeUppercase_AlreadyCanonical_IsIdempotent`,
`CanonicalizeUppercase_Malformed_Throws`, `TryCanonicalizeUppercase_ValidGuid_ReturnsTrueWithCanonicalForm`,
`TryCanonicalizeUppercase_Malformed_ReturnsFalse` — plus the `*Lowercase` mirror cases listed under Step 9
(Part B needs the same coverage for the opposite casing direction).

### 2. `PlanSourcesAsync` capture-point fix

**Status:** Not started. Per the Approach section above — one `canonicalId` computed at the top of the
loop, used at every `s.Id`/`explicitId`/`matchedId`/`addId` reference.

### 3. `PlanPeopleAsync` capture-point fix

**Status:** Not started. Same pattern, adjusted for `PersonEntry.Id` being required rather than optional.

### 4. Character — no fix, finding documented

**Status:** Done during planning (see Background's verified finding) — `EntityIdentity.CharacterId`'s
hash input is always lowercased before hashing, so its output is casing-invariant regardless of
`sourceId`'s casing. No code change.

### 5. Audit `Ensure*ExistsAsync`/`Sql.*.Insert`/`Update` sites

**Status:** Not started. Confirm (by inspection, not by changing code) that every remaining raw-`string`
id parameter downstream of Steps 2-3 now receives an already-canonical value — `EnsureSourceExistsAsync`,
`EnsurePersonExistsAsync`, `EnsureCharacterExistsAsync`. Record the grep evidence in this doc's Notes once
done.

### 6. Part B — `PlanAsync`'s quote loop capture-point fix

**Status:** Not started. Per the Approach section's "Part B: `PlanAsync`'s quote loop" sketch —
canonicalize `q.Id` to lowercase once per iteration, before it's used anywhere.

### 7. Part B — `UPPER()`-wrap every `Quotes.Id`/`QuoteId` query

**Status:** Not started. Per the Approach section's query-audit table — apply `UPPER(...) = UPPER(...)`
to every row marked "Yes", confirming each against the live `Sql.cs` text at implementation time (the
table above is a starting inventory, not a guarantee no sibling query was missed by the grep pattern
used to build it). Update `SqlQueryGuardTests`' const inventory/coverage for each changed query, matching
this project's existing "every SQL change gets a guard-test update in the same commit" discipline.

### 8. Part B — audit `Guid`-typed vs. `string`-typed quote-id parameter bindings

**Status:** Not started. Per the Background finding — find every call site that binds a quote id as a
`Guid` (forcing `GuidHandler`'s uppercase) versus every call site binding the same logical id as a
`string` (no forcing), and make them consistent. Given Step 6 makes the *stored* form canonically
lowercase, the resolution here is likely "stop binding quote ids as `Guid`-typed parameters at all" (since
`GuidHandler` would force uppercase, the opposite of the now-canonical lowercase) — confirmed at
implementation time once every call site is actually enumerated, not decided here.

### 9. Tests

**Status:** Not started.

| Test class | Test method |
|---|---|
| `Quotinator.Data.Tests.Helpers.EntityIdCanonicalizerTests` | (5 cases, Step 1) |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanSourcesAsync_LowercaseExplicitId_AddPath_ResolvedIdIsCanonicalUppercase` |
| " | `PlanSourcesAsync_LowercaseExplicitId_CorrectionMatch_IndexedIdIsCanonicalUppercase` |
| " | `PlanSourcesAsync_QuoteReferencesLowercaseExplicitSource_ResolvedSourceIdIsCanonical` (the join-safety case, at planner level) |
| " | `PlanPeopleAsync_LowercaseExplicitId_ResolvedIdIsCanonicalUppercase` |
| `Quotinator.Core.Tests.Services.SqliteImportActionServiceTests` | `[DynamicData]`-driven, one method covering Sources/People/StageDirections/SoundCues/Conversations: canonicalize a lowercase id, insert directly, assert a `Guid`-typed `SELECT` finds it (the storage-layer guard — see Background's correction to ADR 012's original sketch) |
| " | `ApplyBatchAsync_LowercaseExplicitSourceId_QuoteJoinStillResolves` (full pipeline: import Source + same-batch Quote with lowercase explicit Source id, apply, read the quote back via the real join query, confirm source title/date resolve) |
| " | `ApplyBatchAsync_LowercaseExplicitSourceId_MasterdataRepositoryLookupResolves` (apply, then `SqliteRepository<Source>.GetByIdAsync(Guid)` — the exact call the masterdata endpoint makes — finds the row) |
| `Quotinator.Data.Tests.Helpers.EntityIdCanonicalizerTests` | `CanonicalizeLowercase_UppercaseGuid_ReturnsLowercaseD`, `CanonicalizeLowercase_AlreadyCanonical_IsIdempotent`, `TryCanonicalizeLowercase_Malformed_ReturnsFalse` (Part B's lowercase forms, alongside Step 1's uppercase ones) |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanAsync_UppercaseExplicitQuoteId_ResolvedIdIsCanonicalLowercase` (Part B's capture-point fix, mirroring Part A's Source/Person tests but with the opposite input/output casing) |
| `Quotinator.Core.Tests.Services.SqliteImportActionServiceTests` | `GetById_UppercaseUrlIdAgainstLowercaseStoredQuote_StillResolves` (the specific, previously-unmitigated `GET /quotes/{id}` gap Part B closes — exercised at the service level, since this is the one case with no prior partial mitigation to build on) |

### 10. Verify

**Status:** Not started. `dotnet build --configuration Release` (0 warnings/errors), `dotnet test
--configuration Release --verbosity normal` (full suite green), T1, T2 (Docker smoke test — reproduce
the original live finding: import a Source with a lowercase explicit id, confirm
`GET /api/v1/masterdata/sources/{id}` now returns 200, and confirm `GET /api/v1/quotes/{id}` for a
same-batch quote referencing it still resolves the source title/date correctly; additionally, import a
quote with an uppercase explicit id and confirm `GET /api/v1/quotes/{that-id-in-any-casing}` resolves).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `EntityIdCanonicalizer` canonicalizes, is idempotent, and rejects malformed input via both a throwing and non-throwing form | Unit test | `EntityIdCanonicalizerTests` (5 cases) |
| 2 | ⬜ | A lowercase explicit Source id (Add or correction-match) resolves to canonical uppercase everywhere it's used in the same batch | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_LowercaseExplicitId_*` (2 cases) |
| 3 | ⬜ | A same-batch quote referencing a lowercase-id'd Source resolves to the canonical id | Unit test | `PlanSourcesAsync_QuoteReferencesLowercaseExplicitSource_ResolvedSourceIdIsCanonical` |
| 4 | ⬜ | A lowercase explicit Person id resolves to canonical uppercase | Unit test | `PlanPeopleAsync_LowercaseExplicitId_ResolvedIdIsCanonicalUppercase` |
| 5 | ⬜ | Character ids are unaffected by Source-id casing (documented finding, not a fix) | Doc review | This plan doc's Background section |
| 6 | ⬜ | Every explicit-id-capable table's rows are findable via a `Guid`-typed lookup once canonicalized | Unit test | `SqliteImportActionServiceTests`' `[DynamicData]` storage guard (5 cases) |
| 7 | ⬜ | The Quote→Source join survives a lowercase explicit Source id through a full plan→apply cycle | Unit test | `ApplyBatchAsync_LowercaseExplicitSourceId_QuoteJoinStillResolves` |
| 8 | ⬜ | The masterdata Sources repository lookup (the exact query that originally 404'd) resolves | Unit test | `ApplyBatchAsync_LowercaseExplicitSourceId_MasterdataRepositoryLookupResolves` |
| 9 | ⬜ | A file-authored quote id canonicalizes to lowercase at capture | Unit test | `ImportActionPlannerTests.PlanAsync_UppercaseExplicitQuoteId_ResolvedIdIsCanonicalLowercase` |
| 10 | ⬜ | Every `Quotes.Id`/`QuoteId`-matching query is case-insensitive | Unit test | Query-audit table (Approach section) fully applied; `SqlQueryGuardTests` updated |
| 11 | ⬜ | `GET /quotes/{id}` resolves regardless of URL casing — the previously fully-unmitigated gap | Unit test | `SqliteImportActionServiceTests.GetById_UppercaseUrlIdAgainstLowercaseStoredQuote_StillResolves` |
| 12 | ⬜ | `Guid`-typed vs. `string`-typed quote-id parameter bindings are consistent | Doc review + code review | Step 8's audit findings recorded in this doc's Notes |
| 13 | ⬜ | No regression | Unit test | Full `dotnet test --configuration Release --verbosity normal` |
| 14 | ⬜ | T1 — app starts in Visual Studio | Live (T1) | Developer confirms |
| 15 | ⬜ | T2 — the original live symptom is fixed end to end | Live (T2) | Docker: import a lowercase-explicit-id Source, `GET /api/v1/masterdata/sources/{id}` returns 200; a same-batch quote's `GET /api/v1/quotes/{id}` still resolves the source correctly; import a quote with an uppercase explicit id and confirm `GET /api/v1/quotes/{id}` resolves regardless of URL casing |

---

## Notes

None yet — this is a planning-only pass; implementation has not started.
