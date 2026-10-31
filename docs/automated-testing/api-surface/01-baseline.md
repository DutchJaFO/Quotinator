# Baseline: health, version, random and search respond correctly

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** none

## Preconditions

Nothing beyond the Fresh profile. This test *is* the profile working, which is why it is first, and
why nothing else in the suite is worth running if it fails.

The content it searches is its own: step 2 imports [two quotes](fixtures/baseline-quotes.json) through
`POST /api/v1/import`, so a broken import blocks this document. That is the index's second honest
resolution for a precondition, and it is named here so a blocked run reads as that.

## Determinism

Every variable this test depends on is one the Fresh profile pins: the image built from the working
tree rather than a published tag, no pre-existing volume, and a readiness poll gating the first
request. Querying before that gate races the startup wait page.

**Nothing here depends on what the bundled sources contain.** The search terms are made-up words that
appear only in the fixture, so every expected count is exactly what the fixture puts there.

**Each term sits in the scoped field of one quote and in the text of the other.** That is what proves the
scoping: `Xyzzysource` is the first quote's `source` and only words in the second quote's text, so a default
search finds both and a `field=source` search finds one. A search ignoring `field` would find two and fail.

## Steps

### 1. Create this test's own environment

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete .claude/temp/qt-api-01-out
dotnet script scripts/testing/test-env.csx -- create --name qt-api-01 --port 18101 --image quotinator:local
```

**Expected:** the app reports healthy, so the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Import this test's own quotes

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18101/api/v1/import --method POST --file docs/automated-testing/api-surface/fixtures/baseline-quotes.json --expect 200 --body-out .claude/temp/qt-api-01-out/import.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-01-out/import.json --equals summary.imported=2 --equals summary.errors=0
```

**Expected:** both quotes are imported, with no errors.

**On failure:** stop. Every search below expects these two quotes.

### 3. Check health

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18101/api/v1/health --expect 200 --body-out .claude/temp/qt-api-01-out/health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-01-out/health.json --equals status=healthy
```

**Expected:** `healthy`, not `starting`, which the same endpoint returns with `503` while the seed is
still running, and not `unhealthy`.

**On failure:** stop. Every later step reads the same container, so a degraded or still-initialising
app makes all of them meaningless rather than failing them individually.

### 4. Check the reported version

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18101/api/v1/version --expect 200 --body-out .claude/temp/qt-api-01-out/version.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-01-out/version.json --equals-xml version=Directory.Build.props#Version
```

**Expected:** the reported version is the one in `Directory.Build.props`. It is read from that file rather
than written into this document, so the step cannot go stale the next time the version is bumped.

**On failure:** **a missing `Directory.Build.props` in the build context silently produces `1.0.0`
while `/health` still returns healthy**, so a healthy container is not by itself evidence the build
context was complete. Stop and rebuild from a complete context; every later result is being read off
an image that is not the one under test.

### 5. Fetch a random quote

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18101/api/v1/quotes/random --expect 200 --body-out .claude/temp/qt-api-01-out/random.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-01-out/random.json --equals status=Ok --equals returnedCount=1 --not-empty items.0.quote
```

**Expected:** `status=Ok`, one quote returned, and a non-empty quote body. The endpoint returns `200` with
`status=NoResults` and an empty `items` array against an empty database, so the status code alone does
not establish that content is readable; the quote text is what does.

### 6. Search the default full-text path

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18101/api/v1/quotes/search?q=Xyzzysource" --expect 200 --body-out .claude/temp/qt-api-01-out/search-all.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-01-out/search-all.json --equals status=Ok --equals totalMatching=2
```

**Expected:** both of the fixture's quotes: one through its `source`, the other through its text.

### 7. Search scoped to `source`

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18101/api/v1/quotes/search?q=Xyzzysource&field=source" --expect 200 --body-out .claude/temp/qt-api-01-out/search-source.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-01-out/search-source.json --equals status=Ok --equals totalMatching=1 --equals "items.0.source=Xyzzysource Harbour"
```

**Expected:** only the quote whose `source` holds the term, not the one that only mentions it.

### 8. Search scoped to `author`

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18101/api/v1/quotes/search?q=Xyzzyauthor&field=author" --expect 200 --body-out .claude/temp/qt-api-01-out/search-author.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-01-out/search-author.json --equals status=Ok --equals totalMatching=1 --equals items.0.author=Xyzzyauthor
```

**Expected:** only the quote whose `author` holds the term.

