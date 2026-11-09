# A page capture starts a fresh browser because the page produced no frames

**Kind:** diagnostic
**Entry code:** —
**Status code:** `QTN-KNOWN`
**Affected versions:** the repository's test tooling (`scripts/testing/capture-page.csx`), not the application
**GitHub issue:** [#489](https://github.com/DutchJaFO/Quotinator/issues/489), and [#488](https://github.com/DutchJaFO/Quotinator/issues/488), in whose real browser windows its cause is investigated

## Symptom

`capture-page.csx` reports on stderr, during a step of a `docs/automated-testing/` document:

```
The page produced no frames; starting a fresh browser (2 of 5).
```

When every attempt stalls, the capture fails and exits `1`:

```
The page produced no frames in 5 browsers; a screenshot would never be answered.
```

## Does it prevent the app or API from functioning?

**No.** The application is not involved: this is the tool that loads a page in a browser and writes its screenshot for a test document. What it costs is the time each fresh browser takes to start and load the page, and, when all five browsers stall, a failed test step that says nothing about the application.

## Cause

The browser stops producing frames for the page it opened, so a screenshot request is never answered. Why it stops is not known. Measured 2026-11-09 with Edge: 2 to 4 of every 10 page loads stalled, headless or in a visible window, and the stall was there before any page script ran. The measurements and what was ruled out are in #489.

## Remedy

Nothing is needed while a fresh browser succeeds: the capture then completes and the step's result stands. If a capture fails with *produced no frames in 5 browsers*, run that capture command again, not the whole step: the stall is found before the page script runs, so the failed capture changed nothing, while the step's earlier commands may have. The failure is the tool's, not the application's.

## Notes

Before 2026-11-09 the tool waited forever on an unanswered screenshot, and stopping it left the page's connection to the application half-open, which the container logged as a `WebSocketException` four times. Commit `32e4e933` added the fresh-browser workaround, with a timeout on every browser call and a proper close on every path.
