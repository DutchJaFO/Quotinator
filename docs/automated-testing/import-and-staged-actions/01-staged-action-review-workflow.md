# The staged review → decide → apply workflow, end to end

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #45, #149, #152, #154

## Preconditions

**Beyond the profile.** The container runs with `Quotinator__IncludeDefaultSources=false`, so nothing bundled is in
the database and every action this document reads is its own. Step 4 imports [a quote](fixtures/review-quote.json),
then [the same id with different text](fixtures/review-quote-conflict.json), so a broken import blocks this document.
That is the index's second honest resolution for a precondition, and it is named here so a blocked run reads as that.

`/api/v1/import/actions/*` (#154's unified staging engine) is the live mechanism: every import and seed
run stages through it.

## Determinism

- **Nothing here depends on a bundled file.** The conflict is between this document's two fixtures.
- **`review` policy is forced explicitly.** The conflicting text would otherwise be resolved on the spot,
  leaving nothing pending.
- **Every id and status filter is sent in both cases, and with a value that must not match** (#154). Nothing here
  assumes how a value is stored: the REST server is all this document can see. So `status` and `batchId` are read
  upper-cased and lower-cased in step 5, an action id is used in each case across steps 6 to 9, and `batchId` in each
  case on `apply` in step 8.
- **Apply is refused in both cases rather than succeeding in both**, because only one apply can succeed on a batch. The
  refusal names the pending action, so it shows the batch was found. It is also what separates the two forms from no
  match at all: step 8 applies a batch that does not exist and reads this batch's action unchanged.
- **The fixture stages exactly one pending `Quote`**, and the batch also carries its already-stored source as a no-op,
  so the final tally covers more actions than were ever pending.
- **Every listing is scoped to this batch's own `batchId`.** Step 4's first import writes actions of its own, already
  applied, and they are not this batch's.
- **The final status tally and the stored text are both read.** The decision here is
  `{"quoteText":{"choice":"keep"}}`, so a correct apply leaves the quote exactly as it was, which an apply writing
  nothing would too. `ImportActionStatus.Applied` is what says the write landed, and the unchanged text is what says
  the kept value is the one that survived.

## Steps

### 1. Create this test's own environment

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-import-01-out
dotnet script scripts/testing/test-env.csx -- create --name qt-import-01 --port 18601 --image quotinator:local --env Quotinator__IncludeDefaultSources=false
```

**Expected:** the app reports healthy.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Confirm the staging endpoint is reachable

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18601/api/v1/import/actions --expect 200 --status
```

**Expected:** `200`: the staging endpoint is reachable with no setup.

### 3. Confirm the legacy conflicts machinery is gone

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18601/api/v1/import/conflicts --expect 404 --status
```

**Expected:** `404`. It was removed entirely in #154 Phase B; anything else means the legacy
manual-review machinery has regressed back in.

### 4. Import the quote, then its conflict under forced `review`

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18601/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\import-and-staged-actions\fixtures\review-quote.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18601/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\import-and-staged-actions\fixtures\review-quote-conflict.json --duplicate-resolution review --expect 202 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\conflict.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\conflict.json --not-empty batchId --length-equals pendingActionIds=1
```

**Expected:** `200` for the quote, then `202`, **not** `200`, for the conflict: it is left `Pending` under `review`,
with a `batchId` every step below is scoped to.

**On failure:** a `200` on the second import means nothing was staged, so the rest of this document would be testing
an empty batch. Stop.

### 5. List this batch's pending actions

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18601/api/v1/import/actions?status=pending&batchId={batchId}&pageSize=0" --lower-value batchId=C:\repos\Quotinator\.claude\temp\qt-import-01-out\conflict.json#batchId --out C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-url.txt --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending.json --equals totalCount=1 --equals items.0.entityType=Quote --equals items.0.status=Pending
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18601/api/v1/import/actions?status=PENDING&batchId={batchId}&pageSize=0" --upper-value batchId=C:\repos\Quotinator\.claude\temp\qt-import-01-out\conflict.json#batchId --out C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-upper-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-upper-url.txt --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-upper.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-upper.json --equals totalCount=1 --equals items.0.entityType=Quote
dotnet script scripts/testing/http.csx -- --url "http://localhost:18601/api/v1/import/actions?status=pending&batchId=00000000-0000-4000-8000-000000000000&pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-other-batch.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-other-batch.json --equals totalCount=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18601/api/v1/import/actions?status=decided&batchId={batchId}&pageSize=0" --value batchId=C:\repos\Quotinator\.claude\temp\qt-import-01-out\conflict.json#batchId --out C:\repos\Quotinator\.claude\temp\qt-import-01-out\other-status-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\other-status-url.txt --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-other-status.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-other-status.json --equals totalCount=0
```

**Expected:** exactly the one quote action the conflict created, with `status` and `batchId` both lower-cased and both
upper-cased. A different batch and a different status each read none, so the filters are filtering.

### 6. Decide that action, and confirm it moved

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18601/api/v1/import/actions/00000000-0000-4000-8000-000000000000/decide --json-file C:\repos\Quotinator\docs\automated-testing\import-and-staged-actions\fixtures\decide-keep.json --expect 404 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18601/api/v1/import/actions/{actionId}/decide" --lower-value actionId=C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending.json#items.0.id --out C:\repos\Quotinator\.claude\temp\qt-import-01-out\decide-url.txt
dotnet script scripts/testing/http.csx -- --method POST --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\decide-url.txt --json-file C:\repos\Quotinator\docs\automated-testing\import-and-staged-actions\fixtures\decide-keep.json --expect 204 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18601/api/v1/import/actions?status=Decided&batchId={batchId}&pageSize=0" --value batchId=C:\repos\Quotinator\.claude\temp\qt-import-01-out\conflict.json#batchId --out C:\repos\Quotinator\.claude\temp\qt-import-01-out\decided-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\decided-url.txt --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\decided.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\decided.json --equals totalCount=1 --equals items.0.entityType=Quote
```

**Expected:** an action that does not exist answers `404`. The real one, with its id lower-cased, returns `204`, and the
`Decided` listing holds it.

### 7. Undo the decision

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18601/api/v1/import/actions/{actionId}/undo" --upper-value actionId=C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending.json#items.0.id --out C:\repos\Quotinator\.claude\temp\qt-import-01-out\undo-url.txt
dotnet script scripts/testing/http.csx -- --method POST --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\undo-url.txt --expect 204 --status
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-url.txt --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-after-undo.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-after-undo.json --equals totalCount=1 --equals items.0.status=Pending
```

**Expected:** `204` with the id upper-cased, and the action is back under `Pending`.

### 8. Confirm apply is refused while anything is pending

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18601/api/v1/import/actions/apply?batchId={batchId}" --upper-value batchId=C:\repos\Quotinator\.claude\temp\qt-import-01-out\conflict.json#batchId --out C:\repos\Quotinator\.claude\temp\qt-import-01-out\apply-url.txt
dotnet script scripts/testing/http.csx -- --method POST --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\apply-url.txt --expect 422 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\apply-refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\apply-refused.json --length-equals pendingActionIds=1
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18601/api/v1/import/actions/apply?batchId={batchId}" --lower-value batchId=C:\repos\Quotinator\.claude\temp\qt-import-01-out\conflict.json#batchId --out C:\repos\Quotinator\.claude\temp\qt-import-01-out\apply-lower-url.txt
dotnet script scripts/testing/http.csx -- --method POST --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\apply-lower-url.txt --expect 422 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\apply-refused-lower.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\apply-refused-lower.json --length-equals pendingActionIds=1
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18601/api/v1/import/actions/apply?batchId=00000000-0000-4000-8000-000000000000" --status
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-url.txt --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-after-other-apply.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-after-other-apply.json --equals totalCount=1 --equals items.0.status=Pending
```

**Expected:** `422` in both cases, naming the one undecided action. That is the batch-apply-atomicity contract working as
designed. Applying a batch that does not exist touches nothing: this batch's action is still `Pending`. That call's
status is printed, not asserted, because whether it should answer `200` is not settled.

### 9. Decide it again

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18601/api/v1/import/actions/{actionId}/decide" --upper-value actionId=C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending.json#items.0.id --out C:\repos\Quotinator\.claude\temp\qt-import-01-out\decide-upper-url.txt
dotnet script scripts/testing/http.csx -- --method POST --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\decide-upper-url.txt --json-file C:\repos\Quotinator\docs\automated-testing\import-and-staged-actions\fixtures\decide-keep.json --expect 204 --status
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-url.txt --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-none.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending-none.json --equals totalCount=0
```

**Expected:** `204` with the id upper-cased, and nothing left pending, so `apply` can succeed.

### 10. Apply the batch

```
dotnet script scripts/testing/http.csx -- --method POST --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\apply-url.txt --expect 200 --status
```

**Expected:** `200`, from the same upper-case URL step 8 was refused on.

### 11. Read the batch's final tally and the stored text

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18601/api/v1/import/actions?batchId={batchId}&pageSize=0" --value batchId=C:\repos\Quotinator\.claude\temp\qt-import-01-out\conflict.json#batchId --out C:\repos\Quotinator\.claude\temp\qt-import-01-out\batch-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\batch-url.txt --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\batch.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\batch.json --equals totalCount=2 --equals items.0.status=Applied --equals items.1.status=Applied
dotnet script scripts/testing/http.csx -- --url http://localhost:18601/api/v1/quotes/a4c17e30-6d2b-4e8a-9f15-3c70e1b8d401 --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\quote.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\quote.json --equals "quote=The original test text owned by the review document."
dotnet script scripts/testing/http.csx -- --url http://localhost:18601/api/v1/quotes/A4C17E30-6D2B-4E8A-9F15-3C70E1B8D401 --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-import-01-out\quote-upper.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-import-01-out\quote-upper.json --equals "quote=The original test text owned by the review document."
dotnet script scripts/testing/http.csx -- --url http://localhost:18601/api/v1/quotes/00000000-0000-4000-8000-000000000000 --expect 404 --status
```

**Expected:** both of the batch's actions read `Applied`. The quote still holds the text the decision kept, read with
its id in either case, and an id that does not exist answers `404`.

### 12. Nothing unexpected was thrown

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-import-01 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18601/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\startup-and-degradation\fixtures\malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-import-01 --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** no exception has been logged by anything so far, the refused apply included. The malformed upload then
answers `422` and its `JsonReaderException` is logged, which is the positive control: the same channel that read zero
can see an exception, so the zero means something.

**The count comes first**, because the upload throws more than the type its control names.

**On failure:** a count above zero is an issue, unless a Knowledgebase entry says otherwise. Report it with the
line that names it.

## Observed effect


**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`.

| Stage | Answer | This batch's actions |
|---|---|---|
| `GET /import/actions`, nothing imported | `200` | |
| `GET /import/conflicts` | `404` | |
| Import the quote | `200` | |
| Import the conflict under `review` | `202`, one `pendingActionIds` entry | |
| `status=pending`, `batchId` lower-cased | `200` | 1, `Quote`, `Pending` |
| `status=PENDING`, `batchId` upper-cased | `200` | 1, `Quote` |
| A `batchId` that does not exist | `200` | 0 |
| `status=decided` while it is pending | `200` | 0 |
| Decide an action that does not exist | `404` | |
| Decide, id lower-cased | `204` | 1 `Decided` |
| Undo, id upper-cased | `204` | 1 `Pending` |
| Apply, `batchId` upper-cased, one still pending | `422`, one `pendingActionIds` entry | |
| Apply, `batchId` lower-cased, one still pending | `422`, one `pendingActionIds` entry | |
| Apply a batch that does not exist | `200` | 1 `Pending`, untouched |
| Decide, id upper-cased | `204` | 0 `Pending` |
| Apply, `batchId` upper-cased | `200` | 2, both `Applied` |

`GET /quotes/{id}` read the original text the decision kept with the id in lower case and in upper case, and answered
`404` for an id that does not exist.

**No exception was logged before the malformed upload**, the refused apply included. That upload's
`JsonReaderException` was then logged once.

## Canary

Build the commit before [#154](https://github.com/DutchJaFO/Quotinator/issues/154)'s first code
change, under a distinct tag, and run steps 2 to 11 against it. The staged decide and apply endpoints
are what #154 completed, so step 5 has no listing to read and steps 6 to 10 have nothing to decide,
undo or apply.

**Step 3 asserts that the legacy conflicts machinery is gone, and step 2 is what stops that passing
for the wrong reason.** An absence is also what a container with no import surface at all reports, so
the staging endpoint is confirmed reachable first. On the pre-fix build step 3 inverts: the legacy
machinery answers, which is the state this workflow replaced.

Step 7 is the pair for step 6: a decision that can be made and then undone, rather than only made. A
build that recorded decisions without being able to reverse them satisfies step 6 alone.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-01
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-import-01-out
```

**Expected:** the container, its volume and the output directory are gone.
