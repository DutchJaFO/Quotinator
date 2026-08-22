# A file-authored explicit id is canonicalized at capture, and its joins survive

**Smoke:** no
**Traces to:** #209

## Preconditions

A running container with an admin key, and a **clean seed** — the seed itself is part of this test, not
just setup for the import below.

The bundled curated file's own Conversations reference StageDirections and SoundCues by id. #209's fix
would have broken those references if left incomplete, so a seed completing with no
`SQLite Error 19: FOREIGN KEY constraint failed` is itself an assertion here.

## Determinism

- **The fixture's ids are lowercase in the file**, and the masterdata lookup below uses that same
  lowercase form in the URL — the exact scenario that originally returned 404. Changing either casing
  changes what the test proves.
- **The seed must have run before the import.** A container whose seeding failed would produce a
  passing import and a meaningless FK result.

## Steps

```bash
cat > .claude/temp/smoke-209.json <<'EOF'
{
  "quotes": [{"id":"f6000001-0000-4000-8000-000000000001","quote":"A #209 smoke test line.","originalLanguage":"en","source":"209 Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "sources": [{"id":"f6000002-0000-4000-8000-000000000002","title":"209 Smoke Test Film","type":"movie"}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-209.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/sources/f6000002-0000-4000-8000-000000000002"
curl -s "http://localhost:8080/api/v1/quotes/f6000001-0000-4000-8000-000000000001"
```

## Expected output

- The import returns `200`.
- The masterdata lookup returns `200`, with `id` shown canonicalized — lowercase, per ADR 012's
  system-wide convention.
- The quote lookup resolves `source` to `"209 Smoke Test Film"` via the Quote→Source join, proving the
  fix did not break the join in order to make the masterdata lookup work.
- The container's seed produced no `SQLite Error 19`.

## Observed effect

Partially established: a clean seed emitting no `SQLite Error 19` is an observed effect and is asserted
above. What the container logs during the import itself has not been captured.

**The original failure this replaced:** a file-authored explicit id reached storage in whatever raw
casing the file used, never canonicalized. A `Guid`-typed lookup — which force-uppercases — then
silently failed to find a non-canonically-stored row, even though the same row resolved fine via a
join.

## Cleanup

`rm .claude/temp/smoke-209.json`
