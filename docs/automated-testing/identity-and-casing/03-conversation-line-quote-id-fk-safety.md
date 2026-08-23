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

### 1. Create this test's own environment

```bash
docker rm -f qt-id-03 2>/dev/null; docker volume rm qt-id-03-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-id-03 -p 18203:8080 -v qt-id-03-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true \
  quotinator:local
until curl -sf http://localhost:18203/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Establish the log's starting state

```bash
docker logs qt-id-03 2>&1 | grep -c "SQLite Error 19"
```

**Expected:** `0`. The profile's own seed produced no foreign-key violation.

**On failure:** a non-zero count here means the seed itself is failing, and step 4's reading would be
measuring that rather than this test's import. Stop — this is a profile problem, not a result.

### 3. Import a conversation line whose `quoteId` casing does not match its quote

```bash
cat > .claude/temp/smoke-210-conv.json <<'EOF'
{
  "quotes": [{"id":"f0000210-0000-4000-8000-000000000211","quote":"A #210 conversation-line smoke test quote.","originalLanguage":"en","source":"Smoke Test Film 210b","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "conversations": [{"id":"f0000210-0000-4000-8000-000000000212","description":"A #210 smoke test conversation.","lines":[{"order":1,"type":"quote","quoteId":"F0000210-0000-4000-8000-000000000211"}]}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-210-conv.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:18203/api/v1/import"
```

**Expected:** `200`.

**On failure:** stop. A non-`200` here is the bug this test exists to catch, and step 4 names it.

### 4. Confirm no foreign-key violation was logged

```bash
docker logs qt-id-03 2>&1 | grep -c "SQLite Error 19"
```

**Expected:** still `0` — unchanged from step 2.

The status code alone is not the whole assertion: the log is where
`SQLite Error 19: FOREIGN KEY constraint failed` would name itself, and comparing against step 2 is what
makes this specific to the import rather than to whatever the container did before it.

## Observed effect

Not yet established as a captured record. The failure mode is known and specific — a
`SQLite Error 19` on import — but what a passing run emits has not been recorded.

## Cleanup

```bash
docker rm -f qt-id-03 2>/dev/null
docker volume rm qt-id-03-data 2>/dev/null
rm .claude/temp/smoke-210-conv.json
```
