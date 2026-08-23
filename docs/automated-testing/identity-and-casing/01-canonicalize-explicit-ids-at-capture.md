# A file-authored explicit id is canonicalized at capture, and its joins survive

**Smoke:** no
**Environment:** Fresh
**Traces to:** #209

## Preconditions

Nothing beyond the Fresh profile — but the profile's own first boot is **under test here**, not merely
setup for the import below, which is why the log check is the first step rather than an afterthought.

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

### 1. Create this test's own environment

```bash
docker rm -f qt-id-01 2>/dev/null; docker volume rm qt-id-01-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-id-01 -p 18201:8080 -v qt-id-01-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true \
  quotinator:local
until curl -sf http://localhost:18201/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Read the seed's own log before importing anything

```bash
docker logs qt-id-01 2>&1 | grep -c "SQLite Error 19"
```

**Expected:** `0` — the container's seed produced no `SQLite Error 19`.

**On failure:** any other number is a failure, and it is the first step for a reason: a non-zero count
means the seed is what broke, so the assertions below would be reporting on a database that was never
built correctly. Stop.

### 3. Import a fixture whose quote and Source both carry file-authored explicit ids

```bash
cat > .claude/temp/smoke-209.json <<'EOF'
{
  "quotes": [{"id":"f6000001-0000-4000-8000-000000000001","quote":"A #209 smoke test line.","originalLanguage":"en","source":"209 Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "sources": [{"id":"f6000002-0000-4000-8000-000000000002","title":"209 Smoke Test Film","type":"movie"}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-209.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:18201/api/v1/import"
```

**Expected:** the import returns `200`.

### 4. Look the Source up by the id the file authored

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:18201/api/v1/masterdata/sources/f6000002-0000-4000-8000-000000000002"
```

**Expected:** `200`, with `id` shown canonicalized — lowercase, per ADR 012's system-wide convention.

### 5. Fetch the quote and confirm its Source join still resolves

```bash
curl -s "http://localhost:18201/api/v1/quotes/f6000001-0000-4000-8000-000000000001"
```

**Expected:** the quote lookup resolves `source` to `"209 Smoke Test Film"` via the Quote→Source join,
proving the fix did not break the join in order to make the masterdata lookup work.

## Observed effect

Partially established: a clean seed emitting no `SQLite Error 19` is an observed effect and is asserted
above. What the container logs during the import itself has not been captured.

**The original failure this replaced:** a file-authored explicit id reached storage in whatever raw
casing the file used, never canonicalized. A `Guid`-typed lookup — which force-uppercases — then
silently failed to find a non-canonically-stored row, even though the same row resolved fine via a
join.

## Cleanup

```bash
docker rm -f qt-id-01 2>/dev/null
docker volume rm qt-id-01-data 2>/dev/null
rm .claude/temp/smoke-209.json
```
