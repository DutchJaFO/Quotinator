# A first-boot seed reports what it loaded

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #302, #503

## Preconditions

**Beyond the profile.** The container runs with `Quotinator__IncludeDefaultSources=false` and starts on an empty database
with one file of its own in `{dataDir}/imports/`: the [fixture](../database-lifecycle/fixtures/reset-content.json) that
holds rows in every content category, so every entity type has something to report. That is the same file and the same
empty database [`19`](19-per-file-import-report.md) starts from before it calls the seed endpoints, so the first-boot seed
and the seed endpoints are proven on the same input.

## Determinism

- **A first-boot seed runs at startup, not through an endpoint**, so it has no response to read. What it reports as it
  runs is its result: one `reseedFileApplied` notification per file that applied with nothing left to review, carrying a
  count per entity type (#302). That is the primary result this document reads (#503, decision H). The counts other
  endpoints report afterwards are a secondary source, read by
  [`database-lifecycle/05`](../database-lifecycle/05-version-counts-follow-the-content.md) in a full run.
- **The fixture decides every count**, so each is asserted exactly. Each entity type is named in its own assertion, so a
  missing one fails naming itself.
- **The counts sit in the notification's `metadata`**, which the API returns as JSON text; the probe reads a path into
  text holding JSON. The seed writes one entry per entity type, ordered by type name, so each entry is read by its
  position and its `entityType` asserted alongside its count.

## Steps

### 1. Create this test's own environment

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-import-32-data --delete C:\repos\Quotinator\.claude\temp\qt-import-32-out --copy C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-content.json C:\repos\Quotinator\.claude\temp\qt-import-32-data\imports\reset-content.json --copy C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-imports-manifest.json C:\repos\Quotinator\.claude\temp\qt-import-32-data\imports\manifest.json
dotnet script scripts/testing/test-env.csx -- create --name qt-import-32 --port 18632 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-import-32-data --env Quotinator__IncludeDefaultSources=false
```

**Expected:** the app reports healthy, so the seed has finished.

**On failure:** the step below reads this container. Stop rather than running it against an app that never became
healthy.

### 2. Read what the seed reported loading

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18632/api/v1/notifications?pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-32-out\notifications.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-32-out\notifications.json --where items:metadataKind=reseedfileapplied --length-equals items=1 --equals items.0.metadata.fileName=reset-content.json --length-equals items.0.metadata.counts=10 --equals items.0.metadata.counts.0.entityType=Character --equals items.0.metadata.counts.0.added=3 --equals items.0.metadata.counts.1.entityType=Conversation --equals items.0.metadata.counts.1.added=2 --equals items.0.metadata.counts.2.entityType=Person --equals items.0.metadata.counts.2.added=2 --equals items.0.metadata.counts.3.entityType=Quote --equals items.0.metadata.counts.3.added=4 --equals items.0.metadata.counts.4.entityType=Season --equals items.0.metadata.counts.4.added=2 --equals items.0.metadata.counts.5.entityType=Series --equals items.0.metadata.counts.5.added=2 --equals items.0.metadata.counts.6.entityType=SoundCue --equals items.0.metadata.counts.6.added=2 --equals items.0.metadata.counts.7.entityType=Source --equals items.0.metadata.counts.7.added=3 --equals items.0.metadata.counts.8.entityType=StageDirection --equals items.0.metadata.counts.8.added=2 --equals items.0.metadata.counts.9.entityType=Universe --equals items.0.metadata.counts.9.added=2
```

**Expected:** one `reseedFileApplied` notification, for `reset-content.json`, reporting every entity type added with the
count the fixture gives it: 4 quotes, 3 sources, 3 characters, and 2 of each other type.

**On failure:** no notification means the file left something to review or was not seeded; a count other than the
fixture's means the seed did not load what the file holds. Stop.

## Observed effect

Not yet measured.

## Canary

Build the commit before [#302](https://github.com/DutchJaFO/Quotinator/issues/302)'s first code change, under a distinct
tag, and run steps 1 and 2 against it: that build writes no per-file confirmation, so step 2 finds no `reseedFileApplied`
notification. Not yet run.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-32
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-import-32-data --delete C:\repos\Quotinator\.claude\temp\qt-import-32-out
```

**Expected:** the container and the output directories are gone.
