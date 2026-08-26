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

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-id-03 --port 18203
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Establish the log's starting state

```powershell
$before = ([regex]::Matches((docker logs qt-id-03 2>&1 | Out-String), 'SQLite Error 19')).Count
$before
```

**Expected:** `0`. The profile's own seed produced no foreign-key violation.

**On failure:** a non-zero count here means the seed itself is failing, and step 4's reading would be
measuring that rather than this test's import. Stop — this is a profile problem, not a result.

### 3. Import a conversation line whose `quoteId` casing does not match its quote

```powershell
$fixture = "$PWD\.claude\temp\smoke-210-conv.json"
$json = @'
{
  "quotes": [{"id":"f0000210-0000-4000-8000-000000000211","quote":"A #210 conversation-line smoke test quote.","originalLanguage":"en","source":"Smoke Test Film 210b","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "conversations": [{"id":"f0000210-0000-4000-8000-000000000212","description":"A #210 smoke test conversation.","lines":[{"order":1,"type":"quote","quoteId":"F0000210-0000-4000-8000-000000000211"}]}]
}
'@
[IO.File]::WriteAllText($fixture, $json, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18203/api/v1/import" `
  --file $fixture --duplicate-resolution newest-wins --expect 200
```

**Expected:** `200`.

**On failure:** stop. A non-`200` here is the bug this test exists to catch, and step 4 names it.

### 4. Confirm no foreign-key violation was logged

```powershell
$after = ([regex]::Matches((docker logs qt-id-03 2>&1 | Out-String), 'SQLite Error 19')).Count
"$before -> $after"
```

**Expected:** `0 -> 0` — unchanged from step 2.

The status code alone is not the whole assertion: the log is where
`SQLite Error 19: FOREIGN KEY constraint failed` would name itself, and comparing against step 2 is what
makes this specific to the import rather than to whatever the container did before it.

## Observed effect

Not yet established as a captured record. The failure mode is known and specific — a
`SQLite Error 19` on import — but what a passing run emits has not been recorded.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-id-03
Remove-Item $fixture
```
