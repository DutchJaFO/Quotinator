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

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-api-01 --port 18101
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Check health

```powershell
(Invoke-RestMethod "http://localhost:18101/api/v1/health").status
```

**Expected:** `healthy` — not `starting`, which the same endpoint returns with `503` while the seed is
still running, and not `unhealthy`.

**On failure:** stop. Every later step reads the same container, so a degraded or still-initialising
app makes all of them meaningless rather than failing them individually.

### 3. Check the reported version

```powershell
(Invoke-RestMethod "http://localhost:18101/api/v1/version").version
(Select-Xml -Path Directory.Build.props -XPath '//Version').Node.InnerText
```

**Expected:** the two match. Read from `Directory.Build.props` rather than written into this document,
so the step cannot go stale the next time the version is bumped.

**On failure:** **a missing `Directory.Build.props` in the build context silently produces `1.0.0`
while `/health` still returns healthy** — so a healthy container is not by itself evidence the build
context was complete. Stop and rebuild from a complete context; every later result is being read off
an image that is not the one under test.

### 4. Fetch a random quote

```powershell
$random = Invoke-RestMethod "http://localhost:18101/api/v1/quotes/random"
"status=$($random.status) returned=$($random.returnedCount)"
$random.items[0].quote
```

**Expected:** `status=Ok`, `returned=1`, and a non-empty quote body. The endpoint returns `200` with
`status=NoResults` and an empty `items` array against an empty database, so the status code alone does
not establish that the seeded content is readable — the quote text is what does.

### 5. Search the default full-text path

```powershell
$love = Invoke-RestMethod "http://localhost:18101/api/v1/quotes/search?q=love"
"status=$($love.status) matching=$($love.totalMatching)"
```

**Expected:** `status=Ok` and `matching` greater than zero. The number itself is not predicted here —
it is a property of whatever the bundled sources currently contain.

### 6. Search scoped to `source`

```powershell
$casablanca = Invoke-RestMethod "http://localhost:18101/api/v1/quotes/search?q=Casablanca&field=source"
"status=$($casablanca.status) matching=$($casablanca.totalMatching)"
@($casablanca.items | Where-Object { $_.source -notmatch 'Casablanca' }).Count
```

**Expected:** `status=Ok`, `matching` greater than zero, and `0` rows whose `source` does not contain
the term — the scoping is the subject here, not the count.

### 7. Search scoped to `author`

```powershell
$churchill = Invoke-RestMethod "http://localhost:18101/api/v1/quotes/search?q=Churchill&field=author"
$churchill.items.author
```

**Expected:** the curated Winston Churchill quote's author. An empty result means the curated file did
not seed, which every later curated-content test in the suite also depends on.

### 8. Search scoped to `character`

```powershell
(Invoke-RestMethod "http://localhost:18101/api/v1/quotes/search?q=Rick&field=character").status
```

**Expected:** `NoResults` — no bundled data currently matches, and the endpoint says so rather than
erroring. That is expected behaviour, not a failure.

### 9. Search filtered to `type=person`

```powershell
(Invoke-RestMethod "http://localhost:18101/api/v1/quotes/search?q=love&type=person").status
```

**Expected:** `NoResults`, for the same reason as step 8: the filter combination matches nothing in the
bundled data, and reporting that is the correct behaviour.

## Observed effect

Not yet established. This document records what a pass requires; what the container actually emits
while serving these requests — its log lines and their shape — has not been captured. See
[the index](../README.md#test-outcomes-feed-the-knowledgebase) for why that matters.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-01
```
