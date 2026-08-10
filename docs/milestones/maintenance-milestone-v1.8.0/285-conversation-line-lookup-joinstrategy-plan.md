# #285 — Wrap Conversation's per-line lookups as IJoinStrategy implementations, fix redundant quote query

**Status:** Waiting for release
**GitHub issue:** #285
**Tiers required:** T1, T2
**Depends on:** ADR 017 (done), #287 (done)

---

## Spec requirements

1. `SqliteQuoteService.BuildLineResponse`'s three branches (quote, stage direction, sound cue)
   execute their SQL via `JoinQueryRepository<TResult>`/`IJoinStrategy<TResult>` instead of calling
   `connection.QueryFirstOrDefault` directly — per
   [ADR 017](../architecture-decisions/017-join-capable-reads-use-joinqueryrepository.md).
2. No SQL changes — reuse the existing 3 `Sql.cs` query strings as-is (`Quotes.SelectById()`,
   `StageDirections.SelectByIdWithTranslation`, `SoundCues.SelectByIdWithTranslation`).
3. The quote branch's redundant second query is eliminated as a natural consequence of becoming a
   single `JoinQueryRepository` call — not a separate fix, a structural side effect of requirement 1.
4. No behaviour change — `ConversationEndpointsTests`/`SqliteQuoteServiceTests`' existing conversation
   coverage (with and without `lang`) passes unmodified.

---

## Background — why this issue exists

Filed from #282's research. `BuildLineResponse`'s stage-direction/sound-cue branches already do
single-query, parameterized, `LEFT JOIN + COALESCE` translation resolution — proof that this shape
fits `IJoinStrategy` cleanly. The quote branch additionally has a confirmed redundant query: it calls
`Quotes.SelectById()` once with `lang = TranslationLang(lang, null)`, then — whenever the requested
language differs from the quote's original language — calls the identical SQL with identical
parameters again, discarding the first result.

**Verified before starting:**

- Traced `TranslationLang(string? lang, string? originalLanguage)` precisely: `if (lang is null)
  return null; if (originalLanguage is not null && lang == originalLanguage) return null; return
  lang;`. Called as `TranslationLang(lang, null)` (the first call's actual arguments,
  `originalLanguage` hardcoded to `null`), the middle branch can never fire — so this call always
  evaluates to `lang` unchanged. **The wrapper is a no-op at the first call site**; passing `lang`
  directly is exactly equivalent. This confirms the redundant-query fix is not just "becomes
  redundant once migrated" — the first query was already doing 100% of the necessary work before any
  migration.
