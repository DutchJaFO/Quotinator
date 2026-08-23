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

```bash
docker rm -f qt-db-01 2>/dev/null; docker volume rm qt-db-01-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-db-01 -p 18301:8080 -v qt-db-01-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true \
  quotinator:local
until curl -sf http://localhost:18301/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Confirm the profile finished seeding

```bash
docker logs qt-db-01 2>&1 | grep -c "Quotinator ready"
```

**Expected:** `1`. Seeding, and the file capture that happens as part of it, are complete.

Counted rather than eyeballed: a `tail` of the log is read by a human deciding whether it looks
finished, which is not a condition that can fail.

**On failure:** a container still initialising has nothing to inspect, and every check below would read
a half-built capture. Wait for seeding to finish rather than continuing.

### 3. Confirm all bundled files were captured with correct provenance

```bash
MSYS_NO_PATHCONV=1 docker cp qt-db-01:/data/quotinatordata.db .claude/temp/smoke251.db
MSYS_NO_PATHCONV=1 docker cp qt-db-01:/data/quotinatordata.db-wal .claude/temp/smoke251.db-wal
MSYS_NO_PATHCONV=1 docker cp qt-db-01:/data/quotinatordata.db-shm .claude/temp/smoke251.db-shm
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke251.db \
  --sql "SELECT Id, FileName, Origin, HomeDirectoryKey, LineEnding, EndsWithTrailingNewline, Converter, ConverterOptions FROM Import_FileResource WHERE IsDeleted = 0 ORDER BY FileName"
```

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

```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke251.db \
  --sql "SELECT fr.FileName, COUNT(frb.Id) AS BatchLinks FROM Import_FileResource fr LEFT JOIN Import_FileResourceBatch frb ON frb.FileResourceId = fr.Id WHERE fr.IsDeleted = 0 GROUP BY fr.Id ORDER BY fr.FileName"
```

**Expected:** `manifest.json`'s `BatchLinks` equals the number of seed batches, because it drove every
one of them; every other row shows `1`, having driven only itself.

### 5. List the captured file resources

```bash
curl -s "http://localhost:18301/api/v1/import/file-resources"
```

**Expected:** each item includes `homeDirectoryKey` (`"sources"` for bundled rows) and
`linkedBatchCount`, but **no** `linkedBatchIds` key.

### 6. Reject an unknown `origin`

```bash
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:18301/api/v1/import/file-resources?origin=bogus"
```

**Expected:** `422`.

### 7. Filter the list to `origin=system`

```bash
curl -s "http://localhost:18301/api/v1/import/file-resources?origin=system"
```

**Expected:** one row per bundled source file plus the manifest, and none are `user` or `upload` origin
on a fresh container.

### 8. Fetch one file resource's detail

Substitute the `manifest.json` id from the provenance check:

```bash
curl -s "http://localhost:18301/api/v1/import/file-resources/<manifest-id>"
```

**Expected:** `linkedBatchCount` and the length of `linkedBatchIds` both equal the `BatchLinks` figure
the batch-links query reported.

### 9. List the seed batches

```bash
curl -s "http://localhost:18301/api/v1/import/batches?type=seed"
```

**Expected:** one seed batch per bundled file, matching the `BatchLinks` figure the batch-links query
reported rather than a fixed number.

### 10. Reject an unknown batch `status`

```bash
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:18301/api/v1/import/batches?status=bogus"
```

**Expected:** `422`.

### 11. Fetch one of the linked batches

Every batch id from the file-resource detail must exist here:

```bash
curl -s "http://localhost:18301/api/v1/import/batches/<one-of-the-linkedBatchIds-above>"
```

**Expected:** `200`, proving the FileResource detail and the batches endpoint agree on what exists.

### 12. Reconstruct a captured file byte-for-byte

```bash
curl -s "http://localhost:18301/api/v1/import/file-resources/<id>/download" -o .claude/temp/downloaded.json
MSYS_NO_PATHCONV=1 docker cp qt-db-01:/app/data/sources/quotinator-curated.json .claude/temp/original.json
diff .claude/temp/downloaded.json .claude/temp/original.json && echo IDENTICAL
```

**Expected:** prints `IDENTICAL`. No `X-Api-Key` required; it is a read-only endpoint.

### 13. Override the line ending to CRLF

Confirmed via hex dump, not word count:

```bash
curl -s "http://localhost:18301/api/v1/import/file-resources/<id>/download?lineEnding=crlf" -o .claude/temp/crlf.json
xxd .claude/temp/crlf.json | head -3
```

**Expected:** the hex dump shows `0d0a` sequences even though the file was captured as bare `LF`.

### 14. Download an unknown file resource

```bash
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:18301/api/v1/import/file-resources/00000000-0000-0000-0000-000000000000/download"
```

**Expected:** `404`.

### 15. Download with an invalid `lineEnding`

```bash
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:18301/api/v1/import/file-resources/<id>/download?lineEnding=bogus"
```

**Expected:** `422`.

### 16. Prune without an admin key

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST "http://localhost:18301/api/v1/import/file-resources/prune"
```

**Expected:** `401`.

### 17. Prune with a malformed `keepPerFile`

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:18301/api/v1/import/file-resources/prune?keepPerFile=abc"
```

**Expected:** `422`.

### 18. Prune with a valid key

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:18301/api/v1/import/file-resources/prune"
```

**Expected:** `200` with `{"prunedCount":0}` — nothing to prune, since each bundled file has only one
captured version after a single startup.

**On failure:** a non-zero `prunedCount` means more than one startup wrote captured versions — a reused
volume rather than a prune defect, see Determinism. Start again from a fresh volume rather than
recording this as a result.

## Observed effect

Partially established. The provenance rows, batch-link counts and reconstructed bytes are all observed
state and are asserted above. What the container logs during capture has not been captured itself.

## Cleanup

```bash
docker rm -f qt-db-01 2>/dev/null
docker volume rm qt-db-01-data 2>/dev/null
rm -f .claude/temp/smoke251.db .claude/temp/smoke251.db-wal .claude/temp/smoke251.db-shm \
      .claude/temp/downloaded.json .claude/temp/original.json .claude/temp/crlf.json
```
