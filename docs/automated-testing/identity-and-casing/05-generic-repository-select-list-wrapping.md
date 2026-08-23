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
  asserts nothing about column wrapping.
- These endpoints are the **only** live paths that exercise `RepositorySql`'s rewritten queries end to
  end, via `SqliteRepository<T>`/`SqliteRestorableRepository<T>`'s generic `GetPageAsync`/`GetByIdAsync`.
  Characters additionally exercise `SqliteLinkRepository` through the `Quotinator_CharacterSource`
  many-to-many link.

## Steps

### 1. Create this test's own environment

```bash
docker rm -f qt-id-05 2>/dev/null; docker volume rm qt-id-05-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-id-05 -p 18205:8080 -v qt-id-05-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true \
  quotinator:local
until curl -sf http://localhost:18205/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Page every generic-repository-backed list endpoint

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:18205/api/v1/masterdata/sources?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:18205/api/v1/masterdata/characters?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:18205/api/v1/masterdata/people?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:18205/api/v1/masterdata/series?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:18205/api/v1/masterdata/universes?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:18205/api/v1/conversations?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:18205/api/v1/masterdata/stagedirections?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:18205/api/v1/masterdata/soundcues?pageSize=2"
```

**Expected:** every list call returns `200` with populated `items` and lowercase `id` fields.

**On failure:** a `200` carrying an empty `items` is not a pass — it asserts nothing about column
wrapping, and points at the seed rather than at the queries under test (see Preconditions: this test
can be blocked by a broken import). Stop; step 3 has no id to take.

### 3. Fetch one Source by id in both casings

Take one of the returned ids and fetch it both ways, confirming `GetByIdAsync`'s case-insensitive
lookup survived the rewrite:

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:18205/api/v1/masterdata/sources/<id-as-returned>"
curl -s -w "\n%{http_code}\n" "http://localhost:18205/api/v1/masterdata/sources/<same-id-uppercased>"
```

**Expected:** both `GET .../sources/{id}` calls return `200` with the same record, and its `id` renders
lowercase regardless of the casing requested.

## Observed effect

Not yet established. The responses are asserted above; nothing has been captured about what the
container emits while serving them.

## Cleanup

```bash
docker rm -f qt-id-05 2>/dev/null
docker volume rm qt-id-05-data 2>/dev/null
```
