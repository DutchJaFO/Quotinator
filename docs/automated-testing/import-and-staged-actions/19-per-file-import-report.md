# Every seed and import surface reports per-file, per-entity-type counts

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #221

## Preconditions

This replaces the old flat `duplicates` count everywhere a seed or import operation reports back, and every surface it
checks (the counts after the seed, seed preview, reseed, reset, import and import preview) is reachable on one container.

**Beyond the profile.** The container runs with `Quotinator__IncludeDefaultSources=false` and seeds one file of its own at
first boot, from `{dataDir}/imports/`: the [fixture](../database-lifecycle/fixtures/reset-content.json) that holds rows in
every content category, so every entity type has something to report. Step 5 uploads the same file, so a broken seed or a
broken import blocks this document. That is the index's second honest resolution for a precondition, and it is named here
so a blocked run reads as that.

## Determinism

- **The shape is what is asserted, plus what the fixture decides.** One entry per configured source file, each with a
  `fileName` and an `entityTypes` object. How many rows a file brings in is asserted where the fixture fixes it, and the
  breakdown of a repeat run is not: [#470](https://github.com/DutchJaFO/Quotinator/issues/470) makes this fixture read as
  `modified` when imported again, and that is not this document's subject.
- **The removed fields matter as much as the added ones.** `totalQuotes`, `uniqueQuotes` and
  `crossFileDuplicates` must be absent: a response still carrying them means the old shape survived
  somewhere.
- **Every entity type is named, never counted.** A missing name is visible as a missing name, where a count goes stale the
  moment a new type ships and reads as a failure that gets "fixed" by editing the digit.

## Steps

### 1. Create this test's own environment

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-import-19-data --delete C:\repos\Quotinator\.claude\temp\qt-import-19-out --copy C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-content.json C:\repos\Quotinator\.claude\temp\qt-import-19-data\imports\reset-content.json --copy C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-imports-manifest.json C:\repos\Quotinator\.claude\temp\qt-import-19-data\imports\manifest.json
dotnet script scripts/testing/test-env.csx -- create --name qt-import-19 --port 18619 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-import-19-data --env Quotinator__IncludeDefaultSources=false
dotnet script scripts/testing/http.csx -- --url http://localhost:18619/api/v1/version --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-import-19-out\version-seeded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-19-out\version-seeded.json --equals database.quotes=4 --equals database.sources=3 --equals database.characters=3 --equals database.people=2 --equals database.series=2 --equals database.seasons=2 --equals database.universes=2 --equals database.stageDirections=2 --equals database.soundCues=2 --equals database.conversations=2
```

**Expected:** the app reports healthy, so the seed has finished, and the `json` command exits `0`: the version
endpoint reports every entity type with the count the fixture gives it. Each type is named in its own assertion,
so a missing one fails naming itself.

**Read here, straight after the first boot**, because every later step changes the counts: the reseed, the
Reset and the import. These are the counts the seed left. They were read from the `[Database - Stats]` startup
line until 2026-11-16; the log is not evidence (#490's T2 review, developer), and the version endpoint reports
the same counts from the same source.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy. A count other than the fixture's means the seed did not load what the file holds.

### 2. Read the seed preview

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18619/api/v1/admin/database/seed/preview --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-19-out\preview.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-19-out\preview.json --length-equals reports=1 --equals reports.0.fileName=reset-content.json --present reports.0.entityTypes.Quote --present reports.0.entityTypes.Source --present reports.0.entityTypes.Character --present reports.0.entityTypes.Person --present reports.0.entityTypes.Series --present reports.0.entityTypes.Season --present reports.0.entityTypes.Universe --present reports.0.entityTypes.StageDirection --present reports.0.entityTypes.SoundCue --present reports.0.entityTypes.Conversation --equals reports.0.entityTypes.Quote.incoming=4 --present reports.0.entityTypes.Quote.new --present reports.0.entityTypes.Quote.modified --present reports.0.entityTypes.Quote.blocked --present reports.0.entityTypes.Quote.discarded --present reports.0.entityTypes.Quote.pending --present reports.0.entityTypes.Quote.stale
```

**Expected:** `200` with one entry for the one configured file, carrying its `fileName` and an `entityTypes` object keyed
by each entity type, each with its counts.

### 3. Reseed

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18619/api/v1/admin/database/reseed --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-19-out\reseed.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-19-out\reseed.json --equals quotes=4 --equals sources=3 --equals characters=3 --equals people=2 --equals series=2 --equals seasons=2 --equals universes=2 --equals stageDirections=2 --equals soundCues=2 --equals conversations=2 --length-equals reports=1 --equals reports.0.fileName=reset-content.json --present reports.0.entityTypes.Quote
```

**Expected:** `200`, with a row count for each category and one entry in the same per-file shape.

### 4. Repeat against `POST /admin/database/reset`

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18619/api/v1/admin/database/reset --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-19-out\reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-19-out\reset.json --equals quotes=0 --equals sources=0 --equals characters=0 --equals people=0 --equals series=0 --equals seasons=0 --equals universes=0 --equals stageDirections=0 --equals soundCues=0 --equals conversations=0 --length-equals reports=0
```

**Expected:** the same shape, every count `0` and no reports. Reset no longer reimports content after rebuilding the
schema (#156), so there is nothing to report.

### 5. Import a single file

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18619/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-content.json --duplicate-resolution newest-wins --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-19-out\import.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-19-out\import.json --equals summary.imported=4 --present conflicts --present errors --equals report.fileName=reset-content.json --equals report.entityTypes.Quote.new=4 --present report.entityTypes.Conversation
```

**Expected:** `200` with a top-level `report`, singular, since one file was sent, alongside the existing
`summary`/`conflicts`/`errors` fields, shaped like one entry from `reports`. Reading `report.fileName` is what shows it is an
object: an array has no such path.

### 6. Re-run the same call via `POST /api/v1/import/preview`

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18619/api/v1/import/preview --file C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-content.json --duplicate-resolution newest-wins --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-19-out\import-preview.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-19-out\import-preview.json --equals preview=true --equals report.fileName=reset-content.json --equals report.entityTypes.Quote.incoming=4 --present report.entityTypes.Conversation
```

**Expected:** the same `report` shape, because the report reflects the staged actions whether or not the batch was applied.

### 7. Confirm the removed fields are actually absent

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-no-text C:\repos\Quotinator\.claude\temp\qt-import-19-out\preview.json "totalQuotes|uniqueQuotes|crossFileDuplicates" --expect-text C:\repos\Quotinator\.claude\temp\qt-import-19-out\preview.json "fileName" --expect-text C:\repos\Quotinator\.claude\temp\qt-import-19-out\preview.json "entityTypes"
```

**Expected:** the seed preview holds none of the removed fields, and does hold `fileName` and `entityTypes`, the fields that
replaced them.

**The second half is the positive control, and without it the first proves nothing.** A pattern that cannot match anything
reports an absence exactly as a removed field does; only a field the same read *does* find separates them.
`api-surface/04` shipped a removal check with no control and passed it for weeks on patterns that could never match; see the
index's *A removed or added feature needs its own proof, alongside the normal behaviour*.

### 8. Nothing unexpected was thrown

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-import-19 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18619/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\startup-and-degradation\fixtures\malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-import-19 --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** no exception has been logged by anything so far. The malformed upload then answers `422` and its
`JsonReaderException` is logged, which is the positive control: the same channel that read zero can see an exception, so the
zero means something.

**The count comes first**, because the upload throws more than the type its control names.

**On failure:** a count above zero is an issue, unless a Knowledgebase entry says otherwise. Report it with the
line that names it.

## Observed effect

**Measured 2026-11-15** against `quotinator:local` built from `9b59f414`.

| Surface | Answer | What it reported |
|---|---|---|
| `GET /admin/database/seed/preview` | `200` | One entry, `reset-content.json`, naming all ten entity types |
| `POST /admin/database/reseed` | `200` | The fixture's counts, and one entry in the same shape |
| `POST /admin/database/reset` | `200` | Every count `0`, no reports |
| `POST /import` | `200` | `summary.imported=4`, and a singular `report` with `Quote.new=4` |
| `POST /import/preview` | `200` | `preview=true`, and the same `report` shape |
| The counts after the seed | | Read then from the `[Database - Stats]` line: 4 quotes, 3 sources, 3 characters, and 2 of each other type. Step 1 reads them from `GET /api/v1/version` since 2026-11-16, not yet measured in that form |

The seed preview holds none of `totalQuotes`, `uniqueQuotes` and `crossFileDuplicates`, and holds both `fileName` and
`entityTypes`.

**No exception was logged before the malformed upload.** That upload's `JsonReaderException` was then logged once.

## Canary

Build the commit before [#221](https://github.com/DutchJaFO/Quotinator/issues/221)'s first code
change, under a distinct tag, and run steps 1 to 7 against it. Per-file, per-entity-type counts are
what #221 added, so every surface reports a single total there and step 7's removed fields are still
present. The fixture may name categories that build predates, which would fail step 2 there instead: remove those sections
from the canary's copy and say so in its reading.

**Measured 2026-11-01** on `quotinator:canary-221`, built from `22d45db9`, steps 2 to 8 of the document's form then in order after step 1, with the fixture
unchanged, since that build seeded it. Step 2 fails: no `reports`. Step 3 fails: `quotes=4`, `sources=3`, `characters=3` and
`people=2`, with no other counts and no `reports`. Step 4 fails: the reset reimports and reads those same counts, which is
#156's defect rather than this one. Steps 5 and 6 fail: no `report`. Step 7 fails: the preview still holds the removed fields.
Step 8 of that form, which read the `[Database - Stats]` line, fails: it names quotes, sources, characters and people, and
none of the other six. That reading is step 1's counts today; whether that build's version endpoint reports the other six is
not yet read.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-19 --bind
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-import-19-data --delete C:\repos\Quotinator\.claude\temp\qt-import-19-out
```

**Expected:** the container, its data directory and the output directory are gone.
