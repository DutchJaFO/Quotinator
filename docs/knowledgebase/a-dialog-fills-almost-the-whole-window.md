# A dialog fills almost the whole window on a tall screen

**Kind:** diagnostic
**Entry code:** —
**Status code:** `QTN-KNOWN`
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** [#422](https://github.com/DutchJaFO/Quotinator/issues/422)

## Symptom

A dialog with more content than fits — the notification detail popup, or the startup popup with several
notifications — leaves only a narrow margin above and below it, rather than the 5% of the window height
the dialog is documented to leave. There is no message and nothing in the log; it is visible only by
looking at the window.

## Does it prevent the app or API from functioning?

**No.** The dialog's title row and its buttons stay on screen, its content scrolls, and it can always be
closed. On a window under roughly 1120 pixels tall there is no visible difference at all.

## Cause

The dialog is told to stop at 95% of the window height, and separately to be centred. The styling rule
that centres it also sets a minimum height of the window minus 56 pixels, and a minimum height wins over
a maximum, so the dialog ends up 56 pixels short of the window however tall the window is. Above about
1120 pixels that is taller than the 95% it was meant to stop at.

## Remedy

Nothing. Making the browser window shorter reduces the dialog with it, and no content becomes
unreachable at any size.

## Notes

Found 2026-09-22 while running the automated-test documents for
[#411](https://github.com/DutchJaFO/Quotinator/issues/411). Measured: 1218 pixels tall in a 1274-pixel
window, against the 1210 the 95% rule asks for; 364 in 420, and 664 in 720 — the window's height minus
56 each time. The entry exists because the code and its comments describe a cap that never takes effect,
so an operator comparing the two would find them disagreeing.
