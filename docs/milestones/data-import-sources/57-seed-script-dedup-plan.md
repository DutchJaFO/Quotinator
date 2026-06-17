# #57 — Seed script: dedup inconsistent

**Status:** Closed by design  
**GitHub issue:** #57  
**Closed by:** #61

---

## Original concern

The seed script accumulated duplicates because the old `data/quotes.json` was regenerated from multiple sources without consistent deduplication. Two different datasets could emit the same quote text with different IDs.

## Resolution

#61 (one file per source) eliminates the concern architecturally: each source dataset now writes its own file. Cross-file deduplication happens in `DatabaseInitializer` at seeding time, not in the seed script. Duplicates are identified by quote text similarity and resolved by the conflict-resolution policy (#64).

## Verification

- [ ] Confirm `gh issue close` was run with a justification comment referencing #61
- [ ] No `data/quotes.json` exists in the repo
- [ ] `dotnet-script scripts/seed.csx` produces separate per-source files without a combined output

## Notes

This issue does not need implementation work. If it was not formally closed on GitHub with a `--comment`, do so:

```
gh issue close 57 --comment "Closed by design: #61 writes one file per source; cross-file dedup is handled in DatabaseInitializer at seed time."
```
