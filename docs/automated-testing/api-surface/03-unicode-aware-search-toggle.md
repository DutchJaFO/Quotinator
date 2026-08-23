# The Unicode-aware search flag reaches the running app and changes query behaviour

**Smoke:** no
**Environment:** Fresh
**Traces to:** #222

## Preconditions

This proves the **container-level wiring**, not the matching logic — that is already covered by unit
tests. What is under test is whether `Quotinator__UnicodeAwareSearch` (or the HA add-on's
`unicode_aware_search` option) actually reaches the running app and flips real query behaviour.

No bundled or curated data contains a case-varying accented string, so the test imports a small
throwaway fixture rather than relying on shipped content.

Both halves must reach a successful import (`200`) before their search assertion means anything — a
failed import produces the same empty result as a correctly-unmatched search.

## Determinism

- **Two fresh containers, not one restarted.** A fresh container has no persisted data, so the fixture
  must be imported again in the second half. Reusing a volume would leave the first half's data and
  make the comparison meaningless.
- **Same query, same fixture, one variable.** Only the env var differs between the two halves. That is
  what makes this a with-and-without comparison rather than two unrelated observations.
- The query is percent-encoded (`CAF%C3%89`) — sending the raw accented character depends on shell
  and terminal encoding.
- **Assert the fixture's presence, not a total.** The fixture is the only accented case-varying title
  today, but a bundled source acquiring one would make this two items for an entirely correct reason.

## Steps

### 1. Import the fixture into a container with the flag off (default)

The profile's own environment, under this test's own container name:

```bash
docker build -f docker/Dockerfile -t quotinator:local .
dotnet script scripts/testing/test-env.csx -- create --name qt-api-03-off --port 18103 \
  --env Quotinator__AutoPurgeBundledImportActions=false
cat > .claude/temp/smoke-222.json <<'EOF'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"I will always have Café de Flore.","originalLanguage":"en","source":"Café de Flore","date":"1990","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]
}
EOF
curl -s -X POST -H "X-Api-Key: smoketest" -F "file=@.claude/temp/smoke-222.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:18103/api/v1/import"
```

**Expected:** import returns `200`.

**On failure:** stop. A failed import produces the same empty result the next step reads as a pass,
so its "not matched" outcome would be indistinguishable from the behaviour under test.

### 2. Search the accented title in the wrong case, flag off

```bash
curl -s "http://localhost:18103/api/v1/quotes/search?q=CAF%C3%89&field=source"
```

**Expected:** an empty `items` array with a `message` — the fixture's `Café de Flore` is not matched,
proving default behaviour is unchanged.

### 3. Import the same fixture into a second container with the flag on

The same environment plus the one variable under test:

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-03-off
dotnet script scripts/testing/test-env.csx -- create --name qt-api-03-on --port 19103 \
  --env Quotinator__AutoPurgeBundledImportActions=false \
  --env Quotinator__UnicodeAwareSearch=true
curl -s -X POST -H "X-Api-Key: smoketest" -F "file=@.claude/temp/smoke-222.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:19103/api/v1/import"
```

**Expected:** import returns `200`.

**On failure:** stop, for the same reason as step 1 — an unimported fixture cannot be matched, and the
next step would report the flag as having no effect.

### 4. Repeat the same query with the flag on

```bash
curl -s "http://localhost:19103/api/v1/quotes/search?q=CAF%C3%89&field=source"
```

**Expected:** `200`, and the response **includes** the fixture's own item, `source: "Café de Flore"`.

## Observed effect

Not yet established. The behavioural difference is asserted above; what the container logs while
serving each half has not been captured.

## Cleanup

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-03-off
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-03-on
rm .claude/temp/smoke-222.json
```
