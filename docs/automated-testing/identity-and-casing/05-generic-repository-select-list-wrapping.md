# Every generic-repository-backed endpoint returns correct data and lowercase ids

**Smoke:** no
**Environment:** Fresh
**Traces to:** #210

## Preconditions

Nothing beyond the Fresh profile — its first-boot seed of the bundled files is what populates every
entity below, the curated file's Conversations, StageDirections and SoundCues included.

**This test can therefore be blocked.** That content arrives through the application's own import
path, so a defect there takes this test down with it even though the generic-repository queries may be
perfectly intact. That is the second of the index's two honest resolutions, not the first — a skipped
run here reads as a known consequence of a broken import, not an unexplained gap.

`RepositorySql.cs`'s generic queries (`SelectById`, `SelectByIds`, `SelectDeleted`,
`SelectByForeignKey`, `SelectJunctionRow`, `SelectPage`) build an explicit column list via a
caller-supplied `IEntityColumnMetadata` rather than `SELECT *`, wrapping every id column the way
hand-written `Sql.cs` queries do. This confirms the `SELECT *` removal did not break any of them.

## Determinism

- **`items` must be populated on every endpoint**, not merely present. An empty page returns `200` and
  asserts nothing about column wrapping, which is why the count is printed per endpoint rather than
  the status code alone.
- **The casing comparison is `-cne`.** `-ne` is case-insensitive in PowerShell, so with it an
  uppercase id would compare equal to its own lowercase form and the assertion could never fail.
- These endpoints are the **only** live paths that exercise `RepositorySql`'s rewritten queries end to
  end, via `SqliteRepository<T>`/`SqliteRestorableRepository<T>`'s generic `GetPageAsync`/`GetByIdAsync`.
  Characters additionally exercise `SqliteLinkRepository` through the `Quotinator_CharacterSource`
  many-to-many link.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-id-05 --port 18205
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Page every generic-repository-backed list endpoint

```powershell
$paths = 'masterdata/sources', 'masterdata/characters', 'masterdata/people', 'masterdata/series',
         'masterdata/universes', 'conversations', 'masterdata/stagedirections', 'masterdata/soundcues'
foreach ($path in $paths) {
  $page = Invoke-RestMethod "http://localhost:18205/api/v1/$path`?pageSize=2"
  $wrong = @($page.items.id | Where-Object { $_ -cne $_.ToLowerInvariant() })
  "$path items=$(@($page.items).Count) notLowercase=$($wrong.Count)"
}
```

**Expected:** every endpoint reports a non-zero `items` and `notLowercase=0`.

**On failure:** `items=0` is not a pass — it asserts nothing about column wrapping, and points at the
seed rather than at the queries under test (see Preconditions: this test can be blocked by a broken
import). Stop; step 3 has no id to take.

### 3. Fetch one Source by id in both casings

Take one of the returned ids and fetch it both ways, confirming `GetByIdAsync`'s case-insensitive
lookup survived the rewrite:

```powershell
$sourceId = (Invoke-RestMethod "http://localhost:18205/api/v1/masterdata/sources?pageSize=1").items[0].id
$upper = $sourceId.ToUpperInvariant()
"sourceId=$sourceId upper=$upper"

$lowerCall = dotnet script scripts/testing/http.csx -- --url "http://localhost:18205/api/v1/masterdata/sources/$sourceId" --expect 200 | ConvertFrom-Json
$upperCall = dotnet script scripts/testing/http.csx -- --url "http://localhost:18205/api/v1/masterdata/sources/$upper"    --expect 200 | ConvertFrom-Json
"sameRecord=$($lowerCall.title -ceq $upperCall.title) bothLowercase=$(($lowerCall.id -ceq $sourceId) -and ($upperCall.id -ceq $sourceId))"
```

**Expected:** both calls return `200`, `sameRecord=True`, and `bothLowercase=True` — the record's `id`
renders lowercase regardless of the casing requested.

**On failure:** if `$upper` equals `$sourceId`, the id drawn from the page happened to be all digits
and there is nothing to case-flip. Take a different one rather than recording a pass.

## Observed effect

Not yet established. The responses are asserted above; nothing has been captured about what the
container emits while serving them.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-id-05
```
