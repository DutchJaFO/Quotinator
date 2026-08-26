# Character Modify through the widened schema, explicit ids on Add, and case-insensitive Source matching

**Smoke:** no
**Environment:** Fresh
**Traces to:** #175

## Preconditions

Before this issue, `characters[]` supported only Correction (`id` present, matched by id) or
brand-new-via-natural-key — there was no way to correct an existing Character's `Name` through the
staging/decide pipeline the way Source, Person
([`08-person-modify-and-lowercase-id-reversal.md`](08-person-modify-and-lowercase-id-reversal.md)),
StageDirection and SoundCue
([`07-stagedirection-soundcue-modify.md`](07-stagedirection-soundcue-modify.md)) already could.

The widened schema adds `sourceTitle`/`sourceType`, required unconditionally and mirroring `source`'s
own shape, so a no-id entry resolves through ADR 013's Type-anchored, Series-scoped matching algorithm
rather than a bare Name lookup.

Beyond the Fresh profile: **`Airplane!` must already exist as a Source**, which the bundled seed
supplies. Every fixture below resolves against it, so step 2 confirms it is present before anything
depends on it.

## Determinism

- **Every listing is scoped to this test's own `batchId` and read with `pageSize=0`.** Unscoped, the
  default page is 20 and any staged action left by an earlier run satisfies a `status=pending` or
  `status=blocked` check without this test having produced it — and the masterdata list holds hundreds
  of characters, so "includes X" read off page one is satisfied by X being on page twelve. Neither
  failure is visible in the output.
- **The Character chosen for the Modify half must not already be `Complete`.** The sequence is Modify →
  `Pending`, decide with `markCompletenessAs: "Complete"`, then Modify again → `Blocked`. Running this
  document against a database it had already run against would pick a Character the previous run marked
  `Complete`, so the first Modify would stage `Blocked` instead of `Pending` and the failure would look
  like a defect in the guard rather than in the setup. Creating the container and volume fresh in step 1
  is what makes that impossible.

**The explicit-id-on-Add half exists because a unit-test-only pass could not have caught the bug.** An
explicit `characters[]` id matching nothing was being silently discarded in favour of a freshly-computed
`EntityIdentity`-derived id — unlike `PlanSourcesAsync`'s established `canonicalId ?? EntityIdentity.SourceId(...)`
precedent. **The unit suite's own two tests for this were written against the bug and passed**, because
they never independently verified which id actually landed in the database. Only the walkthrough below
surfaces it.

- **The explicit id is uppercase in the file and looked up in lowercase.** Both castings are
  load-bearing; matching them removes the test's point.
- **The Source-casing fixture uses `AIRPLANE!` in both the quote's `source` and the character's
  `sourceTitle`.** Changing either to the stored casing tests nothing — and step 8's comparison is
  `-ceq`, because `-eq` is case-insensitive in PowerShell and would report a case-variant duplicate as
  the correctly-cased row.
- The Modify half asserts `ambiguousFields` contains **only** `name` — `sourceId` appearing would mean
  an unchanged `SourceId` is spuriously tripping `FieldMergeResolver`.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-12 --port 18612
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18612/api/v1"
$temp = "$PWD\.claude\temp"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Add a Character via natural key, with no id

```powershell
"airplaneSources=$(@((Invoke-RestMethod "$base/masterdata/sources?pageSize=0").items | Where-Object { $_.title -ceq 'Airplane!' }).Count)"

$add = @'
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000001","quote":"A #175 smoke test creation quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"name":"Smoke Test New Character","sourceTitle":"Airplane!","sourceType":"movie"}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-175-add.json", $add, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\smoke-175-add.json" --duplicate-resolution newest-wins --expect 200 --status

$characters = (Invoke-RestMethod "$base/masterdata/characters?pageSize=0").items
"created=$(@($characters | Where-Object { $_.name -ceq 'Smoke Test New Character' }).Count)"
$characterId = ($characters | Where-Object { $_.name -ceq 'Ted Striker' })[0].id
"characterId=$characterId"
```

**Expected:** `airplaneSources=1` before anything runs, the Add returns `200`, `created=1` — linked to
the existing `Airplane!` Source with no id supplied, resolved via ADR 013's algorithm finding no
candidate and then a genuine Add — and a non-empty `characterId` for the Modify half.

**On failure:** `airplaneSources=0` means the seed did not supply the Source every fixture here resolves
against, and every later step would then be creating one rather than matching it. Stop.

### 3. Correct an existing Character by id, under `review`

```powershell
$modify = @"
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000002","quote":"A #175 smoke test modify-trigger quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"id":"$characterId","name":"Renamed Via Smoke Test","sourceTitle":"Airplane!","sourceType":"movie"}]
}
"@
[IO.File]::WriteAllText("$temp\smoke-175-modify.json", $modify, [Text.UTF8Encoding]::new($false))

$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
              --file "$temp\smoke-175-modify.json" --duplicate-resolution review --expect 202 `
            | ConvertFrom-Json).batchId
