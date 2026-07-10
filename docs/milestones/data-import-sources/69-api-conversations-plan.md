# #69 — API: conversation membership, GET /conversations/{id}, random dedup

**Status:** Planning
**GitHub issue:** #69
**Tiers required:** T1, T2
**Depends on:** #67, #68

---

## Scope changes

Cross-checked against current code before planning (per `process.md`'s mandatory pre-implementation
check). The issue as filed on 2026-06-16 proposes replacing `/random`'s `?n=` response with
`{ requestedCount, returnedCount, results }`. That is no longer the current baseline: `/random`
already ships (in this same unreleased milestone) a `FilteredQuoteResult<T>` envelope — `{ status,
items, totalMatching, message }` — for every call, not just `?n=`. The issue's proposed shape both
renames `items` → `results` (inconsistent with every other paged/enveloped response in this API,
e.g. `ImportActionPageResponse.Items`) and drops `status`/`message`, which callers already depend
on.

**Correction (confirmed with the user):** extend the existing envelope rather than replace it.
`FilteredQuoteResult<T>` gains two new nullable properties, `RequestedCount` and `ReturnedCount`
(`int?`), populated only by `/random`; `null` for every other consumer of the same generic type
(currently `/search`, per #109). `Items`/`Status`/`TotalMatching`/`Message` keep their existing
names and meaning — no breaking change to the contract that already shipped. `ReturnedCount` can be
less than `RequestedCount` specifically because of conversation-aware dedup (section 3 below); that
is the scenario these two fields exist to make visible without a client having to re-derive its own
`n` value.

---

## Spec requirements (as corrected)

### 1. `QuoteResponse` — conversation membership

Nullable `Conversations` array (`IReadOnlyList<QuoteConversationMembership>?`), each entry `{
conversationId, position, totalLines }`. `null` when the quote belongs to no conversations — an
empty array is never returned (explicit in the issue; a deliberate exception to the `Genres`
pattern, which defaults to `[]`, since conversation membership is the uncommon case and `null` lets
clients skip a length check).

### 2. `GET /api/v1/conversations/{id}`

Full ordered line list. Each line is either a `QuoteResponse` (with its own `Conversations` field
suppressed — no recursive expansion, to avoid cycles) or a stage-direction/sound-cue line carrying
`text`/`soundFileUrl`/`imageUrl`/`language`/`isTranslated`. Respects `?lang=` with fallback to
`originalLanguage`, same pattern as `IQuoteService`'s existing `TranslationLang` helper.

### 3. `GET /api/v1/quotes/random` — conversation-aware dedup

When a selected quote belongs to a conversation, one of its conversations is chosen at random and
embedded (full line list). For `?n=`, every quote id appearing in a selected conversation is added
to the exclusion set — not just the quote that triggered the selection. If the pool is exhausted
before reaching `n`, return what's available; `ReturnedCount` reports the shortfall (see Scope
changes above).

---

## Design

### 1. `QuoteResponse.Conversations`

**Status:** ⬜ Not started

New `QuoteConversationMembership` DTO in `Quotinator.Core.Models`. `SqliteQuoteService` populates it
via a join against `ConversationLines` (query added to `Sql.cs` per the SQL centralisation policy,
with a `SqlQueryGuardTests.AssembledQueryCases` entry) — designed in #67 section 3, implemented here.

### 2. `GET /api/v1/conversations/{id}` endpoint

**Status:** ⬜ Not started

Registered in `QuoteEndpoints.cs` (or a new `ConversationEndpoints.cs` if the file is getting large
— follow #152's grouping precedent and put it under the same route-group tag as the other
`/api/v1/conversations` surface, since #69 is the only issue that adds one). `{id}` lookup uses
`UPPER(Id) = UPPER(@id)` in its `Sql.cs` query — new GUID route parameters default to
case-insensitive matching (established rule; the existing `/quotes/{id}` lookup predates it and is
a separate, already-tracked gap, not something this issue touches). 404 message goes through
`IApiLocalizer`/`ApiMessages` + `i18ntext/UI.*.json` (all three locales), never a hardcoded string.
`[Description]` attributes on the endpoint and its parameters per the OpenAPI documentation
requirement.

### 3. Random dedup

**Status:** ⬜ Not started

`SqliteQuoteService.GetRandom` gains conversation-exclusion logic: after selecting a quote that
belongs to ≥1 conversation, pick one conversation, look up every `QuoteId` referenced by its
`ConversationLines`, and add them all to the running exclusion set before continuing to fill the
remaining `n - 1` slots. `RequestedCount`/`ReturnedCount` set on the returned
`FilteredQuoteResult<QuoteResponse>` per the Scope changes correction above.

### 4. Documentation sync

**Status:** ⬜ Not started

Per CLAUDE.md's "Keeping API documentation in sync": update `README.md`'s endpoint table,
`addon/DOCS.md`'s endpoint table, and `QuoteEndpoints.cs`'s `[Description]` attributes in the same
commit. Note the new nullable `RequestedCount`/`ReturnedCount` fields on the `/random` response in
both docs; no `?n=` breaking-change note needed since the existing envelope shape is preserved
(update, not replace).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `QuoteResponse.Conversations` is `null` for a quote in no conversation, populated for one that is | Unit test | New `Quotinator.Engine.Tests` test |
| 2 | ⬜ | `GET /api/v1/conversations/{id}` returns lines in `Order` order | Unit test | New `Quotinator.Api.Tests` endpoint test |
| 3 | ⬜ | `GET /api/v1/conversations/{id}` respects `?lang=` with fallback to `originalLanguage` | Unit test | New endpoint test, translated + untranslated case |
| 4 | ⬜ | `{id}` route lookup is case-insensitive | Unit test | New endpoint test: uppercase-cased id in the URL still resolves |
| 5 | ⬜ | `GET /api/v1/conversations/{unknown-id}` returns 404 via `IApiLocalizer` (not a hardcoded string) | Unit test | New endpoint test asserting the response body matches the localized key |
| 6 | ⬜ | Embedded `QuoteResponse` inside a conversation line has no recursive `Conversations` field | Unit test | New endpoint test |
| 7 | ⬜ | `/random?n=` never returns two quotes from the same conversation | Unit test | New `SqliteQuoteService` test with a seeded conversation |
| 8 | ⬜ | `RequestedCount`/`ReturnedCount` reflect a short result when the pool is exhausted by dedup | Unit test | New test with a small seeded pool |
| 9 | ⬜ | `RequestedCount`/`ReturnedCount` are `null` for `/search` (unaffected by this change) | Unit test | Existing `QuoteEndpointsTests` search cases still pass unmodified |
| 10 | ⬜ | `README.md`/`addon/DOCS.md` endpoint tables updated | Live | Manual doc review against the implemented endpoint |
| 11 | ⬜ | OpenAPI/Scalar reference reflects the new endpoint and fields | Live (T1) | `GET /scalar/v1` in a running instance; confirm `/conversations/{id}` and the two new fields appear |
| 12 | ⬜ | Docker build succeeds | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` |

---

## Notes

Consider whether `ReturnedCount` can differ from `RequestedCount` for reasons other than dedup (e.g.
`TotalMatching` itself being smaller than `n`) — yes, that case already exists today via
`TotalMatching`; `RequestedCount`/`ReturnedCount` make the shortfall explicit without requiring the
client to compare against its own request.
