# A fresh seed resolves every bundled file with nothing left pending

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #181

## Preconditions

Every bundled file runs under `review` policy with its own `ruleFile`/`sourceAliasFile`.

A `ConflictResolutionRule` auto-resolves a genuinely ambiguous field on an already-seen entity id
(Modify path only). A `SourceAliasRule` corrects a misspelled or inconsistent raw `(title, type)` to
the already-canonical Source **before** Source resolution runs, so it applies to both a first-seen Add
and a re-seen Modify, and prevents a duplicate Source row being created for the wrong spelling in the
first place.

**A `ConflictResolutionRule` alone cannot do that**: it only ever corrects what a Quote's own field
*displays*, never which Source row it links to.

**This document reads bundled content**, per the index's second rule and #400, to warn when upstream content has made
the import fail or a rule stale. The bundled files are a moving target, so it asserts no figure they happen to produce.
Whether each bundled file still matches what its converter makes of the upstream source is
[`31`](31-bundled-files-match-their-converters.md)'s question. The seed it inspects is the profile's own first boot, and
step 5 adds one setting of its own in a second container.

## Determinism

- **This is the zero-failures assertion for the bundled dataset.** Nothing staged awaiting review is
  the fact; the number of quotes seeded is not asserted, only that content exists.
- **Step 2 counts files, never rows**: it holds the seed's notifications to the number of files the seed preview lists,
  read in the same run, so a source added or removed moves both sides together, and a source's size never enters it.
- **Sources are read through the REST server**, so nothing stops the container or copies its database. Titles compare
  ignoring case, so a case-only difference counts as a duplicate: that is the point, since the alias mechanism exists to
  prevent exactly that.
- **`seed-sources` and `seed-noops` are proven able to fail by their own unit tests**, `BundledSeedCommandTests`, which
  supply a row of each shape they must flag. A check that reads an empty list fails, since an empty list would satisfy
  it, so a pass here means real rows were read and none was flagged.
- **Step 5 needs its own container with the bundled batch auto-purge off.** The profile's setting deletes a cleanly applied
  batch's `Import_Action` rows the moment it applies, which is correct behaviour and exactly what makes them unreadable.

## Steps

### 1. Create this test's own environment

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-import-14-out
dotnet script scripts/testing/test-env.csx -- create --name qt-import-14 --port 18614 --image quotinator:local
```

**Expected:** the app reports healthy: the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Read what the first boot reported seeding

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18614/api/v1/admin/database/seed/preview --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-14-out\preview.json
dotnet script scripts/testing/http.csx -- --url "http://localhost:18614/api/v1/notifications?pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-14-out\notifications.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-14-out\notifications.json --where items:metadataKind=reseedfileapplied --length-equals-json items=C:\repos\Quotinator\.claude\temp\qt-import-14-out\preview.json#reports
```

