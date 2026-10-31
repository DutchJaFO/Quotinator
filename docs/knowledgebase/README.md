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
| [A quote id that worked before an upgrade answers 404](a-quote-id-stops-working-after-an-upgrade.md) | No | upgrades from 1.8.2 or earlier |
| [Content was not loaded, or a migration did not run, because no backup could be taken](no-backup-could-be-taken.md) | Yes, when a migration was refused | 1.9.0-alpha onwards |
| [Backups have reached their quota](backups-have-reached-their-quota.md) | No | 1.9.0-alpha onwards |
| [Every database count reads 0 after a startup problem](degraded-startup-shows-every-count-as-zero.md) | No | 1.9.0-alpha onwards |
| [Two backups of the same database show different version numbers](two-backups-of-one-database-show-different-versions.md) | No | v1.8.0 through 1.9.0-alpha |
| [A correction that only changes capitalisation does not take effect](a-casing-correction-cannot-be-made.md) | No | 1.8.0 onwards |
| [Every page shows an error after startup, while /health says the data directory cannot be written](every-page-fails-when-the-keys-directory-cannot-be-created.md) | Yes | 1.9.0-alpha onwards |
| [Startup reports that database initialisation failed, and a Reset is refused](the-database-file-cannot-be-read.md) | Yes | 1.9.0-alpha onwards |
| [A test environment reports it was removed, but docker could not remove it](a-test-environment-reports-removal-that-did-not-happen.md) | No | test tooling, not the application |
| [A source refresh test fails when GitHub does not answer](a-source-refresh-test-fails-when-github-does-not-answer.md) | No | test suite, not the application |
| [/version reports old counts after an import](version-counts-are-stale-after-an-import.md) | No | 1.9.0-alpha, the only version measured |
| [Applying an import batch answers 200, but nothing was imported](applying-a-batch-that-does-not-exist-answers-200.md) | No | 1.9.0-alpha, the only version measured |
