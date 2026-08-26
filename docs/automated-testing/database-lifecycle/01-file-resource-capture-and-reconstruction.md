# Captured source files are recorded with provenance and reconstruct byte-for-byte

**Smoke:** no
**Environment:** Fresh
**Traces to:** #251, #252

## Preconditions

Nothing beyond the Fresh profile — the capture happens during startup, so what this test inspects *is*
the profile's own first boot, and a container that has not finished seeding has nothing to inspect.

`Quotinator.Tools.DbInspector` (read-only) is used for the provenance checks that need a raw SQL join.

## Determinism

- **Waits for health, not a duration.** The capture completes as part of seeding; polling `/health`
  is what establishes that, not a guessed interval.
- **Copy the `-wal` and `-shm` sidecars alongside the `.db` file.** SQLite does not auto-checkpoint
  recent writes back into the main file until the WAL grows past its threshold, so copying the `.db`
  alone can read a database that is missing everything this test just wrote.
- **Fresh container, first boot only.** The `origin=system` count and the `prunedCount: 0` result both
  depend on exactly one startup having happened. A reused volume gives a second captured version per
  file and breaks both — which is why the profile recreates its volume rather than reusing one.
- **The bundled file names are illustrative.** Check the captured rows against `manifest.json` rather
  than against the list this document happens to name.
- **The batch-link figures are relationships that hold whatever the bundled file set contains** — do
  not substitute the count you happen to observe.
- **On the detail endpoint the three agreeing is the assertion; the value they agree on is data.**

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-db-01 --port 18301
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Confirm the profile finished seeding

```powershell
([regex]::Matches((docker logs qt-db-01 2>&1 | Out-String), 'Quotinator ready')).Count
```

**Expected:** `1`. Seeding, and the file capture that happens as part of it, are complete.

Counted rather than eyeballed: reading the tail of a log and deciding whether it looks finished is not
a condition that can fail.

**On failure:** a container still initialising has nothing to inspect, and every check below would read
a half-built capture. Wait for seeding to finish rather than continuing.

### 3. Confirm all bundled files were captured with correct provenance

```powershell
docker cp qt-db-01:/data/quotinatordata.db .claude/temp/smoke251.db
docker cp qt-db-01:/data/quotinatordata.db-wal .claude/temp/smoke251.db-wal 2>$null
docker cp qt-db-01:/data/quotinatordata.db-shm .claude/temp/smoke251.db-shm 2>$null

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke251.db `
  --sql "SELECT Id, FileName, Origin, HomeDirectoryKey, LineEnding, EndsWithTrailingNewline, Converter, ConverterOptions FROM Import_FileResource WHERE IsDeleted = 0 ORDER BY FileName"
```

The two sidecar copies are allowed to fail — a database whose WAL has already been checkpointed has no
`-wal` file, and that is not an error. `2>$null` keeps that from reading as one.

**Expected:** one row per bundled source file plus `manifest.json` itself, each with `Origin = System`
and `HomeDirectoryKey = sources`. At the time of writing that is
`NikhilNamal17_popular-movie-quotes.json`, `quotinator-curated.json`,
`quotinator-series-universe.json` and `vilaboim_movie-quotes.json`.
`NikhilNamal17_popular-movie-quotes.json` shows `Converter = basic-json-array` with its full
`ConverterOptions` JSON; `vilaboim_movie-quotes.json` shows `Converter = regex-array` with its own
options; the other three — `manifest.json` included — show `NULL` for both, having no `converter` entry
in the manifest.

### 4. Confirm `manifest.json` links to every batch it drove

Not just the two whose files were never redirected to the download cache — the #251 follow-up bug in
`SeedBatch.SourceDirectory`:

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke251.db `
  --sql "SELECT fr.FileName, COUNT(frb.Id) AS BatchLinks FROM Import_FileResource fr LEFT JOIN Import_FileResourceBatch frb ON frb.FileResourceId = fr.Id WHERE fr.IsDeleted = 0 GROUP BY fr.Id ORDER BY fr.FileName"
```

**Expected:** `manifest.json`'s `BatchLinks` equals the number of seed batches, because it drove every
one of them; every other row shows `1`, having driven only itself.

### 5. List the captured file resources

```powershell
$resources = (Invoke-RestMethod "http://localhost:18301/api/v1/import/file-resources").items
$resources | Select-Object fileName, origin, homeDirectoryKey, linkedBatchCount | Format-Table
@($resources | Where-Object { $_.PSObject.Properties.Name -contains 'linkedBatchIds' }).Count
```

**Expected:** each item carries `homeDirectoryKey` (`sources` for bundled rows) and `linkedBatchCount`,
and the final line reports `0` — the list shape does **not** include `linkedBatchIds`, which belongs to
the detail endpoint only.

### 6. Reject an unknown `origin`

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/file-resources?origin=bogus" --expect 422 --status
```

**Expected:** `422`.

### 7. Filter the list to `origin=system`

