# A conversation line referencing a quote in the wrong casing does not violate the foreign key

**Smoke:** no
**Environment:** Fresh
**Traces to:** #210

## Preconditions

Nothing beyond the Fresh profile — both records under test arrive in this test's own import.

`Quotinator_ConversationLine` holds a real `FOREIGN KEY` to `Quotinator_Quote(Id)`. A line referencing
a quote by an id whose casing does not match the quote's now-canonical form must still satisfy it —
the same bug class
[`01-canonicalize-explicit-ids-at-capture.md`](01-canonicalize-explicit-ids-at-capture.md) covers for
`StageDirectionId`/`SoundCueId`, now covering `QuoteId`.

## Determinism

- **The casing mismatch is deliberate and load-bearing.** The quote's own `id` is lowercase; the
  conversation line's `quoteId` uses the uppercase form of that same id. Making both the same casing
  would produce a passing test that proves nothing.
- Both records arrive in a single import, so the FK is evaluated within one transaction rather than
  across two runs.

## Steps

Run the **Fresh** profile, then:

```bash
cat > .claude/temp/smoke-210-conv.json <<'EOF'
{
  "quotes": [{"id":"f0000210-0000-4000-8000-000000000211","quote":"A #210 conversation-line smoke test quote.","originalLanguage":"en","source":"Smoke Test Film 210b","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "conversations": [{"id":"f0000210-0000-4000-8000-000000000212","description":"A #210 smoke test conversation.","lines":[{"order":1,"type":"quote","quoteId":"F0000210-0000-4000-8000-000000000211"}]}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-210-conv.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
docker logs qt-env 2>&1 | grep -c "SQLite Error 19"
```

## Expected output

`200`, and specifically **not** `SQLite Error 19: FOREIGN KEY constraint failed` — the `grep -c` prints
`0`. The status code alone is not the whole assertion: the log is where the constraint failure would
name itself.

## Observed effect

Not yet established as a captured record. The failure mode is known and specific — a
`SQLite Error 19` on import — but what a passing run emits has not been recorded.

## Cleanup

`rm .claude/temp/smoke-210-conv.json`

The imported quote and conversation remain in the database — restore the Fresh profile before the next
test.