- Confirmed via `grep`: `Sql.Quotes.SelectById()` is also called at two other sites in
  `SqliteQuoteService` (`GetById`-equivalent lookup, and inside `GetRandom`'s translation logic) —
  both **out of scope** for this issue, matching #282's own finding that Quote's `GetAll`/`GetRandom`
  hydration is inherently procedural (random exclusion-set loop), not a static join. Only
  `BuildLineResponse`'s usage is being migrated.
- Confirmed `TextEntityRow` (the shared row type for the stage-direction/sound-cue branches) is used
  **only** at those two call sites — no other consumer in the codebase. Since `IJoinStrategy<TResult>`/
  `JoinQueryRepository<TResult>` are keyed by `TResult` in DI, and the two branches need two
  *different* strategies (different SQL) mapping to what is today the *same* `TResult`, `TextEntityRow`
  must split into two distinct types — one per query — the same way #284 kept `SeriesReferenceRow`/
  `UniverseReferenceRow` as separate types despite an identical `(Id, Name)` shape.
- Confirmed `QuoteRow` (the quote branch's row type) is used broadly elsewhere in
  `SqliteQuoteService` (`GetAll`, `Search`, `GetRandom`) — it stays as the general row type; only a
  new `IJoinStrategy<QuoteRow>` wrapping `Quotes.SelectById()` is added alongside it. No conflict:
  those other call sites don't go through DI.
- Confirmed `BuildConversationResponse`/`BuildLineResponse` are currently `private static` methods
  taking `IDbConnection connection` as a parameter. Injecting `JoinQueryRepository<TResult>`
  dependencies requires instance-level fields, so both methods drop `static` — a contained change:
  they're called only from two existing instance methods within the same class
  (`GetRandom`/`GetConversation`), both already instance methods.

---

## Approach

**Split `TextEntityRow` into `StageDirectionLineRow` and `SoundCueLineRow`**, each in its own file in
`Quotinator.Core/Queries/` (matching #284's placement convention), with one `IJoinStrategy<TResult>`
each (`StageDirectionLineStrategy` wrapping `StageDirections.SelectByIdWithTranslation`,
`SoundCueLineStrategy` wrapping `SoundCues.SelectByIdWithTranslation`). The stale shared-row code
comment in `SqliteQuoteService.cs` is removed.

**Add `IJoinStrategy<QuoteRow>`** (`QuoteLineStrategy`, wrapping `Quotes.SelectById()`) — `QuoteRow`
itself stays in place (still used elsewhere), just gains a matching strategy for this one use.

**`SqliteQuoteService`'s primary constructor** gains three new parameters:
`JoinQueryRepository<QuoteRow> quoteLineRepository`, `JoinQueryRepository<StageDirectionLineRow>
stageDirectionLineRepository`, `JoinQueryRepository<SoundCueLineRow> soundCueLineRepository`.
`BuildConversationResponse`/`BuildLineResponse` drop `static` and become instance methods using these
fields instead of `connection.QueryFirstOrDefault(...)`.

**`BuildLineResponse`'s quote branch becomes a single call**, passing `lang` directly (per the
Background section's proof that `TranslationLang(lang, null)` was already a no-op):

```csharp
var rows = await quoteLineRepository.QueryAsync(new { id = lineRow.QuoteId, lang });
var effectiveRow = rows.Count > 0 ? rows[0] : null;
```

**DI registration** (`Program.cs`): 3 new `IJoinStrategy`/`JoinQueryRepository` pairs, alongside
where `SqliteQuoteService` itself is registered.

---

## Files touched

- `src/Quotinator.Core/Queries/` — 2 new POCO files (`StageDirectionLineRow`, `SoundCueLineRow`), 3
  new strategy files (`QuoteLineStrategy`, `StageDirectionLineStrategy`, `SoundCueLineStrategy`).
- `src/Quotinator.Core/Services/SqliteQuoteService.cs` — constructor, `BuildConversationResponse`,
  `BuildLineResponse`, removal of `TextEntityRow`.
- `src/Quotinator.Api/Program.cs` — 3 new DI registration pairs.
- No test changes expected — see "Expected tests" in the GitHub issue for why.

---

## Steps

### 1. Add the POCOs and strategy classes
**Status:** ✅ Done — landed in the same commit as #284's own migration groundwork
(`feat [#284]: migrate reference readers to JoinQueryRepository/IJoinStrategy` was a separate commit;
`QuoteLineStrategy`/`StageDirectionLineStrategy`/`SoundCueLineStrategy` and their POCOs
(`QuoteRow` promoted from `SqliteQuoteService`'s own private class; `StageDirectionLineRow`/
`SoundCueLineRow` split from the shared `TextEntityRow`, per this doc's Background) were added while
scaffolding #285's own constructor changes, ahead of discovering the async blocker).

### 2. Update SqliteQuoteService's constructor and the two Build* methods
**Status:** ✅ Done — **landed as part of #287's own commit**
(`feat [#287][#285]: convert IQuoteService and implementations to fully async`), not a separate #285
commit. #287's async conversion required rewriting `BuildLineResponse`/`BuildConversationResponse`
regardless; since the `JoinQueryRepository<T>` constructor fields were already scaffolded (added
before the async blocker was discovered), wiring them in directly was the natural implementation —
writing a throwaway async-Dapper-direct version first and replacing it in a separate #285 commit
would have been pure churn. The quote branch is now a single call, exactly as planned (see Approach).

### 3. Update Program.cs DI registrations
**Status:** ✅ Done — same commit as Step 2.

### 4. Verify
**Status:** ✅ Done — see #287's own plan doc for the full build/test/T1/T2 evidence (same commits,
same running app/container). Directly re-confirmed here: `ConversationEndpoints.cs`'s live `GET
/conversations/{id}` response (T2 pass) shows correctly resolved mixed stage-direction/quote lines,
with series/universe references embedded, and correct English fallback under `?lang=nl` where no
Dutch translation exists — proving all three `JoinQueryRepository`-backed branches work.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Quote/StageDirection/SoundCue line lookups execute via `JoinQueryRepository`, same public behaviour | Unit test | Existing `ConversationEndpointsTests`/`SqliteQuoteServiceConversationTests` pass unmodified (updated for #287's async signatures, no assertion changes) |
| 2 | ✅ | Quote branch issues a single query, not two | Code review | `BuildLineResponse`'s `"quote"` case is one `await _quoteLineRepository.QueryAsync(...)` call, no `TranslationLang`-gated second call |
| 3 | ✅ | No regression | Build + test | `dotnet build --configuration Release` — 0/0; `dotnet test --configuration Release` — 3299/3299 passed |
| 4 | ✅ | T1 — app starts in Visual Studio | Live (T1) | Developer confirmed (2026-08-10, same pass as #287): clean boot, schema/stats match seed exactly |
| 5 | ✅ | T2 — live container's Conversation endpoint still resolves quote/stage-direction/sound-cue lines correctly, with and without `lang` | Live (T2) | `GET /conversations/{id}` (Star Wars "I am your father" scene) correctly returns mixed stage-direction + 4 quote lines with series/universe embedded; `?lang=nl` correctly falls back to English (no Dutch translation in this fixture) |

---

## Notes

**Implementation landed across two commits, split differently than "Files touched" above
anticipated.** The POCOs/strategies (Step 1) landed with #284's own migration work; the actual
`SqliteQuoteService`/`Program.cs` wiring (Steps 2–3) landed inside #287's async-conversion commit,
since #287 needed to rewrite the exact same methods regardless and the constructor fields already
existed. No functional deviation from this doc's Approach — the code matches what was planned, just
committed under #287's own message rather than a separate #285 commit.
