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

## Steps

**Flag off (default) — a fresh container without the env var:**

```bash
docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
cat > .claude/temp/smoke-222.json <<'EOF'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"I will always have Café de Flore.","originalLanguage":"en","source":"Café de Flore","date":"1990","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-222.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/quotes/search?q=CAF%C3%89&field=source"
```

**Flag on — stop that container, start a fresh one with the env var set:**

```bash
docker stop <container>
docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> -e Quotinator__UnicodeAwareSearch=true quotinator:local
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-222.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/quotes/search?q=CAF%C3%89&field=source"
```

## Expected output

**Flag off:** import returns `200`. The search returns an empty `items` array with a `message` — the
fixture's `Café de Flore` is not matched, proving default behaviour is unchanged.

**Flag on:** the same query against the same fixture returns `200` and **includes** the fixture's own
item, `source: "Café de Flore"`.

Assert its presence, not a total. The fixture is the only accented case-varying title today, but a
bundled source acquiring one would make this two items for an entirely correct reason.

## Observed effect

Not yet established. The behavioural difference is asserted above; what the container logs while
serving each half has not been captured.

## Cleanup

`docker run --rm` removes each container on exit. Delete the fixture:
`rm .claude/temp/smoke-222.json`.
