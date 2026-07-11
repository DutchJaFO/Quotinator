# #69 — API: conversation membership, GET /conversations/{id}, random dedup

**Status:** In progress
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

**Status:** ✅ Done

New `QuoteConversationMembership` DTO in `Quotinator.Core.Models`. `SqliteQuoteService.GetById`,
`GetAll`, and `Search` all call a new private `LoadConversationMemberships` helper, backed by
`Sql.ConversationLines.SelectMembershipForQuote` (added to `Sql.cs`, with a
`SqlQueryGuardTests.AggregateQueries_MatchDocumentedInventory` entry since it contains a `COUNT(*)`
subquery for `totalLines`). `Conversations` is `null` (never `[]`) when the quote belongs to no
conversation, matching the deliberate exception to the `Genres` pattern documented in the spec
above.

Design decision found while implementing: `StageDirectionEntity`/`SoundCueEntity` (added in #67)
have no `OriginalLanguage` column — #67's schema only tracked it on `Quote`/`Conversation`. Rather
than reopening the already-shipped #67 migration, the original language for stage-direction and
sound-cue text is hardcoded to `"en"` in `Sql.StageDirections.SelectByIdWithTranslation` /
`Sql.SoundCues.SelectByIdWithTranslation` and `SqliteQuoteService.BuildLineResponse`. This is a
known, deliberate limitation — documented inline in `Sql.cs` — not an oversight; every bundled and
curated stage direction/sound cue is in fact English-original at time of writing.

### 2. `GET /api/v1/conversations/{id}` endpoint

**Status:** ✅ Done

Implemented as a new `Quotinator.Api.Endpoints.ConversationEndpoints.cs` (file getting large was the
trigger, following #152's grouping precedent), registered via `app.MapConversationEndpoints()` in
`Program.cs` under a new `Conversations` OpenAPI tag (`ApiTags.Conversations`). `{id}` lookup uses
`Sql.Conversations.SelectForRead`, case-insensitive per the established rule. 404 goes through
`IApiLocalizer`/`ApiMessages.ConversationNotFound`, translated in all three
`i18ntext/UI.*.json` locales. `?lang=` validated the same way as the existing quote endpoints
(400 on an invalid code). `[Description]` attributes present on the endpoint and its parameters.

`SqliteQuoteService.BuildConversationResponse` builds the full ordered line list; each line is
either a `QuoteResponse` (via the existing `ToResponse`, called with `conversations: null,
embeddedConversation: null` to suppress recursion — proven by
`ConversationEndpointsTests.GetById_QuoteLine_HasNoRecursiveConversationsField`) or a
stage-direction/sound-cue line. The wire-format `type` discriminator (`stage_direction`/
`sound_cue`/`quote`) is derived from the DB enum name via
`JsonNamingPolicy.SnakeCaseLower.ConvertName`, avoiding a hand-duplicated mapping table.

### 3. Random dedup

**Status:** ✅ Done

`SqliteQuoteService.GetRandom` rewritten with an iterative loop: while `items.Count < count`, query
excluding `excludedIds` so far; for each picked quote, call `LoadConversationMemberships` — if any
membership exists, pick one conversation at random (`Random.Shared.Next`) and add every quote id
referenced by that conversation's lines (`Sql.ConversationLines.SelectQuoteIdsForConversation`) to
`excludedIds` before continuing, embedding the full conversation via `BuildConversationResponse`.
Loop is safety-valved by `maxPasses = totalMatching + 1` so an exhausted pool returns early rather
than looping forever. `RequestedCount`/`ReturnedCount` are set on the returned
`FilteredQuoteResult<QuoteResponse>`; the `NoResults` branch in `QuoteEndpoints.GetRandom` was fixed
to preserve them rather than discarding them on an empty result.

### 4. Documentation sync

**Status:** ✅ Done

`README.md` and `addon/DOCS.md` endpoint tables updated with the new
`GET /api/v1/conversations/{id}` row; `/random`'s row/description updated to mention
`requestedCount`/`returnedCount`/`embeddedConversation`. README also gained a short paragraph
explaining the universal `conversations` membership field on quote responses.
`QuoteEndpoints.cs`'s `/random` `.WithDescription(...)` updated in the same commit. No `?n=`
breaking-change note needed — the existing envelope shape was extended, not replaced.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `QuoteResponse.Conversations` is `null` for a quote in no conversation, populated for one that is | Unit test | `SqliteQuoteServiceConversationTests.GetById_QuoteInNoConversation_ConversationsIsNull` / `GetById_QuoteInConversation_ConversationsPopulated` |
| 2 | ✅ | `GET /api/v1/conversations/{id}` returns lines in `Order` order | Unit test | `ConversationEndpointsTests.GetById_KnownId_ReturnsOkWithLinesInOrder`; also `SqliteQuoteServiceConversationTests.GetConversation_MixedLineTypes_ReturnsInOrder` |
| 3 | ✅ | `GET /api/v1/conversations/{id}` respects `?lang=` with fallback to `originalLanguage` | Unit test | `SqliteQuoteServiceConversationTests.GetConversation_WithTranslation_ReturnsTranslatedText` / `..._NoTranslation_FallsBackToOriginal` |
| 4 | ✅ | `{id}` route lookup is case-insensitive | Unit test | `ConversationEndpointsTests.GetById_UppercaseCasedId_StillResolves`; `SqliteQuoteServiceConversationTests.GetConversation_CaseInsensitiveId_Resolves` |
| 5 | ✅ | `GET /api/v1/conversations/{unknown-id}` returns 404 via `IApiLocalizer` (not a hardcoded string) | Unit test | `ConversationEndpointsTests.GetById_UnknownId_Returns404WithLocalisedDetail` / `..._WithAcceptLanguageNl_ReturnsDutchDetail` |
| 6 | ✅ | Embedded `QuoteResponse` inside a conversation line has no recursive `Conversations` field | Unit test | `ConversationEndpointsTests.GetById_QuoteLine_HasNoRecursiveConversationsField`; `SqliteQuoteServiceConversationTests.GetConversation_QuoteLine_HasNoRecursion` |
| 7 | ✅ | `/random?n=` never returns two quotes from the same conversation | Unit test | `SqliteQuoteServiceConversationTests.GetRandom_ConversationQuote_DedupsPartnerQuote` (+ live T2 evidence below) |
| 8 | ✅ | `RequestedCount`/`ReturnedCount` reflect a short result when the pool is exhausted by dedup | Unit test | `SqliteQuoteServiceConversationTests.GetRandom_DedupShrinksPool_ReturnedCountLessThanRequested` (+ live T2 evidence below) |
| 9 | ✅ | `RequestedCount`/`ReturnedCount` are `null` for `/search` (unaffected by this change) | Unit test | Existing `QuoteEndpointsTests` search cases pass unmodified; `FilteredQuoteResult<T>` leaves both fields unset outside `GetRandom` |
| 10 | ✅ | `README.md`/`addon/DOCS.md` endpoint tables updated | Live | Manual doc review — `/api/v1/conversations/{id}` row added, `/random` description mentions `requestedCount`/`returnedCount`/`embeddedConversation` |
| 11 | ⬜ | OpenAPI/Scalar reference reflects the new endpoint and fields | Live (T1) | Awaiting user's own Visual Studio build/run per the T1 tier — `GET /scalar/v1`; confirm `/conversations/{id}` and the two new fields appear |
| 12 | ✅ | Docker build succeeds | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded; container run confirmed `/api/v1/version` healthy (`schemaVersion: 8`), `GET /api/v1/conversations/{id}` against the real seeded "The Black Knight" conversation (`ce516316-6d19-4244-a7b2-f2eddd125cda`) returned ordered `sound_cue` + 2 `quote` lines, uppercase-cased id resolved, unknown id returned 404 with the localized detail, and `?character=Ted%20Striker`/`?source=Airplane!&n=2` confirmed live dedup (`requestedCount: 2, returnedCount: 1`) with `embeddedConversation` populated |

---

## Notes

Consider whether `ReturnedCount` can differ from `RequestedCount` for reasons other than dedup (e.g.
`TotalMatching` itself being smaller than `n`) — yes, that case already exists today via
`TotalMatching`; `RequestedCount`/`ReturnedCount` make the shortfall explicit without requiring the
client to compare against its own request.