```powershell
$system = (Invoke-RestMethod "http://localhost:18301/api/v1/import/file-resources?origin=system").items
"system=$(@($system).Count) allBundled=$(@($resources).Count) other=$(@($system | Where-Object { $_.origin -ne 'system' }).Count)"
```

**Expected:** `system` equals `allBundled` from step 5 and `other` is `0` — on a fresh container every
captured row is bundled, so nothing is `user` or `upload` origin.

### 8. Fetch one file resource's detail

```powershell
$manifestId = ($resources | Where-Object { $_.fileName -eq 'manifest.json' }).id
$curatedId  = ($resources | Where-Object { $_.fileName -eq 'quotinator-curated.json' }).id
"manifestId=$manifestId curatedId=$curatedId"

$detail = Invoke-RestMethod "http://localhost:18301/api/v1/import/file-resources/$manifestId"
"linkedBatchCount=$($detail.linkedBatchCount) linkedBatchIds=$(@($detail.linkedBatchIds).Count)"
```

**Expected:** `linkedBatchCount` and the length of `linkedBatchIds` are equal, and both match the
`BatchLinks` figure step 4 reported for `manifest.json`.

### 9. List the seed batches

```powershell
(Invoke-RestMethod "http://localhost:18301/api/v1/import/batches?type=seed").totalCount
```

**Expected:** one seed batch per bundled file, matching the `BatchLinks` figure step 4 reported rather
than a fixed number.

### 10. Reject an unknown batch `status`

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/batches?status=bogus" --expect 422 --status
```

**Expected:** `422`.

### 11. Fetch one of the linked batches

Every batch id from the file-resource detail must exist here:

```powershell
$linkedBatchId = $detail.linkedBatchIds[0]
$linkedBatchId
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/batches/$linkedBatchId" --expect 200 --status
```

**Expected:** `200`, proving the FileResource detail and the batches endpoint agree on what exists.

### 12. Reconstruct a captured file byte-for-byte

```powershell
Invoke-WebRequest "http://localhost:18301/api/v1/import/file-resources/$curatedId/download" `
  -OutFile .claude/temp/downloaded.json -UseBasicParsing
docker cp qt-db-01:/app/data/sources/quotinator-curated.json .claude/temp/original.json

(Get-FileHash .claude/temp/downloaded.json).Hash -eq (Get-FileHash .claude/temp/original.json).Hash
```

**Expected:** `True`. No `X-Api-Key` required; it is a read-only endpoint. Compared by hash rather than
by eye, so a single differing byte fails rather than being scrolled past.

### 13. Override the line ending to CRLF

```powershell
Invoke-WebRequest "http://localhost:18301/api/v1/import/file-resources/$curatedId/download?lineEnding=crlf" `
  -OutFile .claude/temp/crlf.json -UseBasicParsing

$crlfBytes     = [IO.File]::ReadAllBytes("$PWD\.claude\temp\crlf.json")
$originalBytes = [IO.File]::ReadAllBytes("$PWD\.claude\temp\original.json")
function Count-Crlf($bytes) {
  $n = 0
  for ($i = 0; $i -lt $bytes.Length - 1; $i++) { if ($bytes[$i] -eq 13 -and $bytes[$i + 1] -eq 10) { $n++ } }
  $n
}
"crlf=$(Count-Crlf $crlfBytes) original=$(Count-Crlf $originalBytes)"
```

**Expected:** `crlf` is non-zero and `original` is `0` — the override introduced `0d0a` sequences into
a file captured as bare `LF`. The original is counted alongside as the positive control: without it, a
non-zero count could just as easily mean the file always had CRLF.

### 14. Download an unknown file resource

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/file-resources/00000000-0000-0000-0000-000000000000/download" --expect 404 --status
```

**Expected:** `404`.

### 15. Download with an invalid `lineEnding`

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/file-resources/$curatedId/download?lineEnding=bogus" --expect 422 --status
```

**Expected:** `422`.

### 16. Prune without an admin key

```powershell
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18301/api/v1/import/file-resources/prune" --no-key --expect 401 --status
```

**Expected:** `401`.

### 17. Prune with a malformed `keepPerFile`

```powershell
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18301/api/v1/import/file-resources/prune?keepPerFile=abc" --expect 422 --status
```

**Expected:** `422`.

### 18. Prune with a valid key

```powershell
(dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18301/api/v1/import/file-resources/prune" --expect 200 | ConvertFrom-Json).prunedCount
```

**Expected:** `0` — nothing to prune, since each bundled file has only one captured version after a
single startup.

**On failure:** a non-zero `prunedCount` means more than one startup wrote captured versions — a reused
volume rather than a prune defect, see Determinism. Start again from a fresh volume rather than
recording this as a result.

## Observed effect

Partially established. The provenance rows, batch-link counts and reconstructed bytes are all observed
state and are asserted above. What the container logs during capture has not been captured itself.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-01
Remove-Item .claude/temp/smoke251.db, .claude/temp/smoke251.db-wal, .claude/temp/smoke251.db-shm, `
            .claude/temp/downloaded.json, .claude/temp/original.json, .claude/temp/crlf.json `
            -ErrorAction SilentlyContinue
```
