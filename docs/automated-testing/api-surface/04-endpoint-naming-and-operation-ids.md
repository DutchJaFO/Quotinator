# Endpoint names and summaries follow the standard, including the breaking operationId renames

**Smoke:** no
**Environment:** Fresh
**Traces to:** #279

## Preconditions

Nothing beyond the Fresh profile.

An `operationId` becomes part of the published OpenAPI spec, which a generated client can depend on —
renaming one is a breaking change. This test confirms the renames landed and the old values are gone
everywhere, not just where they were edited.

The spec must be fetched after startup has finished — a fetch during initialisation returns the wait
page, not the spec. The profile's readiness poll is what gates that.

## Determinism

- **Named container** (`qt-api-04`), so the log assertion at the end reads the right container's output.
- **Waits for health, not for a duration.** This previously used `sleep 15`, a guess that would fail on
  a slower machine for a reason unrelated to what the test verifies, and waste time on a faster one.
- The negative assertions matter as much as the positive ones: the old operationIds must be absent
  from the **whole** spec, not merely absent from the two endpoints that were renamed.

## Steps

### 1. Create this test's own environment

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-api-04 --port 18104
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Fetch the published spec and confirm it is a real document

```bash
curl -s "http://localhost:18104/openapi/v1.json" > /tmp/spec279.json
wc -c < /tmp/spec279.json
```

**Expected:** a non-zero byte count — around 176,000 at the time of writing, but the assertion is
"not empty", not the figure.

**On failure:** an empty or missing file is what a fetch during initialisation produces, and every
check below reads it. A zero here would make each of them report an absence that is really a missing
document. Stop.

### 3. Count all four operationIds in one pass — the two new, and the two they replaced

```bash
for id in GetAllImportBatches GetAllFileResources GetImportBatches GetFileResources; do
  printf '%s %s\n' "$id" \
    "$(grep -oE "\"operationId\":[[:space:]]*\"$id\"" /tmp/spec279.json | wc -l)"
done
```

**Expected:** `GetAllImportBatches 1`, `GetAllFileResources 1`, `GetImportBatches 0`,
`GetFileResources 0`.

**The two `1`s are the positive control for the two `0`s**, and that is why all four are counted by
one construction rather than in separate commands. A pattern that cannot match anything reports `0`
for a removed name just as confidently as a genuinely removed name does, and only a name the same
pattern *does* find separates them. Found exactly that way during #339's full run: every pattern here
was written `"operationId":"X"` against a spec that is pretty-printed `"operationId": "X"`, so the
removal half had been passing on a pattern that could never match — see the index's *A removed or
added feature needs its own proof*.

`[[:space:]]*` rather than a literal space, so a change in how the spec is formatted cannot silently
re-break this the same way.

**On failure:** all four reading `0` means the pattern or the file is wrong, not that the renames
are missing. Check step 2's byte count first.

### 4. Count the List-endpoint summaries, required and forbidden together

```bash
for s in "List people" "List quotes" "List series"; do
  printf '%s %s\n' "$s" "$(grep -oE "\"summary\":[[:space:]]*\"$s\"" /tmp/spec279.json | wc -l)"
done
for s in "All people \(paginated\)" "All quotes \(paginated\)" "List Series"; do
  printf '%s %s\n' "$s" "$(grep -oE "\"summary\":[[:space:]]*\"$s\"" /tmp/spec279.json | wc -l)"
done
```

**Expected:** the first three each report `1`; the last three each report `0`. Every List-endpoint
`summary` reads `"List x"` with a lowercase plural noun, and none of the pre-standard forms survives.

The first loop is the second loop's positive control, for the same reason as step 3. The parentheses
are escaped because these patterns are extended regular expressions.

### 5. Fetch a quote by id and read its log tag

```bash
id=$(curl -s "http://localhost:18104/api/v1/quotes/random" \
     | grep -oE '"id":[[:space:]]*"[a-f0-9-]+"' | head -1 | cut -d'"' -f4)
curl -s "http://localhost:18104/api/v1/quotes/$id" > /dev/null
docker logs qt-api-04 2>&1 | grep -oE "\[Api - GetQuoteById\]|\[Api - GetById\]" | sort | uniq -c
```

**Expected:** one or more `[Api - GetQuoteById]`, and **no** `[Api - GetById]` — the old,
already-mismatched tag.

Both tags are counted together for the same reason step 3 counts all four operationIds: the absence of
the old one means nothing unless the same command is shown finding the new one. The id is extracted
into a variable rather than left as a `<id>` placeholder, so the step runs unattended.

### 6. Count the GetById summaries, capitalised against lowercase

```bash
grep -oE '"summary":[[:space:]]*"[A-Za-z ]+ by ID"' /tmp/spec279.json | wc -l
grep -oE '"summary":[[:space:]]*"[A-Za-z ]+ by id"' /tmp/spec279.json | wc -l
```

**Expected:** the first count is non-zero — 11 at the time of writing, but the assertion is that
GetById summaries exist and are found — and the second is exactly `0`. Every GetById summary reads
`"X by ID"` with a capitalised `ID`.

**This is asserted against the published spec rather than the rendered Scalar page**, and the change
is deliberate. Scalar renders these strings straight from the spec, so a spec-level count tests the
same claim while running unattended; the previous step asked a person to open a browser and eyeball
"a few" operations, which is neither repeatable nor able to fail on the ones they did not look at.

The rendered page was confirmed once, during #339's full run: `/scalar/v1` was loaded in a browser,
all 13 operation groups expanded, and the DOM read — 11 `X by ID` summaries, 0 lowercase. That
establishes Scalar does not transform the text. Nothing here needs to re-establish it every run.

## Observed effect

Partially established: the log line is itself an observed effect and is asserted above. The rest of
what the container emits while serving these requests has not been captured.

## Cleanup

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-04
rm /tmp/spec279.json
```
