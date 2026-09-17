# Startup logs repeated "a suitable constructor could not be located"

**Kind:** diagnostic
**Entry code:** —
**Status code:** —
**Affected versions:** 1.9.0-alpha only
**GitHub issue:** [#397](https://github.com/DutchJaFO/Quotinator/issues/397)

## Symptom

Six `Error` lines during startup, before the application reports ready:

```
[Runtime - Exception] a063374a thrown: InvalidOperationException
System.InvalidOperationException: A suitable constructor for type
'Quotinator.Api.Endpoints.Filters.AdminApiKeyFilter' could not be located.
```

## Does it prevent the app or API from functioning?

**No.** The application starts, reports healthy, and admin endpoints authenticate normally. Six is one
per admin endpoint group.

## Cause

ASP.NET Core's `AddEndpointFilter<T>()` builds its filter factory by first probing for a constructor
taking `EndpointFilterFactoryContext`, catching the failure and falling back to the parameterless one
(dotnet/runtime#67309). The filter had no such constructor, so each of the six registrations threw once.

## Remedy

Upgrade. The filter is now registered once and handed to each group as an instance, which activates
nothing and throws nothing. No operator action is needed on an affected build.

## Notes

Only ever visible in 1.9.0-alpha: earlier builds threw the same six exceptions and logged none of them,
and later builds do not throw them at all.
