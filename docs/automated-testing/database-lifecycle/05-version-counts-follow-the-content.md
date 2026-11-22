# `/version`'s content counts follow what a seed, an import and a Reset did

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #468, #503

## Preconditions

**Beyond the profile.** Both containers run with `Quotinator__IncludeDefaultSources=false`, so nothing bundled is in the
database, and the only content is the [fixture](fixtures/reset-content.json) that holds rows in every content category. It
arrives by the two routes [`03`](03-reset-is-a-full-wipe.md) uses:

| Variant | Port | How the content arrives |
|---|---|---|
| A, pre-filled | `18305` | A file in `{dataDir}/imports/`, seeded at first boot |
| B, API import | `19305` | `POST /api/v1/import` after startup |

**This is a secondary check.** Whether a seed, an import or a Reset did its work is read from that operation's own result:
the seed's notification in [`import-and-staged-actions/32`](../import-and-staged-actions/32-a-first-boot-seed-reports-what-it-loaded.md),
the import's response, and Reset's response and the list endpoints in `03`. This document asks only whether `/version`
reports counts that agree with what those operations did (#503, decision H). It runs in a full run, not in the smoke set.

## Determinism

- **Waits for health, not a duration.**
- **The fixture decides every count**, so each is asserted exactly, and each entity type is named in its own assertion, so
  a missing one fails naming itself.
- **Each count is read after the operation has answered**, so a stale count reads as stale rather than as a race.

## Steps

**Variant A, steps 1 to 3: the content is seeded at first boot.**

### 1. Create the environment, with the content waiting to be seeded

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-db-05a-data --delete C:\repos\Quotinator\.claude\temp\qt-db-05-out --copy C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-content.json C:\repos\Quotinator\.claude\temp\qt-db-05a-data\imports\reset-content.json --copy C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-imports-manifest.json C:\repos\Quotinator\.claude\temp\qt-db-05a-data\imports\manifest.json
dotnet script scripts/testing/test-env.csx -- create --name qt-db-05a --port 18305 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-db-05a-data --env Quotinator__IncludeDefaultSources=false
```

**Expected:** the app reports healthy, so the seed has finished.

**On failure:** stop: every step of this variant reads this container.

### 2. `/version` reports what the seed loaded

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18305/api/v1/version --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-05-out\a-version-seeded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-05-out\a-version-seeded.json --equals database.quotes=4 --equals database.sources=3 --equals database.characters=3 --equals database.people=2 --equals database.series=2 --equals database.seasons=2 --equals database.universes=2 --equals database.stageDirections=2 --equals database.soundCues=2 --equals database.conversations=2
```

**Expected:** every count is the fixture's: 4 quotes, 3 sources, 3 characters, and 2 of each other type.

**On failure:** `/version` does not report what the seed loaded. Stop: do not run step 3.

### 3. `/version` reports what Reset left

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18305/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18305/api/v1/version --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-05-out\a-version-reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-05-out\a-version-reset.json --equals database.quotes=0 --equals database.sources=0 --equals database.characters=0 --equals database.people=0 --equals database.series=0 --equals database.seasons=0 --equals database.universes=0 --equals database.stageDirections=0 --equals database.soundCues=0 --equals database.conversations=0
```

**Expected:** every count is `0`.

**On failure:** `/version` does not report what Reset left. Stop: do not run step 4.

**Variant B, steps 4 to 6: the content is imported through the API.**

### 4. Create the environment and import the content

```
dotnet script scripts/testing/test-env.csx -- create --name qt-db-05b --port 19305 --image quotinator:local --env Quotinator__IncludeDefaultSources=false
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19305/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-content.json --expect 200 --status
```

**Expected:** the app reports healthy and the import answers `200`.

**On failure:** stop: every later step reads this container's content.

### 5. `/version` reports what the import added

```
dotnet script scripts/testing/http.csx -- --url http://localhost:19305/api/v1/version --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-05-out\b-version-imported.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-05-out\b-version-imported.json --equals database.quotes=4 --equals database.sources=3 --equals database.characters=3 --equals database.people=2 --equals database.series=2 --equals database.seasons=2 --equals database.universes=2 --equals database.stageDirections=2 --equals database.soundCues=2 --equals database.conversations=2
```

**Expected:** every count is the fixture's, as in step 2.

**On failure:** every count reading `0` means `/version` did not refresh its counts after the API import, the bug
[#468](https://github.com/DutchJaFO/Quotinator/issues/468) owns and the
[Knowledgebase](../../knowledgebase/version-counts-are-stale-after-an-import.md) describes. Any other reading is a
different fault. Either way, stop: do not run step 6.

### 6. `/version` reports what Reset left

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19305/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:19305/api/v1/version --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-05-out\b-version-reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-05-out\b-version-reset.json --equals database.quotes=0 --equals database.sources=0 --equals database.characters=0 --equals database.people=0 --equals database.series=0 --equals database.seasons=0 --equals database.universes=0 --equals database.stageDirections=0 --equals database.soundCues=0 --equals database.conversations=0
```

**Expected:** every count is `0`.

**On failure:** `/version` does not report what Reset left. Stop.

## Observed effect

**Measured 2026-11-16** against `quotinator:local` built from `2fd67742`, as steps of
[`03`](03-reset-is-a-full-wipe.md) before #503 moved them here: after the seed, `/version` read the fixture's counts, and
`0` after Reset. Straight after the API import, `/version` read `0` for all ten counts while the list endpoints returned
the rows, as #468 describes, and `0` after Reset, which a stale counter also reads.

## Canary

Step 5 fails on every build until #468 lands, and that failure is this document's proof that it can fail: the same
command that reads the seeded counts in step 2 reads `0` where the import added rows. Step 6's `0` proves nothing on its
own while step 5 fails, since a counter that never moved also reads `0`; it is meaningful only once step 5 passes.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-05a --bind
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-05b
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-db-05a-data --delete C:\repos\Quotinator\.claude\temp\qt-db-05-out
```

**Expected:** both containers, variant B's volume, variant A's data directory and the output directory are gone.
