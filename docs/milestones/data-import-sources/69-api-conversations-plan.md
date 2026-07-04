# #69 — API: conversations

**Status:** Planning
**GitHub issue:** #69  
**Depends on:** #67 (schema), #68 (curated data)

---

## Spec requirements

1. `QuoteResponse` gains an optional `conversations` array:
   ```json
   {
     "id": "...",
     "quote": "...",
     "conversations": [
       { "id": "...", "lineCount": 3 }
     ]
   }
   ```
   Populated only when the quote has an associated conversation.

2. New endpoint `GET /api/v1/conversations/{id}` — returns the full conversation:
   ```json
   {
     "id": "...",
     "quoteId": "...",
     "lines": [
       { "position": 1, "character": "Roger Murdock", "text": "Have you ever been in a cockpit before?" },
       { "position": 2, "character": "Joey", "text": "No sir, I've never been up in a plane before." }
     ],
     "stageDirections": [ ... ],
     "soundCues": [ ... ]
   }
   ```
   Lines ordered by `Position ASC`.

3. `?lang=` parameter respected — returns translated `text` when a translation exists, falls back to original language.

4. `GET /api/v1/quotes/random` — conversation-aware dedup: when returning a quote that belongs to a conversation, never return two quotes from the same conversation in the same `?n=` response.

5. `?n=` response shape change (breaking change, must be documented in changelog):
   Old: `[ { quote: ... }, ... ]`  
   New: `{ "requestedCount": N, "returnedCount": M, "items": [ { quote: ... }, ... ] }`

---

## Implementation steps

1. [ ] Update `QuoteResponse` to include optional `conversations` array
2. [ ] Update `SqliteQuoteService` to populate `conversations` when present
3. [ ] Register `GET /api/v1/conversations/{id}` in `QuoteEndpoints.cs`
4. [ ] Implement conversation query: lines + stage directions + sound cues, ordered correctly
5. [ ] Apply `?lang=` to `ConversationLines.Text` and `StageDirectionTranslations.Text`
6. [ ] Update `GetRandom` / `GetRandomN` for conversation-aware dedup
7. [ ] Update `?n=` response shape (breaking change)
8. [ ] Update `README.md`, `addon/DOCS.md` endpoint tables
9. [ ] Add OpenAPI `[Description]` on new endpoint and parameters
10. [ ] `CHANGELOG.md`: document the `?n=` breaking change prominently
11. [ ] Tests: conversation endpoint returns ordered lines, lang fallback works, random dedup works, n= shape change

---

## Notes

The `?n=` response shape change is breaking. Consumers using the flat array format (e.g. the MagicMirror integration in `CLAUDE.md`) will need to update their `jq` filter from `.[].quote` to `.items[].quote`. Document this change in the changelog with the migration path.

Consider whether `returnedCount` can differ from `requestedCount` — yes, when there are fewer distinct conversations than `n` requested.
