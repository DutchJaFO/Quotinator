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
supplies. Every fixture below resolves against it, so confirm it is present before running them —
`curl -s "http://localhost:8080/api/v1/masterdata/sources?pageSize=0"`, the same call the last step
uses.

## Determinism

- **Every listing is scoped to this test's own `batchId` and read with `pageSize=0`.** Unscoped, the
  default page is 20 and any staged action left by an earlier run satisfies a `status=pending` or
  `status=blocked` check without this test having produced it — and the masterdata list holds hundreds
  of characters, so "includes X" read off page one is satisfied by X being on page twelve. Neither
  failure is visible in the output.
- **The Character chosen for the Modify half must not already be `Complete`.** The sequence is Modify →
  `Pending`, decide with `markCompletenessAs: "Complete"`, then Modify again → `Blocked`. Running this
  document twice against the same database picks a Character the previous run already marked
  `Complete`, so the first Modify stages `Blocked` instead of `Pending` and the failure looks like a
  defect in the guard rather than in the setup. Restore the Fresh profile first.

**The explicit-id-on-Add half exists because a unit-test-only pass could not have caught the bug.** An
explicit `characters[]` id matching nothing was being silently discarded in favour of a freshly-computed
`EntityIdentity`-derived id — unlike `PlanSourcesAsync`'s established `canonicalId ?? EntityIdentity.SourceId(...)`
precedent. **The unit suite's own two tests for this were written against the bug and passed**, because
they never independently verified which id actually landed in the database. Only the walkthrough below
surfaces it.

- **The explicit id is uppercase in the file and looked up in lowercase.** Both castings are
  load-bearing; matching them removes the test's point.
- **The Source-casing fixture uses `AIRPLANE!` in both the quote's `source` and the character's
  `sourceTitle`.** Changing either to the stored casing tests nothing.
- The Modify half asserts `ambiguousFields` contains **only** `name` — `sourceId` appearing would mean
  an unchanged `SourceId` is spuriously tripping `FieldMergeResolver`.

## Steps

**Add via natural key, no id:**

```bash
cat > .claude/temp/smoke-175-add.json <<'EOF'
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000001","quote":"A #175 smoke test creation quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"name":"Smoke Test New Character","sourceTitle":"Airplane!","sourceType":"movie"}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-add.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/masterdata/characters?pageSize=0" \
  | grep -c "Smoke Test New Character"
```

**Correct an existing Character by id, under `review`:**

```bash
cat > .claude/temp/smoke-175-modify.json <<'EOF'
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000002","quote":"A #175 smoke test modify-trigger quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"id":"<an existing Character id from the query above>","name":"Renamed Via Smoke Test","sourceTitle":"Airplane!","sourceType":"movie"}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-modify.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

Copy the `batchId` from that response, then list **only this batch's** pending actions:

```bash
curl -s "http://localhost:8080/api/v1/import/actions?status=pending&batchId=<batchId>&pageSize=0"
```

Decide and apply:

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
  -d '{"characterName":{"choice":"replace"},"markCompletenessAs":"Complete"}' \
  "http://localhost:8080/api/v1/import/actions/<id>/decide"
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply?batchId=<batchId>"
curl -s "http://localhost:8080/api/v1/masterdata/characters/<id>"
```

Then re-attempt another Modify against the same id under `review` — the same file, re-imported:

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-modify.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

Copy this second import's own `batchId`, then:

```bash
curl -s "http://localhost:8080/api/v1/import/actions?status=blocked&batchId=<second batchId>&pageSize=0"
```

**Explicit id honoured on Add — the T2-only fix:**

```bash
cat > .claude/temp/smoke-175-explicit-add.json <<'EOF'
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000005","quote":"A #175 smoke test explicit-id-add quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"id":"F5111175-0000-4000-8000-000000000175","name":"Explicit Id Character","sourceTitle":"Airplane!","sourceType":"movie"}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-explicit-add.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/characters/f5111175-0000-4000-8000-000000000175"
```

**Case-insensitive Source natural-key matching:**

```bash
cat > .claude/temp/smoke-175-source-casing.json <<'EOF'
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000006","quote":"A #175 smoke test source-casing quote.","originalLanguage":"en","source":"AIRPLANE!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"name":"Case Insensitive Source Character","sourceTitle":"AIRPLANE!","sourceType":"movie"}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-source-casing.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/masterdata/sources?pageSize=0"
```

## Expected output

- The Add returns `200`, and the `grep -c` for "Smoke Test New Character" reports `1` — linked to the
  existing `Airplane!` Source, with no id supplied, resolved via ADR 013's algorithm finding no candidate,
  then a genuine Add.
- The Modify returns `202` with one pending id, and `ambiguousFields` is `["name"]` **only**.
- After deciding and applying, `name` reads "Renamed Via Smoke Test" and `completenessStatus` is
  `Complete`.
- A further Modify under `review` stages **`Blocked`, not `Pending`**, and the on-disk name is
  unchanged — the same guarantee Source, Person, StageDirection and SoundCue already have.
- The explicit-id Add succeeds, and the lowercase masterdata lookup returns `200`. The returned `id` is
  the lowercase-canonicalized form of the file's own id — **never an unrelated `EntityIdentity`-derived
  one**.
- Despite `AIRPLANE!` appearing in both the quote's `source` and the character's `sourceTitle`, the
  Sources list still contains exactly one `"title":"Airplane!"` row — the entry resolved to the
  pre-existing Source rather than creating a case-sensitive duplicate.

## Observed effect

Not yet established as a captured record. The id that lands in the database is the load-bearing
observation for the explicit-id half — the import reported success in the failing case too.

## Cleanup

```bash
rm -f .claude/temp/smoke-175-*.json
```

Removing the fixtures does not undo what they imported. The quotes and Characters each file added, the
renamed Character, and the staged batches behind them all remain in the database. Restore the Fresh
profile before the next test.