### 9. Search scoped to `character`

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18101/api/v1/quotes/search?q=Xyzzycharacter&field=character" --expect 200 --body-out .claude/temp/qt-api-01-out/search-character.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-01-out/search-character.json --equals status=Ok --equals totalMatching=1 --equals items.0.character=Xyzzycharacter
dotnet script scripts/testing/http.csx -- --url "http://localhost:18101/api/v1/quotes/search?q=Xyzzyauthor&field=character" --expect 200 --body-out .claude/temp/qt-api-01-out/search-character-none.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-01-out/search-character-none.json --equals status=NoResults
```

**Expected:** the term in a `character` field finds that one quote. A term that exists only in other fields
reads `NoResults`, and the endpoint says so rather than erroring.

### 10. Search filtered by `type`

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18101/api/v1/quotes/search?q=Xyzzysource&type=person" --expect 200 --body-out .claude/temp/qt-api-01-out/search-person.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-01-out/search-person.json --equals status=Ok --equals totalMatching=1 --equals items.0.type=person
dotnet script scripts/testing/http.csx -- --url "http://localhost:18101/api/v1/quotes/search?q=Xyzzysource&type=tv" --expect 200 --body-out .claude/temp/qt-api-01-out/search-tv.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-01-out/search-tv.json --equals status=NoResults
```

**Expected:** `type=person` narrows the two matches to the one `person` quote, and `type=tv`, which neither
quote is, reads `NoResults`.

### 11. Nothing unexpected was thrown

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-01 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18101/api/v1/import --file docs/automated-testing/startup-and-degradation/fixtures/malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-01 --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** no exception has been logged by anything so far. The malformed upload then answers `422` and its
`JsonReaderException` is logged, which is the positive control: the same channel that read zero can see an
exception, so the zero means something.

**The count comes first, and that order is the point.** The upload throws more than the type its control
names, so counting afterwards would mean excluding what it throws, which also hides the same type from
anything else.

**On failure:** a count above zero is an issue, unless a Knowledgebase entry says otherwise. Report it with the
line that names it.

## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`.

| Request | Answer |
|---|---|
| `POST /api/v1/import`, the fixture | `200`, `summary.imported=2`, `summary.errors=0` |
| `GET /api/v1/health` | `200`, `status=healthy` |
| `GET /api/v1/version` | `200`, the version in `Directory.Build.props` |
| `GET /api/v1/quotes/random` | `200`, `status=Ok`, `returnedCount=1`, a non-empty quote |
| `search?q=Xyzzysource` | `200`, `status=Ok`, `totalMatching=2` |
| `search?q=Xyzzysource&field=source` | `200`, `totalMatching=1`, the quote with that `source` |
| `search?q=Xyzzyauthor&field=author` | `200`, `totalMatching=1`, the quote with that `author` |
| `search?q=Xyzzycharacter&field=character` | `200`, `totalMatching=1`, the quote with that `character` |
| `search?q=Xyzzyauthor&field=character` | `200`, `status=NoResults`, with a `message` |
| `search?q=Xyzzysource&type=person` | `200`, `totalMatching=1`, the `person` quote |
| `search?q=Xyzzysource&type=tv` | `200`, `status=NoResults` |
| `POST /api/v1/import`, a malformed file | `422` |

**What a healthy first boot logs.** In order: the `Quotinator starting` banner; Kestrel's `Now listening on`
lines; `[Changelog - Init]` and `[Changelog - Load]`; `[Database - Init]` creating the schema at baseline;
for each bundled file, a `[Database - Seed]` *importing* line and a *report* line; `[Database - Seed] seeding
complete`; `[Database - Stats]`; `[Server] listening on`; and the `Quotinator ready` banner. Kestrel binds
before the database work starts, so the wait page can answer during the seed.

**No exception was logged before the malformed upload.** That upload logged one `JsonReaderException` and one
`QuoteImportValidationException`. The second appears four times with one id, as the rethrow crosses each frame.

## Canary

No commit precedes this document's subject. Health, version, random and search are the application's
oldest contract, so there is no pre-fix build to run, and the control is inside the run instead.

It is already there: steps 6 to 9 assert presence and steps 9 and 10 assert absence, over the same
endpoints in the same container, and each scoped search has to find fewer than the default search does.
An endpoint answering nothing at all fails the presence assertions, one matching everything fails the
absence assertions, and one ignoring its `field` fails the scoped counts. Neither failure can pass this
document, which is what a canary has to establish.

So they are run together. A run that stops after step 8 has asserted only that the endpoints answer, not
that they discriminate.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-01
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete .claude/temp/qt-api-01-out
```

**Expected:** the container, its volume and the output directory are gone.
