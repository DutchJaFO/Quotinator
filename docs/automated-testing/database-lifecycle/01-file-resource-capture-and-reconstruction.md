# Captured source files are recorded with provenance and reconstruct byte-for-byte

**Smoke:** no
**Traces to:** #251, #252

## Preconditions

A container started fresh and allowed to seed normally — the capture happens during startup, so a
container that has not finished seeding has nothing to inspect.

`Quotinator.Tools.DbInspector` (read-only) is used for the provenance checks that need a raw SQL join.

## Determinism

- **Waits for health, not a duration.** The capture completes as part of seeding; polling `/health`
  is what establishes that, not a guessed interval.
- **Copy the `-wal` and `-shm` sidecars alongside the `.db` file.** SQLite does not auto-checkpoint
  recent writes back into the main file until the WAL grows past its threshold, so copying the `.db`
  alone can read a database that is missing everything this test just wrote.
- **Fresh container, no volume.** The `origin=system` count and the `prunedCount: 0` result both
  depend on exactly one startup having happened. A reused volume gives a second captured version per
  file and breaks both.
- Port `18099` is used rather than 8080 so this can run alongside another container.

## Steps

**Start and let it seed:**

```bash
docker run -d --name smoke251 -p 18099:8099 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
until curl -sf http://localhost:18099/api/v1/health > /dev/null; do sleep 1; done
docker logs smoke251 2>&1 | tail -5
```

**Confirm all bundled files were captured with correct provenance:**

```bash
MSYS_NO_PATHCONV=1 docker cp smoke251:/app/data/quotinatordata.db .claude/temp/smoke251.db
MSYS_NO_PATHCONV=1 docker cp smoke251:/app/data/quotinatordata.db-wal .claude/temp/smoke251.db-wal
MSYS_NO_PATHCONV=1 docker cp smoke251:/app/data/quotinatordata.db-shm .claude/temp/smoke251.db-shm
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke251.db \
  --sql "SELECT Id, FileName, Origin, HomeDirectoryKey, LineEnding, EndsWithTrailingNewline, Converter, ConverterOptions FROM Import_FileResource WHERE IsDeleted = 0 ORDER BY FileName"
```

**Confirm `manifest.json` links to all four batches it drove**, not just the two whose files were never
redirected to the download cache — the #251 follow-up bug in `SeedBatch.SourceDirectory`:

```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke251.db \
  --sql "SELECT fr.FileName, COUNT(frb.Id) AS BatchLinks FROM Import_FileResource fr LEFT JOIN Import_FileResourceBatch frb ON frb.FileResourceId = fr.Id WHERE fr.IsDeleted = 0 GROUP BY fr.Id ORDER BY fr.FileName"
```

**List endpoint, filterable by `origin`:**

```bash
curl -s "http://localhost:18099/api/v1/import/file-resources"
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:18099/api/v1/import/file-resources?origin=bogus"
curl -s "http://localhost:18099/api/v1/import/file-resources?origin=system"
```

**Detail endpoint** — substitute the `manifest.json` id from the provenance check:

```bash
curl -s "http://localhost:18099/api/v1/import/file-resources/<manifest-id>"
```

**Batches list/detail** — every batch id from the detail above must exist here:

```bash
curl -s "http://localhost:18099/api/v1/import/batches?type=seed"
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:18099/api/v1/import/batches?status=bogus"
curl -s "http://localhost:18099/api/v1/import/batches/<one-of-the-linkedBatchIds-above>"
```

**Byte-exact reconstruction:**

```bash
curl -s "http://localhost:18099/api/v1/import/file-resources/<id>/download" -o .claude/temp/downloaded.json
MSYS_NO_PATHCONV=1 docker cp smoke251:/app/data/sources/quotinator-curated.json .claude/temp/original.json
diff .claude/temp/downloaded.json .claude/temp/original.json && echo IDENTICAL
```

**`lineEnding` override** — confirmed via hex dump, not word count:

```bash
curl -s "http://localhost:18099/api/v1/import/file-resources/<id>/download?lineEnding=crlf" -o .claude/temp/crlf.json
xxd .claude/temp/crlf.json | head -3
```

**Error cases and prune auth/validation:**

```bash
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:18099/api/v1/import/file-resources/00000000-0000-0000-0000-000000000000/download"
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:18099/api/v1/import/file-resources/<id>/download?lineEnding=bogus"
curl -s -o /dev/null -w "%{http_code}\n" -X POST "http://localhost:18099/api/v1/import/file-resources/prune"
curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:18099/api/v1/import/file-resources/prune?keepPerFile=abc"
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:18099/api/v1/import/file-resources/prune"
```

## Expected output

**Provenance** — one row per bundled source file plus `manifest.json` itself, each with
`Origin = System` and `HomeDirectoryKey = sources`. At the time of writing that is
`NikhilNamal17_popular-movie-quotes.json`, `quotinator-curated.json`,
`quotinator-series-universe.json` and `vilaboim_movie-quotes.json` — check against `manifest.json`
rather than against that list, which is illustrative.
`NikhilNamal17_popular-movie-quotes.json` shows `Converter = basic-json-array` with its full
`ConverterOptions` JSON; `vilaboim_movie-quotes.json` shows `Converter = regex-array` with its own
options; the other three — `manifest.json` included — show `NULL` for both, having no `converter` entry
in the manifest.

**Batch links** — `manifest.json`'s `BatchLinks` equals the number of seed batches, because it drove
every one of them; every other row shows `1`, having driven only itself. Both are relationships that
hold whatever the bundled file set contains — do not substitute the count you happen to observe.

**List** — each item includes `homeDirectoryKey` (`"sources"` for bundled rows) and `linkedBatchCount`,
but **no** `linkedBatchIds` key. `origin=bogus` returns `422`. `origin=system` reports one row per
bundled source file plus the manifest, and none are `user` or `upload` origin on a fresh container.

**Detail** — `linkedBatchCount` and the length of `linkedBatchIds` both equal the `BatchLinks` figure
the raw SQL join reported. The three agreeing is the assertion; the value they agree on is data.

**Batches** — `type=seed` reports one seed batch per bundled file, matching the `BatchLinks` figure
above rather than a fixed number. `status=bogus`
returns `422`. The batch detail returns `200`, proving the FileResource detail and the batches endpoint
agree on what exists.

**Download** — prints `IDENTICAL`. No `X-Api-Key` required; it is a read-only endpoint.

**`lineEnding=crlf`** — the hex dump shows `0d0a` sequences even though the file was captured as bare
`LF`.

**Error cases**, in order: `404` (unknown id), `422` (invalid `lineEnding`), `401` (no key), `422`
(malformed `keepPerFile`), then `200` with `{"prunedCount":0}` — nothing to prune, since each bundled
file has only one captured version after a single startup.

## Observed effect

Partially established. The provenance rows, batch-link counts and reconstructed bytes are all observed
state and are asserted above. What the container logs during capture has not been captured itself.

## Cleanup

```bash
docker rm -f smoke251
rm -f .claude/temp/smoke251.db .claude/temp/smoke251.db-wal .claude/temp/smoke251.db-shm \
      .claude/temp/downloaded.json .claude/temp/original.json .claude/temp/crlf.json
```
