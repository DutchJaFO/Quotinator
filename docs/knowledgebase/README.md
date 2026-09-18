# Knowledgebase entries

Operator-facing answers: what a message means, whether it costs anything, and what to do about it. The
code protocol, the area list and the allocation rules live in [`../knowledgebase.md`](../knowledgebase.md);
this folder holds the content.

New entries start from [`entry-template.md`](entry-template.md). Entry codes are not allocated yet —
per `knowledgebase.md`'s bootstrapping rule, entries come first and codes are assigned during the sweep
[#333](https://github.com/DutchJaFO/Quotinator/issues/333) performs.

| Entry | Prevents functioning? | Affected versions |
|---|---|---|
| [A cancelled socket or transport connection](transport-connection-cancelled-during-a-request.md) | No | 1.9.0-alpha onwards |
| [An uploaded file is rejected as invalid JSON](uploaded-file-is-rejected-as-invalid-json.md) | No | up to 1.9.0-alpha |
| [Repeated "a suitable constructor could not be located" at startup](repeated-suitable-constructor-exceptions-at-startup.md) | No | 1.9.0-alpha only |
