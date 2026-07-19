# #210 — Canonicalize Quotes.Id at capture, case-insensitive lookup

**Status:** Planning
**GitHub issue:** #210
**Tiers required:** T1, T2
**Depends on:** none (parent tracking issue #207; shares `EntityIdCanonicalizer` with sibling sub-issue #209 but does not require it to land first)

---

## Spec requirements

1. Add the lowercase throwing/non-throwing forms (`CanonicalizeLowercase`/`TryCanonicalizeLowercase`) to
   `Quotinator.Data.Helpers.EntityIdCanonicalizer` — sibling sub-issue #209 adds the uppercase forms to
   the same class; whichever lands first creates the file with its own half.
2. Canonicalize a file-authored `SourceQuote.Id` to lowercase (matching `QuoteIdentity.StableId`'s own
   pinned, must-never-change lowercase convention) at the single earliest capture point in
   `ImportActionPlanner.PlanAsync`'s quote loop.
3. Audit and `UPPER()`-wrap every `Quotes.Id`/`QuoteId`-matching query that compares against a
   caller-or-file-supplied value, matching the same case-insensitive-by-default policy this project
   already applies to every other id column (CLAUDE.md's "GUID/enum/id comparisons are case-insensitive
   by default"). `Sql.Quotes.SelectById()` — the query behind `GET /api/v1/quotes/{id}` — currently has
   **no** case-insensitive wrapping at all, unlike Source/People's already-partially-mitigated
   equivalents; this is a live-user-facing gap, not just an internal-consistency one.
4. Audit every place a `Quotes.Id`-derived value is bound as a `Guid`-typed Dapper parameter (which
   `GuidHandler` force-uppercases) versus a plain `string` parameter (which doesn't) — a mismatch
   between the two for the *same* logical id is its own, independent source of inconsistency, separate
   from whatever casing the original file used.

---

## Background — why this issue exists

Found while planning sibling sub-issue #209 (Source/Person/StageDirection/SoundCue/Conversation
canonicalization — see #207, the parent tracking issue): a file-authored `Quotes.Id` has the identical
capture-time gap as Source/Person, confirmed by tracing the actual code, not assumed:

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
  `EntityIdentity.cs`'s own comment. But that pin only governs the *auto-generated* path; a
  file-authored `id` (the overwhelmingly common case — nearly every quote entry in this project's own
  bundled/curated data supplies one) is never checked or conformed against it. So `Quotes.Id` is not
  actually the consistent lowercase convention the `QuoteIdentity.StableId` comment implies — it is, in
  practice, "whatever casing each import happened to use," exactly Source/Person's bug, just targeting
  lowercase instead of uppercase, and with a weaker read-side mitigation (none) than Source/People have.
- A further, independent inconsistency was found alongside this (not fully resolved during planning,
  flagged for the implementation step to investigate and enumerate all instances of, not designed to a
  specific fix here): some call sites bind a quote id as a `Guid`-typed parameter (e.g.
  `QuoteSeedWriter.InsertGenresAsync(connection, resolved, Guid.Parse(resolved.Id), ...)` —
  `GuidHandler` force-uppercases this), while others (`Sql.Quotes.Insert`'s own `Id = resolved.Id`)
  bind the same logical value as a plain `string` (no forcing). Two call sites touching the *same*
  quote's id can therefore each apply a *different* casing transform to it, independent of whatever the
  source file originally said.

---

## Approach

### `EntityIdCanonicalizer` — lowercase forms

`Guid.ToString("D")` is already lowercase by .NET's own default, so the lowercase form needs no explicit
transform, mirroring exactly how `QuoteIdentity.StableId` itself produces its output today:

```csharp
public static class EntityIdCanonicalizer
{
    /// <exception cref="FormatException">rawId is not a valid Guid.</exception>
    public static string CanonicalizeLowercase(string rawId) => Guid.Parse(rawId).ToString("D");

    public static bool TryCanonicalizeLowercase(string rawId, out string? canonical)
    {
        if (Guid.TryParse(rawId, out var parsed)) { canonical = parsed.ToString("D"); return true; }
        canonical = null;
        return false;
    }
}
```

If sub-issue #209 has not landed yet, this sub-issue creates the file with only these two methods; #209
then adds its uppercase forms alongside them (or vice versa, whichever lands first).

### `PlanAsync`'s quote loop — capture-point fix (lowercase target)

Same shape as Source/Person's fix in #209, targeting `CanonicalizeLowercase` instead:

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

### Query audit — every `Quotes.Id`/`QuoteId`-matching site

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
the capture-point fix makes both sides consistently canonical, these need no wrapping.

---

## Steps

### 1. `EntityIdCanonicalizer` lowercase forms + tests

**Status:** Not started.

New/extended file `src/Quotinator.Data/Helpers/EntityIdCanonicalizer.cs`. New/extended test file
`tests/Quotinator.Data.Tests/Helpers/EntityIdCanonicalizerTests.cs`:
`CanonicalizeLowercase_UppercaseGuid_ReturnsLowercaseD`, `CanonicalizeLowercase_AlreadyCanonical_IsIdempotent`,
`TryCanonicalizeLowercase_ValidGuid_ReturnsTrueWithCanonicalForm`, `TryCanonicalizeLowercase_Malformed_ReturnsFalse`.

### 2. `PlanAsync`'s quote loop capture-point fix

**Status:** Not started. Per the Approach section's sketch — canonicalize `q.Id` to lowercase once per
iteration, before it's used anywhere.

### 3. `UPPER()`-wrap every `Quotes.Id`/`QuoteId` query

**Status:** Not started. Per the Approach section's query-audit table — apply `UPPER(...) = UPPER(...)`
to every row marked "Yes", confirming each against the live `Sql.cs` text at implementation time (the
table above is a starting inventory, not a guarantee no sibling query was missed by the grep pattern
used to build it). Update `SqlQueryGuardTests`' const inventory/coverage for each changed query, matching
this project's existing "every SQL change gets a guard-test update in the same commit" discipline.

### 4. Audit `Guid`-typed vs. `string`-typed quote-id parameter bindings

**Status:** Not started. Per the Background finding — find every call site that binds a quote id as a
`Guid` (forcing `GuidHandler`'s uppercase) versus every call site binding the same logical id as a
`string` (no forcing), and make them consistent. Given Step 2 makes the *stored* form canonically
lowercase, the resolution here is likely "stop binding quote ids as `Guid`-typed parameters at all"
(since `GuidHandler` would force uppercase, the opposite of the now-canonical lowercase) — confirmed at
implementation time once every call site is actually enumerated, not decided here.

### 5. Tests

**Status:** Not started.

| Test class | Test method |
|---|---|
| `Quotinator.Data.Tests.Helpers.EntityIdCanonicalizerTests` | (4 cases, Step 1) |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanAsync_UppercaseExplicitQuoteId_ResolvedIdIsCanonicalLowercase` |
| `Quotinator.Core.Tests.Services.SqliteImportActionServiceTests` | `GetById_UppercaseUrlIdAgainstLowercaseStoredQuote_StillResolves` (the specific, previously-unmitigated `GET /quotes/{id}` gap this issue closes — exercised at the service level, since this is the one case with no prior partial mitigation to build on) |

### 6. Verify

**Status:** Not started. `dotnet build --configuration Release` (0 warnings/errors), `dotnet test
--configuration Release --verbosity normal` (full suite green), T1, T2 (Docker smoke test — import a
quote with an uppercase explicit id and confirm `GET /api/v1/quotes/{that-id-in-any-casing}` resolves).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `EntityIdCanonicalizer`'s lowercase forms canonicalize, are idempotent, and reject malformed input via both a throwing and non-throwing form | Unit test | `EntityIdCanonicalizerTests` (4 cases) |
| 2 | ⬜ | A file-authored quote id canonicalizes to lowercase at capture | Unit test | `ImportActionPlannerTests.PlanAsync_UppercaseExplicitQuoteId_ResolvedIdIsCanonicalLowercase` |
| 3 | ⬜ | Every `Quotes.Id`/`QuoteId`-matching query is case-insensitive | Unit test | Query-audit table (Approach section) fully applied; `SqlQueryGuardTests` updated |
| 4 | ⬜ | `GET /quotes/{id}` resolves regardless of URL casing — the previously fully-unmitigated gap | Unit test | `SqliteImportActionServiceTests.GetById_UppercaseUrlIdAgainstLowercaseStoredQuote_StillResolves` |
| 5 | ⬜ | `Guid`-typed vs. `string`-typed quote-id parameter bindings are consistent | Doc review + code review | Step 4's audit findings recorded in this doc's Notes |
| 6 | ⬜ | No regression | Unit test | Full `dotnet test --configuration Release --verbosity normal` |
| 7 | ⬜ | T1 — app starts in Visual Studio | Live (T1) | Developer confirms |
| 8 | ⬜ | T2 — the case-insensitive lookup gap is fixed end to end | Live (T2) | Docker: import a quote with an uppercase explicit id and confirm `GET /api/v1/quotes/{id}` resolves regardless of URL casing |

---

## Notes

None yet — this is a planning-only pass; implementation has not started.
