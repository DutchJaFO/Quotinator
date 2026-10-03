# Knowledgebase entries

Operator-facing answers: what a message means, whether it costs anything, and what to do about it. The
code protocol, the area list and the allocation rules live in [`../knowledgebase.md`](../knowledgebase.md);
this folder holds the content.

New entries start from [`entry-template.md`](entry-template.md). Entry codes are not allocated yet:
per `knowledgebase.md`'s bootstrapping rule, entries come first and codes are assigned during the sweep
[#333](https://github.com/DutchJaFO/Quotinator/issues/333) performs.

| Entry | Prevents functioning? | Affected versions |
|---|---|---|
| [An antiforgery token could not be decrypted](antiforgery-token-could-not-be-decrypted.md) | No | 1.9.0-alpha onwards |
| [A cancelled socket or transport connection](transport-connection-cancelled-during-a-request.md) | No | 1.9.0-alpha onwards |
| [An uploaded file is rejected as invalid JSON](uploaded-file-is-rejected-as-invalid-json.md) | No | up to 1.9.0-alpha |
| [Repeated "a suitable constructor could not be located" at startup](repeated-suitable-constructor-exceptions-at-startup.md) | No | 1.9.0-alpha only |
| [Generating conflict rules answers 500 for a source](generating-conflict-rules-answers-500.md) | Yes, for that endpoint | 1.9.0-alpha onwards |
| [A quote id that worked before an upgrade answers 404](a-quote-id-stops-working-after-an-upgrade.md) | No | upgrades from 1.8.2 or earlier |
| [A dialog fills almost the whole window on a tall screen](a-dialog-fills-almost-the-whole-window.md) | No | 1.9.0-alpha onwards |
| [Quotes come back after a restart, although the database was reset](a-reset-is-undone-by-the-next-restart.md) | No | 1.9.0-alpha onwards |
| [Content was not loaded, or a migration did not run, because no backup could be taken](no-backup-could-be-taken.md) | Yes, when a migration was refused | 1.9.0-alpha onwards |
| [Backups have reached their quota](backups-have-reached-their-quota.md) | No | 1.9.0-alpha onwards |
| [Every database count reads 0 after a startup problem](degraded-startup-shows-every-count-as-zero.md) | No | 1.9.0-alpha onwards |
| [Two backups of the same database show different version numbers](two-backups-of-one-database-show-different-versions.md) | No | v1.8.0 through 1.9.0-alpha |