**Expected:** one `reseedFileApplied` notification for every file the seed preview lists: each bundled file reported
itself applied with nothing left to review. That is the seed's own report of what it did, written as it ran. A first-boot
seed runs at startup rather than through an endpoint, so it has no response to read, and this is its primary result
(#503, decision H). The counts other endpoints report afterwards are a secondary source, read by
[`database-lifecycle/05`](../database-lifecycle/05-version-counts-follow-the-content.md) in a full run.

**On failure:** a file with no notification either left something to review, which
`GET /import/actions?status=pending` names, or was not seeded. Stop: do not run step 3.

### 3. Confirm nothing is left staged awaiting review

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18614/api/v1/import/actions?status=pending&pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-14-out\pending.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-14-out\pending.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url "http://localhost:18614/api/v1/import/actions?status=PENDING&pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-14-out\pending-upper.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-14-out\pending-upper.json --equals totalCount=0
```

**Expected:** none pending, with `status` in either case. That this filter finds a pending action when one exists is what
[`01`](01-staged-action-review-workflow.md) shows, in both cases.

**On failure:** look for the line naming `left staged awaiting review` in the log, then read
`GET /import/actions?batchId=<id>` to see which entity or field lacks a rule or alias.

### 4. Cross-check for duplicate Sources

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18614/api/v1/masterdata/sources?pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-14-out\sources.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- seed-sources --sources C:\repos\Quotinator\.claude\temp\qt-import-14-out\sources.json --declared C:\repos\Quotinator\data\sources
```

**Expected:** no exact duplicate, no casing duplicate, and no undeclared date variant.

An **exact duplicate** is the same title, type and date stored twice, which nothing legitimises. A **casing duplicate** is
two spellings of one title that `sourceAliasFile` should have merged onto a canonical form. An **undeclared date variant**
is a title carrying two dates with nobody having said which reading applies, and there are exactly two ways to say it: a
dated `SourceAliasRule` when one of the dates is wrong, or a pair of `sources[]` declarations when the title really does
name two works. Once either is in place the variant is permitted and passes. Until then it fails, because a wrong date
must not reach the database unremarked just because nothing crashed.

**Casing duplicates are explicitly permitted, never hidden** (developer rule, 2026-09-08). The importer keeps the incoming
spelling (`ResolveSourceAsync_DateVariantOfDifferentlyCasedTitle_KeepsBothSpellingsVisible`), both reach the database, this
check reports them, and a reviewed `SourceAliasRule` is what resolves it
(`ResolveSourceAsync_DifferentlyCasedTitle_WithAliasDeclared_ResolvesToTheCanonicalSpelling`).

### 5. Confirm every no-op resolution is one somebody has accounted for

**This is the step that makes a missing rule visible.** Since [#377](https://github.com/DutchJaFO/Quotinator/issues/377)
an import action whose resolution settles on the values already stored is classified `ResolvedToExisting` rather than
`Modify`, so these rows are a countable population. Each is either a rule somebody should declare, or legitimately nothing.

```
dotnet script scripts/testing/test-env.csx -- create --name qt-import-14-noop --port 19614 --image quotinator:local --env Quotinator__AutoPurgeBundledImportActions=false
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19614/api/v1/admin/database/reseed --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:19614/api/v1/import/actions?pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-14-out\actions.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- seed-noops --actions C:\repos\Quotinator\.claude\temp\qt-import-14-out\actions.json --rules C:\repos\Quotinator\data\sources
```

**Expected:** at least one no-op, counted by entity type, and each accounted for in one of two ways. A rule already covers
the entity: an `AlreadyApplied` `ConflictResolutionRule`, still doing work and not retirable, per #374. Or the incoming side
does not carry the field and the stored value legitimately wins, so no rule is wanted.

**On failure:** a new unexplained no-op after a source refresh means the outside world moved in a way our rules do not yet
cover. Read that row's `existingFields` and `incomingFields`, and decide whether it wants a `ConflictResolutionRule`, a
`SourceAliasRule`, or nothing at all. Deciding "nothing at all" is a legitimate outcome; leaving it undecided is not.

### 6. Nothing unexpected was thrown

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-import-14 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-import-14-noop --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18614/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\startup-and-degradation\fixtures\malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-import-14 --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** no exception has been logged by either container, the seed and the reseed included. The malformed upload then
answers `422` and its `JsonReaderException` is logged, which is the positive control: the same channel that read zero can
see an exception, so the zero means something.

**The count comes first**, because the upload throws more than the type its control names.

**On failure:** a count above zero is an issue, unless a Knowledgebase entry says otherwise. Report it with the
line that names it.

## Observed effect

**Measured 2026-11-17** against `quotinator:local` built from `2fd67742`. These figures describe the bundled sources as
they stood, and only the checks above are asserted.

| Reading | Value |
|---|---|
| `status=pending`, and `status=PENDING` | `0` both ways |
| `seed-sources` | 473 sources, 6 titles carrying more than one row, nothing flagged |
| `seed-noops` after a reseed | 25 no-ops, 24 `Quote` and 1 `Source`, every one accounted for |

**No exception was logged by either container before the malformed upload**, the seed and the reseed included. That
upload's `JsonReaderException` was then logged once.

## Canary

**Step 3 asserts an absence, and on its own it is the weakest shape this suite has.** Nothing left staged awaiting review
is what a clean seed produces, and also what a seed that imported nothing, a broken staging path and a regressed status
filter all produce. Steps 2 and 5 are what give it meaning: step 2 reads what the boot reported seeding, and step 5 requires
at least one no-op and every no-op accounted for, so an empty list is not accepted as self-explanatory.

For the pre-fix form, build the commit before [#181](https://github.com/DutchJaFO/Quotinator/issues/181)'s first code
change, under a distinct tag, and run steps 2 to 4 against it: the bundled files do not all resolve cleanly there, so step 3
finds rows awaiting review and step 4 finds the duplicate Sources that went with them.

**Measured 2026-11-01** on `quotinator:canary-181`, built from `bcace5f6`, steps 2 to 4 in order after step 1, when step 2
still read `/version`'s counts: quotes, sources, characters and people read above `0`, and the other six counts did not
exist on that build. Step 3 fails: `75` pending, with `status` in either case. Step 2 now reads the seed's own
notifications, which that build predates; its reading there is taken when the canary is next run.

**Step 4 passes there, and it should not.** `seed-sources` exits `0`, reading `482` sources and no title on more than one row,
while the list it read holds `Matrix` and `The Matrix`, `Shawshank Redemption` and `The Shawshank Redemption`, `Wolf of the
Wall Street` and `The Wolf of Wall Street`, and `Spiderman` and `Spider Man`. It compares title and type ignoring case, so
duplicates spelled differently are invisible to it, and those are what a `SourceAliasRule` exists to merge. Until step 4 can
see them, which is [#479](https://github.com/DutchJaFO/Quotinator/issues/479), step 3 alone proves this document able to fail.

Pin `Quotinator__AutoPurgeBundledImportActions` for every run: an empty list produced by auto-purge is indistinguishable from an
empty list produced by the behaviour under test.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-14
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-14-noop
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-import-14-out
```

**Expected:** both containers, their volumes and the output directory are gone.
