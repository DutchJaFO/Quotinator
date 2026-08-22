# Baseline — health, version, random and search respond correctly

**Smoke:** yes
**Environment:** Fresh
**Traces to:** —

## Preconditions

Nothing beyond the Fresh profile. This test *is* the profile working — which is why it is first, and
why nothing else in the suite is worth running if it fails.

## Determinism

Every variable this test depends on is one the Fresh profile pins: the image built from the working
tree rather than a published tag, no pre-existing volume, and a readiness poll gating the first
request. Querying before that gate races the startup wait page.

## Steps

Run the **Fresh** profile, then:

```bash
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

None. This test only reads, so the profile's container and volume are left as they are for whatever
runs next.