$batchId
```

**Expected:** a non-empty `batchId` — the import staged under `review`.

This here-string is double-quoted (`@"…"@`) rather than literal, because `$characterId` has to be
substituted into it. Nothing else in the JSON begins with `$`.

### 4. List **only this batch's** pending actions

```powershell
$staged = (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").items
$staged | Select-Object entityType, actionType, ambiguousFields

$characterAction = ($staged | Where-Object { $_.entityType -eq 'Character' })[0]
$actionId = $characterAction.id
"actionId=$actionId ambiguous=$($characterAction.ambiguousFields -join ',')"
```

**Expected:** `ambiguous=name` — **only** `name` — since `sourceId` appearing would mean an unchanged
`SourceId` is spuriously tripping `FieldMergeResolver`, and `actionId` is non-empty.

### 5. Decide the ambiguous field, apply the batch, and read the Character back

```powershell
Invoke-RestMethod -Method Post -Uri "$base/import/actions/$actionId/decide" -Headers $key `
  -ContentType 'application/json' -Body '{"characterName":{"choice":"replace"},"markCompletenessAs":"Complete"}' | Out-Null

foreach ($id in (Invoke-RestMethod "$base/import/actions?status=Pending&batchId=$batchId&pageSize=0").items.id) {
  Invoke-RestMethod -Method Post -Uri "$base/import/actions/$id/decide" -Headers $key `
    -ContentType 'application/json' -Body '{"quoteText":{"choice":"keep"}}' | Out-Null
}

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/actions/apply?batchId=$batchId" --expect 200 --status
Invoke-RestMethod "$base/masterdata/characters/$characterId" | Select-Object id, name, completenessStatus
```

**Expected:** the apply returns `200`, and the Character reads back `Renamed Via Smoke Test` with
`completenessStatus` of `Complete`.

**The loop decides the fixture's own quote action**, which `apply` requires and which naming only the
Character action would leave pending — see [`07`](07-stagedirection-soundcue-modify.md) step 4 for the
same trap.

### 6. Attempt a *different* Modify against the now-`Complete` Character

**A third name, not the one step 5 just applied.** Re-importing `smoke-175-modify.json` unchanged
stages nothing at all: its `name` is now exactly what the row holds, so there is no field change for
the guard to block, and the blocked listing comes back empty for a reason that has nothing to do with
completeness. Measured during #339's full run, where that re-import produced `totalCount 0` and the
step read as a failure of the guard.

```powershell
$again = @"
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000003","quote":"A #175 smoke test third-name quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"id":"$characterId","name":"Renamed A Third Time","sourceTitle":"Airplane!","sourceType":"movie"}]
}
"@
[IO.File]::WriteAllText("$temp\smoke-175-modify-again.json", $again, [Text.UTF8Encoding]::new($false))

$blockedBatchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
                     --file "$temp\smoke-175-modify-again.json" --duplicate-resolution review `
                   | ConvertFrom-Json).batchId

$blocked = (Invoke-RestMethod "$base/import/actions?batchId=$blockedBatchId&pageSize=0").items
$blocked | Group-Object status | Select-Object Count, Name
"blockedCharacter=$(@($blocked | Where-Object { $_.entityType -eq 'Character' -and $_.status -eq 'Blocked' }).Count)"
(Invoke-RestMethod "$base/masterdata/characters/$characterId").name
```

**Expected:** `blockedCharacter=1` — a genuinely different `name` against a `Complete` row stages
**`Blocked`, not `Pending`** — and the Character still reads `Renamed Via Smoke Test`, not
`Renamed A Third Time`. That is the same guarantee Source, Person, StageDirection and SoundCue already
have.

**On failure:** `blockedCharacter=0` with an otherwise-empty tally is the tell that the fixture, not
the guard, is wrong — either the name in the file already matches the stored one, or nothing staged.
The full tally is printed alongside for exactly that reason.

### 7. Add a Character carrying an explicit uppercase id — the T2-only fix

```powershell
$explicit = @'
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000005","quote":"A #175 smoke test explicit-id-add quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"id":"F5111175-0000-4000-8000-000000000175","name":"Explicit Id Character","sourceTitle":"Airplane!","sourceType":"movie"}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-175-explicit-add.json", $explicit, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\smoke-175-explicit-add.json" --duplicate-resolution newest-wins --expect 200 --status

$explicitCharacter = dotnet script scripts/testing/http.csx -- `
  --url "$base/masterdata/characters/f5111175-0000-4000-8000-000000000175" --expect 200 | ConvertFrom-Json
"id=$($explicitCharacter.id) canonical=$($explicitCharacter.id -ceq 'f5111175-0000-4000-8000-000000000175')"
```

**Expected:** the explicit-id Add succeeds, the lowercase masterdata lookup returns `200`, and
`canonical=True` — the returned `id` is the lowercase-canonicalized form of the file's own id,
**never an unrelated `EntityIdentity`-derived one**.

### 8. Match an existing Source through a differently-cased title

```powershell
$casing = @'
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000006","quote":"A #175 smoke test source-casing quote.","originalLanguage":"en","source":"AIRPLANE!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"name":"Case Insensitive Source Character","sourceTitle":"AIRPLANE!","sourceType":"movie"}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-175-source-casing.json", $casing, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\smoke-175-source-casing.json" --duplicate-resolution newest-wins --expect 200 --status

$sources = (Invoke-RestMethod "$base/masterdata/sources?pageSize=0").items
"storedCasing=$(@($sources | Where-Object { $_.title -ceq 'Airplane!' }).Count)"
"uppercaseDuplicate=$(@($sources | Where-Object { $_.title -ceq 'AIRPLANE!' }).Count)"
```

**Expected:** `storedCasing=1` and `uppercaseDuplicate=0` — despite `AIRPLANE!` appearing in both the
quote's `source` and the character's `sourceTitle`, the entry resolved to the pre-existing Source
rather than creating a case-sensitive duplicate.

**Both comparisons are `-ceq`.** With `-eq` the two lines would count the same rows and always agree,
so the assertion could never fail — the casing is the entire subject here.

## Observed effect

Not yet established as a captured record. The id that lands in the database is the load-bearing
observation for the explicit-id half — the import reported success in the failing case too.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-12
Get-ChildItem $temp -Filter 'smoke-175-*.json' | Remove-Item -ErrorAction SilentlyContinue
```
