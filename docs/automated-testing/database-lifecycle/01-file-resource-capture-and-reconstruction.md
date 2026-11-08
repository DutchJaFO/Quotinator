# Captured source files are recorded with provenance and reconstruct byte-for-byte

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #251, #252

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-db-01`, publishing `18301`, on a bind
directory, so the database file can be read from the host once the container has stopped. The capture
happens during startup, so what this test inspects *is* the profile's own first boot, and a container
that has not finished seeding has nothing to inspect.

`Quotinator.Tools.DbInspector` (read-only) reads the provenance and batch links that no endpoint
reports, and writes them as JSON (`--json-out`) so they can be compared with what the API and the
manifest say.

## Determinism

- **Fresh container, first boot only.** The `prunedCount` of `0` depends on exactly one startup having
  captured each file, which is why the bind directory is deleted before the container is created.
- **No bundled file is named.** The data this test inspects is whatever the bundled seed captured, so
  every assertion relates one reading to another instead of to a list: the captured rows to
  `data/sources/manifest.json` (one per file entry, plus the manifest itself; one recorded converter per
  entry that declares one), the API to the database, and the batch links to the seed batches. A file
  added to or removed from the manifest changes the figures and not the document. The one name used is
  `manifest.json`, which the capture records under that name by design.
- **The reconstruction is of `manifest.json`**, compared with the copy inside the image it was captured
  from, so the comparison does not depend on which data files are bundled.
- **The database is read after the container has stopped**, so the file is checkpointed and nothing holds
  it. The log is read before each stop.
- **On the detail endpoint the three figures agreeing is the assertion; the value they agree on is data.**

## Steps

### 1. Create this test's own environment

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-db-01 --delete C:\repos\Quotinator\.claude\temp\qt-db-01-out --create-directory C:\repos\Quotinator\.claude\temp\qt-db-01 --create-directory C:\repos\Quotinator\.claude\temp\qt-db-01-out\original
dotnet script scripts/testing/test-env.csx -- create --name qt-db-01 --port 18301 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-db-01
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-01 --pattern "Quotinator ready" --exactly 1
```

**Expected:** the app reports healthy, and the log records exactly one completed start, so the bundled
seed and the capture that is part of it have finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never finished seeding.

### 2. The list carries provenance and a batch count, but no batch ids

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/file-resources?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-01-out\list.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-01-out\list.json --greater-than totalCount=0 --length-equals-field items=totalCount
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-01-out\list.json --where items:origin=system --where items:homeDirectoryKey=sources --where items:linkedBatchCount --length-equals-field items=totalCount
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-01-out\list.json --where items:linkedBatchIds --length-equals items=0
```

**Expected:** every command exits `0`: captured files are listed, every one bundled (`origin=system`,
`homeDirectoryKey=sources`) and carrying `linkedBatchCount`, and none carrying `linkedBatchIds`, which
belongs to the detail endpoint only.

**The last `0` is evidence because step 4's detail response carries `linkedBatchIds`**, so the field
exists to be found.

### 3. Filter by `origin`, and reject an unknown one

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/file-resources?origin=bogus" --expect 422 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/file-resources?origin=system&pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-01-out\system.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-01-out\system.json --equals-json totalCount=C:\repos\Quotinator\.claude\temp\qt-db-01-out\list.json#totalCount --where items:origin=system --length-equals-field items=totalCount
```

**Expected:** `422` for the unknown origin, and the `system` filter returns every captured row, each
`system`: on a fresh container nothing is `user` or `upload` origin.

### 4. The manifest's detail agrees with the seed batches

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/file-resources?fileName=manifest.json" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-01-out\manifest-list.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-01-out\manifest-list.json --length-equals items=1
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18301/api/v1/import/file-resources/{id}" --value id=C:\repos\Quotinator\.claude\temp\qt-db-01-out\manifest-list.json#items.0.id --out C:\repos\Quotinator\.claude\temp\qt-db-01-out\detail-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-db-01-out\detail-url.txt --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-01-out\detail.json
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/batches?type=seed&pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-01-out\batches.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-01-out\detail.json --length-equals-field linkedBatchIds=linkedBatchCount --equals-json linkedBatchCount=C:\repos\Quotinator\.claude\temp\qt-db-01-out\batches.json#totalCount --greater-than linkedBatchCount=0
```

**Expected:** every command exits `0`: one captured `manifest.json`, whose detail lists as many batch ids
as its `linkedBatchCount`, and that count is the number of seed batches, since the manifest drove every
one of them.

### 5. Reject an unknown batch `status`, and find a linked batch

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/batches?status=bogus" --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18301/api/v1/import/batches/{id}" --value id=C:\repos\Quotinator\.claude\temp\qt-db-01-out\detail.json#linkedBatchIds.0 --out C:\repos\Quotinator\.claude\temp\qt-db-01-out\batch-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-db-01-out\batch-url.txt --expect 200 --status
```

