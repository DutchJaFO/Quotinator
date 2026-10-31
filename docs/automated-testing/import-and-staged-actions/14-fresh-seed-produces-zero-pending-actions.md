# A fresh seed resolves every bundled file with nothing left pending

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #181
**Fully green after:** [#400](https://github.com/DutchJaFO/Quotinator/issues/400): step 5 has nothing to run until then, because the regeneration it needs is reached through the source refresh #400 builds its step on

## Preconditions

Every bundled file runs under `review` policy with its own `ruleFile`/`sourceAliasFile`.

A `ConflictResolutionRule` auto-resolves a genuinely ambiguous field on an already-seen entity id
(Modify path only). A `SourceAliasRule` corrects a misspelled or inconsistent raw `(title, type)` to
the already-canonical Source **before** Source resolution runs, so it applies to both a first-seen Add
and a re-seen Modify, and prevents a duplicate Source row being created for the wrong spelling in the
first place.

**A `ConflictResolutionRule` alone cannot do that**: it only ever corrects what a Quote's own field
*displays*, never which Source row it links to.

**This is the one document permitted to read bundled content**, per the index's second rule and #400. The seed it
inspects is the profile's own first boot, and step 6 adds one setting of its own in a second container.

## Determinism

- **This is the zero-failures assertion for the bundled dataset.** Nothing staged awaiting review is
  the fact; the number of quotes seeded is not asserted, only that content exists.
- **Every count is checked for being above `0`, never against a figure**: what matters is that seeding produced
  content, and a source's size changes whenever a source or a correction does.
- **Sources are read through the REST server**, so nothing stops the container or copies its database. Titles compare
  ignoring case, so a case-only difference counts as a duplicate: that is the point, since the alias mechanism exists to
  prevent exactly that.
- **`seed-sources` and `seed-noops` are proven able to fail by their own unit tests**, `BundledSeedCommandTests`, which
  supply a row of each shape they must flag. A check that reads an empty list fails, since an empty list would satisfy
  it, so a pass here means real rows were read and none was flagged.
- **Step 6 needs its own container with the bundled batch auto-purge off.** The profile's setting deletes a cleanly applied
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

### 2. Read what the first boot seeded

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18614/api/v1/version --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-14-out\version.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-14-out\version.json --greater-than database.quotes=0 --greater-than database.sources=0 --greater-than database.characters=0 --greater-than database.people=0 --greater-than database.series=0 --greater-than database.seasons=0 --greater-than database.universes=0 --greater-than database.stageDirections=0 --greater-than database.soundCues=0 --greater-than database.conversations=0
```

**Expected:** every entity count is above `0`, each named on its own, so a type that seeds nothing reads as its own name
rather than shifting a total.

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

### 5. Confirm each bundled file still matches what its own converter produces

**There is nothing to run here until [#400](https://github.com/DutchJaFO/Quotinator/issues/400) delivers this step.** It
is this document's watch on external data, the only place in the project allowed to go red because the outside world
changed rather than because this project broke (developer rule, 2026-09-08). What it was written to run could not run for
anybody: it looped over a manifest property that does not exist, and called a `--convert` option the application does not
have. #400 item 6 replaces it with a runnable step, reached through the source refresh. `Quotinator:AutoUpdateSources` is
off by default (#424), so a container seeds the checked-in copy and that step will have to turn the refresh on for its run.

**Expected:** no command, and nothing to record. This is not a pass: the comparison has not been made.

### 6. Confirm every no-op resolution is one somebody has accounted for

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

### 7. Nothing unexpected was thrown

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

**Measured 2026-11-01** against `quotinator:local` built from `00a9dfe2`. These figures describe the bundled sources as
they stood, and only the checks above are asserted.

| Reading | Value |
|---|---|
| `/version` after first boot | 795 quotes, 473 sources, 15 characters, 3 people, 31 series, 3 seasons, 8 universes, 2 stage directions, 1 sound cue, 4 conversations |
| `status=pending`, and `status=PENDING` | `0` both ways |
| `seed-sources` | 473 sources, 6 titles carrying more than one row, nothing flagged |
| `seed-noops` after a reseed | 25 no-ops, 24 `Quote` and 1 `Source`, every one accounted for |

Step 5 ran nothing, so the bundled files have not been compared with their converters.

**No exception was logged by either container before the malformed upload**, the seed and the reseed included. That
upload's `JsonReaderException` was then logged once.

## Canary

**Step 3 asserts an absence, and on its own it is the weakest shape this suite has.** Nothing left staged awaiting review
is what a clean seed produces, and also what a seed that imported nothing, a broken staging path and a regressed status
filter all produce. Steps 2 and 6 are what give it meaning: step 2 reads what the boot actually seeded, and step 6 requires
at least one no-op and every no-op accounted for, so an empty list is not accepted as self-explanatory.

For the pre-fix form, build the commit before [#181](https://github.com/DutchJaFO/Quotinator/issues/181)'s first code
change, under a distinct tag, and run steps 2 to 4 against it: the bundled files do not all resolve cleanly there, so step 3
finds rows awaiting review and step 4 finds the duplicate Sources that went with them.

**Step 5 has no canary**, for the reason the `Fully green after:` field states. Pin
`Quotinator__AutoPurgeBundledImportActions` for every run: an empty list produced by auto-purge is indistinguishable from an
empty list produced by the behaviour under test.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-14
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-14-noop
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-import-14-out
```

**Expected:** both containers, their volumes and the output directory are gone.
