# Every generic-repository-backed endpoint returns correct data and lowercase ids

**Smoke:** no
**Environment:** Fresh
**Traces to:** #210

## Preconditions

A running container with a populated database — every endpoint below must have rows, or a `200` with
empty `items` would pass while proving nothing.

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

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/sources?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/characters?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/people?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/series?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/universes?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/conversations?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/stagedirections?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/soundcues?pageSize=2"
```

Then confirm `GetByIdAsync`'s case-insensitive lookup survived the rewrite — take one of the returned
ids and fetch it both ways:

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/sources/<id-as-returned>"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/sources/<same-id-uppercased>"
```

## Expected output

Every list call returns `200` with populated `items` and lowercase `id` fields.

Both `GET .../sources/{id}` calls return `200` with the same record, and its `id` renders lowercase
regardless of the casing requested.

## Observed effect

Not yet established. The responses are asserted above; nothing has been captured about what the
container emits while serving them.

## Cleanup

None — this test only reads.