**Expected:** `422` for the unknown status, then `200` for a batch id taken from the detail, proving the
file-resource detail and the batches endpoint agree on what exists.

### 6. Reconstruct a captured file byte-for-byte, and override its line ending

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18301/api/v1/import/file-resources/{id}/download" --value id=C:\repos\Quotinator\.claude\temp\qt-db-01-out\manifest-list.json#items.0.id --out C:\repos\Quotinator\.claude\temp\qt-db-01-out\download-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-db-01-out\download-url.txt --no-key --expect 200 --save C:\repos\Quotinator\.claude\temp\qt-db-01-out\downloaded.json
docker cp qt-db-01:/app/data/sources/manifest.json C:\repos\Quotinator\.claude\temp\qt-db-01-out\original\manifest.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-identical-in C:\repos\Quotinator\.claude\temp\qt-db-01-out\downloaded.json C:\repos\Quotinator\.claude\temp\qt-db-01-out\original
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18301/api/v1/import/file-resources/{id}/download?lineEnding=crlf" --value id=C:\repos\Quotinator\.claude\temp\qt-db-01-out\manifest-list.json#items.0.id --out C:\repos\Quotinator\.claude\temp\qt-db-01-out\crlf-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-db-01-out\crlf-url.txt --no-key --expect 200 --save C:\repos\Quotinator\.claude\temp\qt-db-01-out\crlf.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-db-01-out\crlf.json "\r\n" --expect-no-text C:\repos\Quotinator\.claude\temp\qt-db-01-out\original\manifest.json "\r\n"
```

**Expected:** every command exits `0`: the download, with no `X-Api-Key`, is byte for byte the file in the
image, and the `crlf` override puts `\r\n` into a file whose original has none.

**Compared by hash, not by eye**, so a single differing byte fails. The original's own lack of `\r\n` is
the control for the override: without it, a `\r\n` in the override could mean the file always had one.

### 7. Reject an unknown file resource and an unknown `lineEnding`

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/file-resources/00000000-0000-0000-0000-000000000000/download" --expect 404 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18301/api/v1/import/file-resources/{id}/download?lineEnding=bogus" --value id=C:\repos\Quotinator\.claude\temp\qt-db-01-out\manifest-list.json#items.0.id --out C:\repos\Quotinator\.claude\temp\qt-db-01-out\bogus-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-db-01-out\bogus-url.txt --expect 422 --status
```

**Expected:** `404` for the unknown id, and `422` for the unknown line ending on a known one.

### 8. Prune: refused without a key or with a malformed count, and nothing to prune after one start

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18301/api/v1/import/file-resources/prune --no-key --expect 401 --status
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18301/api/v1/import/file-resources/prune?keepPerFile=abc" --expect 422 --status
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18301/api/v1/import/file-resources/prune --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-01-out\prune.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-01-out\prune.json --equals prunedCount=0
```

**Expected:** `401`, `422`, then `prunedCount = 0`: each file has one captured version after a single
start, within the default of five kept.

**Step 10 is this `0`'s positive control**: the same endpoint, told to keep none, removes every captured
row.

### 9. Confirm the captured rows against the manifest and the API

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-01 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-01 --bind C:\repos\Quotinator\.claude\temp\qt-db-01
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-db-01\quotinatordata.db --sql "SELECT COUNT(*) AS Captured, SUM(CASE WHEN Origin = 'System' AND HomeDirectoryKey = 'sources' THEN 1 ELSE 0 END) AS Bundled, SUM(CASE WHEN FileName = 'manifest.json' THEN 1 ELSE 0 END) AS Manifest, SUM(CASE WHEN FileName <> 'manifest.json' THEN 1 ELSE 0 END) AS DataFiles, SUM(CASE WHEN Converter IS NOT NULL THEN 1 ELSE 0 END) AS Converted, SUM(CASE WHEN (Converter IS NULL) <> (ConverterOptions IS NULL) THEN 1 ELSE 0 END) AS ConverterWithoutOptions, (SELECT COUNT(*) FROM Import_FileResourceBatch b JOIN Import_FileResource m ON m.Id = b.FileResourceId WHERE m.FileName = 'manifest.json' AND m.IsDeleted = 0) AS ManifestLinks, SUM(CASE WHEN FileName <> 'manifest.json' AND (SELECT COUNT(*) FROM Import_FileResourceBatch b WHERE b.FileResourceId = Import_FileResource.Id) <> 1 THEN 1 ELSE 0 END) AS DataFilesNotLinkedOnce FROM Import_FileResource WHERE IsDeleted = 0" --json-out C:\repos\Quotinator\.claude\temp\qt-db-01-out\capture.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-01-out\capture.json --equals-json 0.Captured=C:\repos\Quotinator\.claude\temp\qt-db-01-out\list.json#totalCount --equals-field 0.Bundled=0.Captured --equals 0.Manifest=1 --equals 0.ConverterWithoutOptions=0 --equals-field 0.ManifestLinks=0.DataFiles --equals-json 0.ManifestLinks=C:\repos\Quotinator\.claude\temp\qt-db-01-out\batches.json#totalCount --equals 0.DataFilesNotLinkedOnce=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file data/sources/manifest.json --length-equals-json files=C:\repos\Quotinator\.claude\temp\qt-db-01-out\capture.json#0.DataFiles
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file data/sources/manifest.json --where files:converter --present files.0 --length-equals-json files=C:\repos\Quotinator\.claude\temp\qt-db-01-out\capture.json#0.Converted
```

