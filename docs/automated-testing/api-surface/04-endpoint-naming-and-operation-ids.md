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
- **The spec is read as an object, never matched as text.** Every assertion below enumerates the
  operations the document actually declares, so no assertion can depend on how the JSON is formatted.
  This is not a stylistic choice: every pattern here was once written `"operationId":"X"` against a
  pretty-printed spec that says `"operationId": "X"`, and the removal half passed for months on a
  pattern that could never have matched anything.
- **Case-sensitive comparisons** (`-ceq`, `-cmatch`). `-eq` and `-match` are case-insensitive in
  PowerShell, and step 4 exists precisely to tell `by ID` from `by id`.
- The negative assertions matter as much as the positive ones: the old operationIds must be absent
  from the **whole** spec, not merely absent from the two endpoints that were renamed.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-api-04 --port 18104
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Fetch the published spec and confirm it is a real document

```powershell
$spec = Invoke-RestMethod "http://localhost:18104/openapi/v1.json"
$operations = foreach ($path in $spec.paths.PSObject.Properties) {
  foreach ($verb in $path.Value.PSObject.Properties) { $verb.Value }
}
"operations = $(@($operations).Count)"
```

**Expected:** a non-zero count — 52 at the time of writing, but the assertion is "the spec parsed and
declares operations", not the figure.

**On failure:** a fetch during initialisation returns the wait page, which is not JSON and fails to
parse here rather than three steps later. Every check below reads `$operations`, so a zero would make
each of them report an absence that is really a missing document. Stop.

### 3. Count all four operationIds in one pass — the two new, and the two they replaced

```powershell
foreach ($id in 'GetAllImportBatches', 'GetAllFileResources', 'GetImportBatches', 'GetFileResources') {
  "$id $(@($operations | Where-Object { $_.operationId -ceq $id }).Count)"
}
```

**Expected:** `GetAllImportBatches 1`, `GetAllFileResources 1`, `GetImportBatches 0`,
`GetFileResources 0`.

**The two `1`s are the positive control for the two `0`s**, and that is why all four are counted by
one construction rather than in separate commands. A lookup that cannot match anything reports `0`
for a removed name just as confidently as a genuinely removed name does, and only a name the same
construction *does* find separates them — see the index's *A removed or added feature needs its own
proof*.

**On failure:** all four reading `0` means `$operations` is empty, not that the renames are missing.
Check step 2's count first.

### 4. Count the List-endpoint summaries, required and forbidden together

```powershell
foreach ($s in 'List people', 'List quotes', 'List series',
                'All people (paginated)', 'All quotes (paginated)', 'List Series') {
  "$s $(@($operations | Where-Object { $_.summary -ceq $s }).Count)"
}
```

**Expected:** the first three each report `1`; the last three each report `0`. Every List-endpoint
`summary` reads `"List x"` with a lowercase plural noun, and none of the pre-standard forms survives.

The first three are the last three's positive control, for the same reason as step 3. `List Series`
differs from `List series` only in case, which is why the comparison is `-ceq`: with `-eq` this
assertion would pass no matter which of the two the spec carried.

### 5. Fetch a quote by id and read its log tag

```powershell
$id = (Invoke-RestMethod "http://localhost:18104/api/v1/quotes/random").items[0].id
$id
Invoke-RestMethod "http://localhost:18104/api/v1/quotes/$id" | Out-Null
$log = docker logs qt-api-04 2>&1 | Out-String
"[Api - GetQuoteById] $(([regex]::Matches($log, '\[Api - GetQuoteById\]')).Count)"
"[Api - GetById]      $(([regex]::Matches($log, '\[Api - GetById\]')).Count)"
```

**Expected:** one or more `[Api - GetQuoteById]`, and exactly `0` `[Api - GetById]` — the old,
already-mismatched tag.

Both tags are counted together for the same reason step 3 counts all four operationIds: the absence of
the old one means nothing unless the same command is shown finding the new one. The id is captured into
a variable rather than left as a `<id>` placeholder, so the step runs unattended. Occurrences are
counted with `[regex]::Matches` rather than by piping to `Select-String`, which returns one result per
matching *line* and would report `1` for any number of hits on the same line.

### 6. Count the GetById summaries, capitalised against lowercase

```powershell
"by ID $(@($operations | Where-Object { $_.summary -cmatch ' by ID$' }).Count)"
"by id $(@($operations | Where-Object { $_.summary -cmatch ' by id$' }).Count)"
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

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-04
```
