# Baseline — health, version, random and search respond correctly

**Smoke:** yes
**Traces to:** —

## Preconditions

A container built from the current working tree (`quotinator:local`), started fresh with no
pre-existing volume, and allowed to finish seeding before any request is made.

Nothing else in this suite is worth running if this fails, which is why it is first.

## Determinism

- **Image**: `quotinator:local`, built from the working tree — never a published tag.
- **No volume**: a fresh container each run, so the bundled dataset is exactly what shipped in the
  image.
- **Seeding complete**: `/health` returning healthy is the gate; querying before that races the
  startup wait page.

## Steps

```bash
docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
curl -s http://localhost:8080/api/v1/health
curl -s http://localhost:8080/api/v1/version
curl -s http://localhost:8080/api/v1/quotes/random
curl -s "http://localhost:8080/api/v1/quotes/search?q=love"
curl -s "http://localhost:8080/api/v1/quotes/search?q=Casablanca&field=source"
curl -s "http://localhost:8080/api/v1/quotes/search?q=Churchill&field=author"
curl -s "http://localhost:8080/api/v1/quotes/search?q=Rick&field=character"
curl -s "http://localhost:8080/api/v1/quotes/search?q=love&type=person"
```

## Expected output

`/version` must return the expected version number. **A missing `Directory.Build.props` in the build
context silently produces `1.0.0` while `/health` still returns healthy** — so a healthy container is
not by itself evidence the build context was complete.

The search queries cover three paths:

- default full-text — `love` returns results
- `field=source` — `Casablanca` returns results
- `field=author` — `Churchill` returns the curated Winston Churchill quote

`field=character` (`Rick`) and `type=person&q=love` may return an empty `items` array with a
`message`, because no bundled data currently matches either. That is expected behaviour, not a
failure.

## Observed effect

Not yet established. This document records what a pass requires; what the container actually emits
while serving these requests — its log lines and their shape — has not been captured. See
[the index](../README.md#test-outcomes-feed-the-knowledgebase) for why that matters.

## Cleanup

`docker run --rm` removes the container on exit. No volume is created.