**Expected:** nothing thrown before the stop, and every `json` command exits `0`:

- the database holds as many captured rows as the API listed, every one bundled, one of them
  `manifest.json`;
- one data file captured per manifest entry, and one recorded converter per entry that declares one, each
  with its options and none without;
- the manifest linked to every seed batch, and every data file to exactly one, its own.

**The batch links are relationships that hold whatever the bundled file set contains**, which is why
no figure in this step is written down.

**The `0` counts are evidence because the same query's other columns find rows**: converters are counted
against the manifest's own entries, and the data files each linked once are counted against the
manifest's links.

### 10. Positive control: told to keep none, prune removes every captured row

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-db-01 --port 18301 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-db-01
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18301/api/v1/import/file-resources/prune?keepPerFile=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-01-out\prune-all.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-01-out\prune-all.json --equals-json prunedCount=C:\repos\Quotinator\.claude\temp\qt-db-01-out\list.json#totalCount
dotnet script scripts/testing/http.csx -- --url "http://localhost:18301/api/v1/import/file-resources?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-01-out\list-pruned.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-01-out\list-pruned.json --equals totalCount=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-01 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** every command exits `0`: the restarted container still holds exactly what step 2 listed, a
prune keeping none removes all of it, the list is then empty, and nothing was thrown.

**This is what makes step 8's `0` a reading rather than a prune that never removes anything.**

## Observed effect

**Measured 2026-11-08** on `feature/notification-system` (1.9.0-alpha), as written, every command
exiting `0`:

| Step | Observed |
|---|---|
| 1 | `1` start logged |
| 2 | `6` captured files, all `6` bundled with a batch count, `0` carrying batch ids |
| 3 | `422`; the `system` filter returns all `6` |
| 4 | one `manifest.json`, `5` batch ids, `linkedBatchCount = 5`, `5` seed batches |
| 5 | `422`; the linked batch `200` |
| 6 | the download `2251` bytes, SHA-256 `8ADA33085594C0CF9333BCAEE013F6F4FEADBC2DEBD3C0A5D4D036F427B5C5FA`, identical to the image's copy; the `crlf` form `2331` bytes, `80` line endings each a byte longer, where the original has no `\r\n` |
| 7 | `404`, `422` |
| 8 | `401`, `422`, `prunedCount = 0` |
| 9 | `0` exceptions; `Captured = 6`, `Bundled = 6`, `Manifest = 1`, `DataFiles = 5` against the manifest's `5` entries, `2` converters against its `2` declaring one, `0` without options, `ManifestLinks = 5`, `0` data files not linked once |
| 10 | `prunedCount = 6` keeping none, the list then `0`; `0` exceptions |

**The earlier form of this document had drifted, which is what the conversion removed.** It named four
bundled data files where the manifest now lists five (`quotinator-seasons.json` was added after it was
written), and expected bare `LF` of files the capture records as `CRLF`. It still passed, because it
printed tables for a reader to compare rather than asserting on them. The converted steps relate every
reading to the manifest, the API or another reading, so neither drift is possible.

The capture's own log lines are still not asserted: nothing about the capture depends on them.

## Canary

Build the commit before [#251](https://github.com/DutchJaFO/Quotinator/issues/251)'s first code
change, under a distinct tag, and run steps 1 to 6 against it.

Step 2's listing endpoint must answer `404`, and step 9 finds no captured rows at all: capture and the
endpoints that read it are what #251 and [#252](https://github.com/DutchJaFO/Quotinator/issues/252)
added, so nothing on that build has provenance to report.

Step 6 carries a control of its own that does not need a second build. It reconstructs the file byte for
byte and then overrides the line ending, asserting `\r\n` appears where the original has none. A
reconstruction returning something unrelated to the captured bytes fails one or the other, so neither
reading can pass alone.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-01 --bind C:\repos\Quotinator\.claude\temp\qt-db-01
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-db-01 --delete C:\repos\Quotinator\.claude\temp\qt-db-01-out
```

**Expected:** the container and both directories are gone.
