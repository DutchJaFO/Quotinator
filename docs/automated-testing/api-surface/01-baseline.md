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

### 1. Create this test's own environment

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-api-01 --port 18101
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Check health

```bash
curl -s -w "\n%{http_code}\n" http://localhost:18101/api/v1/health
```

**Expected:** `200` with `{"status":"healthy"}` — not `503 {"status":"starting"}` and not
`{"status":"unhealthy"}`.

**On failure:** stop. Every later step reads the same container, so a degraded or still-initialising
app makes all of them meaningless rather than failing them individually.

### 3. Check the reported version

```bash
curl -s http://localhost:18101/api/v1/version
```

**Expected:** the expected version number.

**On failure:** **a missing `Directory.Build.props` in the build context silently produces `1.0.0`
while `/health` still returns healthy** — so a healthy container is not by itself evidence the build
context was complete. Stop and rebuild from a complete context; every later result is being read off
an image that is not the one under test.

### 4. Fetch a random quote

```bash
curl -s -w "\n%{http_code}\n" http://localhost:18101/api/v1/quotes/random | grep -c '"quote":'
```

**Expected:** `1` — a quote body came back. The endpoint returns `200` with `{"status":"NoResults"}`
and an empty `items` array against an empty database, so the status code alone does not establish that
the seeded content is readable.

### 5. Search the default full-text path

```bash
curl -s "http://localhost:18101/api/v1/quotes/search?q=love"
```

**Expected:** `love` returns results.

### 6. Search scoped to `source`

```bash
curl -s "http://localhost:18101/api/v1/quotes/search?q=Casablanca&field=source"
```

**Expected:** `Casablanca` returns results.

### 7. Search scoped to `author`

```bash
curl -s "http://localhost:18101/api/v1/quotes/search?q=Churchill&field=author"
```

**Expected:** `Churchill` returns the curated Winston Churchill quote.

### 8. Search scoped to `character`

```bash
curl -s "http://localhost:18101/api/v1/quotes/search?q=Rick&field=character"
```

**Expected:** may return an empty `items` array with a `message`, because no bundled data currently
matches. That is expected behaviour, not a failure.

### 9. Search filtered to `type=person`

```bash
curl -s "http://localhost:18101/api/v1/quotes/search?q=love&type=person"
```

**Expected:** may return an empty `items` array with a `message`, because no bundled data currently
matches. That is expected behaviour, not a failure.

## Observed effect

Not yet established. This document records what a pass requires; what the container actually emits
while serving these requests — its log lines and their shape — has not been captured. See
[the index](../README.md#test-outcomes-feed-the-knowledgebase) for why that matters.

## Cleanup

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-01
```
