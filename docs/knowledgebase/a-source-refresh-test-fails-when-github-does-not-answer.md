# A source refresh test fails when GitHub does not answer

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** the repository's test suite (`docs/automated-testing/startup-and-degradation/07`), not the application
**GitHub issue:** [#462](https://github.com/DutchJaFO/Quotinator/issues/462)

## Symptom

Step 2 of `startup-and-degradation/07` fails: `POST /api/v1/admin/sources/refresh?force=true` names the outcome
`failed` where the step expects `updated`, or fewer than two files reach `sources/download`.

## Does it prevent the app or API from functioning?

**No.** This is a limit of the test, not a defect in the application. What it costs is a red reading that says
nothing about this project, so the step cannot be read as a regression on its own.

## Cause

The step reaches `raw.githubusercontent.com`, because the bundled manifest names it, and whether GitHub answers
during a run cannot be chosen. When it does not answer, a refresh that could not download cannot show that it
would have.

Those two readings have other causes as well: a refresh that fails for a reason of this project's own looks
the same from this step alone.

## Remedy

Wait and run the document again. If it passes, nothing further is needed, and no new issue should be filed for
it: #462 already covers this.

If it keeps failing, confirm whether GitHub is reachable from the machine. A failure while GitHub is reachable
is not this entry, and is reported in its own right.

## Notes

#462 replaces the dependency with a dummy source server in a Docker image under this project's control, and
this entry goes when that lands. A `failed` reading is not a pass: the index's
*A test that cannot be confirmed has failed* still applies.
