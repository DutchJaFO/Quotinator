using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Filters;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Database;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Csv;
using Quotinator.Data.Import;
using Quotinator.Api.Logging;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/import</c> endpoints — file import (#45, #65) and the staged-action review workflow (#154).</summary>
internal static class ImportEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    internal static void MapImportEndpoints(this WebApplication app)
    {
        // Read-only listing — no API key required, matches GET /admin/audit's precedent.
        var publicGroup = app.MapGroup("/api/v1/import")
                             .WithTags(ApiTags.Import)
                             .RequireRateLimiting(RateLimitPolicies.Admin);

        // Every write here mutates staged or real data — requires X-Api-Key, matches reseed/reset/refresh's precedent.
        var adminGroup = app.MapGroup("/api/v1/import")
                            .WithTags(ApiTags.Import)
                            .RequireRateLimiting(RateLimitPolicies.Admin)
                            .AddEndpointFilter<AdminApiKeyFilter>()
                            .WithMetadata(AdminApiKeyRequiredMarker.Instance);

        const string ImportDescription =
            "Imports every quote in the uploaded `file` — the same duplicate-detection/merge engine the startup seeder uses, applied to " +
            "one file at a time. `settings` (optional, JSON text field) may set `converter` (name of a compiled `IQuoteSourceConverter` " +
            "plugin, e.g. `csv` — omit when `file` is already Quotinator's canonical JSON schema), `duplicateResolution` (a policy object — " +
            "`default` plus optional per-entity-type overrides — overriding `Quotinator:DefaultConflictPolicy` for this run only), and " +
            "`enrich` (currently always `501 Not Implemented` when `true`; reserved for #19). " +
            "Malformed `settings`, an unrecognised `converter` name, or file content that converts to zero valid quotes all return `422`. " +
            "A row missing a quote/source or with an invalid `id` is skipped and reported in `errors` — one bad row never aborts the rest of the file. " +
            "`report` gives a per-entity-type new/modified/blocked/discarded/pending/stale action breakdown for this file (issue #221), " +
            "distinct from `summary` (Quote-only row counts including validation errors). " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.";

        adminGroup.MapPost("/preview", (
                [Description("The source file to import — Quotinator's canonical JSON schema, or a raw upstream format when `settings.converter` names a compiled converter.")] IFormFile file,
                [Description("Optional JSON text field: `converter`, `duplicateResolution` (policy object), `enrich` (boolean).")] [FromForm] string? settings,
                IQuoteImportService importService,
                IApiLocalizer localizer,
                ILogger<Log> logger,
                CancellationToken cancellationToken) =>
                    HandleImportAsync(file, settings, importService, localizer, logger, preview: true, purgeOnSuccess: false, cancellationToken))
             .DisableAntiforgery()
             .Produces<ImportResultResponse>(StatusCodes.Status200OK)
             .Produces<ImportResultResponse>(StatusCodes.Status202Accepted)
             .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
             .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
             .Produces<ProblemDetails>(StatusCodes.Status501NotImplemented)
             .WithName("PreviewImportQuotes")
             .WithSummary("Preview a quote import")
             .WithDescription(
                 "Runs the full import pipeline and stages exactly what it would do, then never applies — a real, inspectable " +
                 "`ImportBatch` is created (review it via `GET /api/v1/import/actions?batchId=`), but nothing is written to any " +
                 "quote data. Returns `200` when the batch would apply cleanly as-is, or `202` when any row needs a decision " +
                 "(adjust the file, or decide the ambiguous rows via `POST /api/v1/import/actions/{id}/decide`). Once ready, " +
                 "either re-run `POST /api/v1/import` with the same file to stage-and-apply in one call, or apply the already-" +
                 "staged batch directly via `POST /api/v1/import/actions/apply`. " + ImportDescription);

        adminGroup.MapPost("/", (
                // string?, not Guid? — a nullable value-type query parameter throws BadHttpRequestException
                // on any binding quirk (same reasoning as the yearFrom/yearTo/page/pageSize pattern in
                // QuoteEndpoints.cs), which the global BadRequestExceptionHandler safety net then reports as
                // a generic, misleading "numeric parameters" 422. Parsed explicitly below instead.
                [Description("Applies an already-staged batch (from a prior `/import` or `/import/preview` call) instead of uploading a file — alias for `POST /import/actions/apply` that returns the same response shape the file-upload mode does.")] string? batchId,
                [Description("#249: when true and the call results in the batch having zero pending actions, that batch's conflict-resolution history (Import_Action rows) is purged immediately. Forfeits `POST /import/actions/reverse` for this batch.")] bool? purgeOnSuccess,
                // HttpRequest, not a bound IFormFile?/[FromForm] string? pair — this route accepts a request
                // with no body at all in batchId mode. Minimal API's automatic form binding always requires
                // a form content-type to even attempt binding; a request with no Content-Type/body fails
                // that check at the framework's own routing/binding layer (not as a normal thrown exception),
                // bypassing BadRequestExceptionHandler entirely and producing a bare, uninformative 400.
                // Reading the request manually — only when batchId is absent — lets us return a clear 422
                // instead of that generic 400, and never touches the body at all in batchId mode.
                HttpRequest request,
                IQuoteImportService importService,
                IApiLocalizer localizer,
                ILogger<Log> logger,
                CancellationToken cancellationToken) =>
                    batchId is not null
                        ? HandleApplyBatchAsync(batchId, purgeOnSuccess ?? false, importService, localizer, logger, cancellationToken)
                        : HandleImportFromRequestAsync(request, purgeOnSuccess ?? false, importService, localizer, logger, cancellationToken))
             .DisableAntiforgery()
             .Accepts<IFormFile>("multipart/form-data")
             .Produces<ImportResultResponse>(StatusCodes.Status200OK)
             .Produces<ImportResultResponse>(StatusCodes.Status202Accepted)
             .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
             .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
             .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
             .Produces<ProblemDetails>(StatusCodes.Status501NotImplemented)
             .WithName("ImportQuotes")
             .WithSummary("Import quotes, or apply an already-staged batch")
             .WithDescription(
                 "Two modes on one route, distinguished by whether `batchId` is present: " +
                 "**file mode** (`file` required, `batchId` omitted) stages the file, then immediately attempts to apply it " +
                 "(two sequential commits — a crash between them leaves the batch `Staged`, a safe, recoverable state); " +
                 "**batch mode** (`batchId` given, `file`/`settings` ignored) applies a batch already staged by a prior " +
                 "`/import` or `/import/preview` call — identical to `POST /import/actions/apply` but returning the same " +
                 "response envelope shape as file mode, for a consistent contract regardless of which mode was used. " +
                 "Returns `404` if `batchId` doesn't exist, or `422` if neither `file` nor `batchId` is given. Either mode " +
                 "returns `200` when everything applied, or `202` when any row needs a decision (adjust the file and " +
                 "re-import, or decide the ambiguous rows via `POST /api/v1/import/actions/{id}/decide` then re-apply). " +
                 "`purgeOnSuccess=true` (#249) purges the batch's conflict-resolution history (`Import_Action` rows) " +
                 "immediately once it applies fully — has no effect on a `202` response, since nothing applied. " +
                 "Purging forfeits `POST /import/actions/reverse` for that batch, since it depends on those same rows. " +
                 ImportDescription);

        // ── #154: unified staging engine — /import/actions/* ────────────────────

        publicGroup.MapGet("/actions", async (
            string? status,
            string? batchId,
            string? entityType,
            IImportActionService service,
            IApiLocalizer localizer,
            [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
            [Description("Number of actions per page (0–500). 0 means every matching action as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null) =>
        {
            if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
                return pageError!;

            var result = await service.GetPagedAsync(batchId, status, entityType, pageValue, pageSizeValue);

            return PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer)
                ?? Results.Ok(result);
        })
        .WithName("GetImportActions")
        .WithSummary("List staged import actions")
        .WithDescription(
            "Returns a paginated list of staged import actions (#154), newest first — the review " +
            "surface for a staged batch, whether staged via `POST /import`, `POST /import/preview`, " +
            "or startup seeding. Filter by `status` (`Pending`, `Decided`, `Applied`, `Discarded`, " +
            "`Blocked`, `Stale`), `batchId`, and/or `entityType` (`Quote`, `Source`, `Character`, " +
            "`Person`). Each item includes `relatedActionIds` (the Source/Character/Person actions in " +
            "the same batch a Quote action depends on) and `ambiguousFields` (the fields genuinely " +
            "needing a decision, populated only while `status` is `Pending`). Maximum `pageSize` is 500.");

        publicGroup.MapGet("/actions/export", async (
            string? batchId,
            string? format,
            IImportActionService service,
            IApiLocalizer localizer) =>
        {
            if (string.IsNullOrWhiteSpace(batchId))
                return Results.Problem(detail: localizer[ApiMessages.ImportActionBatchIdRequired], statusCode: StatusCodes.Status422UnprocessableEntity);

            var normalizedFormat = (format ?? "json").ToLowerInvariant();
            if (normalizedFormat is not ("json" or "csv"))
                return Results.Problem(detail: localizer[ApiMessages.ImportActionExportUnknownFormat], statusCode: StatusCodes.Status422UnprocessableEntity);

            var rows = await service.ExportBatchAsync(batchId);

            if (normalizedFormat == "csv")
            {
                var csvRows = new List<IEnumerable<string?>> { ImportActionFieldRowMapper.CsvHeader };
                csvRows.AddRange(rows.Select(ImportActionFieldRowMapper.ToCsvRow));
                return Results.Text(CsvLineWriter.Write(csvRows), "text/csv");
            }

            return Results.Ok(rows);
        })
        .Produces<IReadOnlyList<ImportActionFieldRowResponse>>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithName("ExportImportActionBatch")
        .WithSummary("Export a staged batch's decidable fields as a flat file")
        .WithDescription(
            "Returns every decidable field across `batchId`'s `Pending`, `Decided`, `Blocked`, and " +
            "`Stale` Modify actions, one row per field — the flat format `POST /import/actions/bulk-decide` " +
            "reads back, for reviewing or revising many decisions at once outside the API (spreadsheet, " +
            "script, etc.) instead of one `POST /import/actions/{id}/decide` call per action. " +
            "`format` (`json`, the default, or `csv`) controls the response shape; both use the same " +
            "flat `ActionId, EntityId, EntityType, Field, ExistingValue, IncomingValue, Decision, " +
            "CustomValue, MarkCompletenessAs` row shape. A `Decided` action's `Decision`/`CustomValue` " +
            "reflect the caller's actual prior per-field choice, not an inference from the resolved " +
            "value. `MarkCompletenessAs` repeats the same value on every row belonging to the same " +
            "`ActionId`. Returns `422` if `batchId` is missing or `format` isn't `json`/`csv`. An " +
            "unknown `batchId` returns `200` with zero rows, matching `GET /import/actions`'s own " +
            "behaviour for an unknown `batchId`. No `X-Api-Key` required, matching `GET /import/actions`'s precedent.");

        adminGroup.MapPost("/actions/bulk-decide", async (
                string? batchId,
                string? format,
                // HttpRequest, not a bound IFormFile?/[FromForm] parameter — mirrors POST /import's
                // own fix (see that route's comment above): minimal API's automatic form binding
                // always requires a form content-type to even attempt binding, so a request with no
                // Content-Type/body at all fails at the framework's own routing/binding layer, not as
                // a normal thrown exception — bypassing BadRequestExceptionHandler entirely and
                // producing a bare, uninformative 400 instead of this endpoint's own 422s. Found live
                // via T2 Docker testing.
                HttpRequest request,
                IImportActionService service,
                IApiLocalizer localizer) =>
            {
                if (string.IsNullOrWhiteSpace(batchId))
                    return Results.Problem(detail: localizer[ApiMessages.ImportActionBatchIdRequired], statusCode: StatusCodes.Status422UnprocessableEntity);

                if (!request.HasFormContentType)
                    return Results.Problem(detail: localizer[ApiMessages.ImportFileMissing], statusCode: StatusCodes.Status422UnprocessableEntity);

                var form = await request.ReadFormAsync();
                var file = form.Files["file"];
                if (file is null || file.Length == 0)
                    return Results.Problem(detail: localizer[ApiMessages.ImportFileMissing], statusCode: StatusCodes.Status422UnprocessableEntity);

                var normalizedFormat = (format ?? "json").ToLowerInvariant();
                if (normalizedFormat is not ("json" or "csv"))
                    return Results.Problem(detail: localizer[ApiMessages.ImportActionExportUnknownFormat], statusCode: StatusCodes.Status422UnprocessableEntity);

                using var reader = new StreamReader(file.OpenReadStream());
                var content = await reader.ReadToEndAsync();

                var (parsedRows, parseErrors) = normalizedFormat == "csv" ? ParseCsvRows(content) : ParseJsonRows(content);
                var result = await service.BulkDecideAsync(batchId, parsedRows);

                return Results.Ok(new BulkDecideResponse
                {
                    RowsProcessed  = parsedRows.Count + parseErrors.Count,
                    ActionsDecided = result.ActionsDecided,
                    Errors         = [.. parseErrors, .. result.Errors],
                });
            })
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<BulkDecideResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .WithName("BulkDecideImportActions")
            .WithSummary("Apply many staged-action decisions from an edited export file")
            .WithDescription(
                "Reads the uploaded, edited `GET /import/actions/export` file back and applies each " +
                "row's `Decision`/`CustomValue`, grouped by `ActionId` — one `POST /import/actions/{id}" +
                "/decide` call per action, reusing the same validation that endpoint already applies " +
                "(no new validation logic). Deciding a `Blocked` or `Stale` action works exactly like " +
                "deciding a `Pending` one. A malformed row (bad `ActionId`, unrecognised `Decision`, malformed " +
                "CSV/JSON) or an action group that fails validation (unknown `ActionId`, `ActionId` not " +
                "part of `batchId`, `EntityType` mismatch, `Field` not decidable for that `EntityType`) " +
                "is reported in the response's `errors` list without aborting the rest of the file, " +
                "matching `POST /import`'s existing \"one bad row never aborts the rest\" model. " +
                "Returns `422` if `batchId`/`file` is missing or `format` isn't `json`/`csv`. " +
                "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        adminGroup.MapPost("/actions/{id}/decide", async (
            string id,
            ConflictDecisionRequest request,
            IImportActionService service,
            IApiLocalizer localizer) =>
        {
            if (!Guid.TryParse(id, out var actionId))
                return Results.Problem(detail: localizer[ApiMessages.ImportActionNotFound], statusCode: StatusCodes.Status404NotFound);

            try
            {
                await service.DecideAsync(actionId, request);
                return Results.NoContent();
            }
            catch (ImportActionNotFoundException)
            {
                return Results.Problem(detail: localizer[ApiMessages.ImportActionNotFound], statusCode: StatusCodes.Status404NotFound);
            }
            catch (ImportActionStateException)
            {
                return Results.Problem(detail: localizer[ApiMessages.ImportActionAlreadyResolved], statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (ImportActionNotDecidableException ex)
            {
                return Results.Problem(
                    detail: localizer.Format(ApiMessages.ImportActionNotDecidable, ex.EntityType),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (UnresolvedFieldConflictException ex)
            {
                return Results.Problem(
                    detail: localizer.Format(ApiMessages.ImportActionAmbiguousFieldsUnresolved, string.Join(", ", ex.FieldNames)),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        .WithName("DecideImportAction")
        .WithSummary("Stage a per-field decision for one staged action")
        .WithDescription(
            "Records a per-field keep/replace/custom decision for one staged action of a currently-" +
            "decidable entity type — git-merge-style: an explicit decision always wins for that " +
            "field, even if it wasn't actually ambiguous. A field left out auto-resolves (empty-side " +
            "wins, equal values keep existing); a field that is genuinely ambiguous (both sides " +
            "non-empty and differ) with no decision returns `422`. An entity type/action combination " +
            "that isn't currently decidable (e.g. an Add action — never ambiguous — or an entity type " +
            "not yet supporting Modify decisions) returns `422` if targeted. Nothing is written to " +
            "any domain table yet — call `POST /import/actions/apply` once every action in the batch " +
            "has been decided. Calling this again for the same action overwrites the prior decision. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        adminGroup.MapPost("/actions/{id}/undo", async (
            string id,
            IImportActionService service,
            IApiLocalizer localizer) =>
        {
            if (!Guid.TryParse(id, out var actionId))
                return Results.Problem(detail: localizer[ApiMessages.ImportActionNotFound], statusCode: StatusCodes.Status404NotFound);

            try
            {
                await service.UndoDecisionAsync(actionId);
                return Results.NoContent();
            }
            catch (ImportActionNotFoundException)
            {
                return Results.Problem(detail: localizer[ApiMessages.ImportActionNotFound], statusCode: StatusCodes.Status404NotFound);
            }
            catch (ImportActionStateException)
            {
                return Results.Problem(detail: localizer[ApiMessages.ImportActionNotDecided], statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        .WithName("UndoImportActionDecision")
        .WithSummary("Undo a staged action decision")
        .WithDescription(
            "Reverts a staged action's decision back to pending. Only valid while the action has a " +
            "decision recorded but its batch hasn't been applied yet. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        adminGroup.MapPost("/actions/apply", async (
            string? batchId,
            bool? purgeOnSuccess,
            IImportActionService service,
            IApiLocalizer localizer) =>
        {
            // Declared nullable and validated here, not bound as a required `string` — a required
            // minimal-API parameter throws BadHttpRequestException at the binding layer when omitted,
            // which the global safety net (BadRequestExceptionHandler) maps to a message about numeric
            // parameters that has nothing to do with batchId. See the "Numeric query parameter binding
            // pattern" convention this mirrors.
            if (string.IsNullOrWhiteSpace(batchId))
                return Results.Problem(detail: localizer[ApiMessages.ImportActionBatchIdRequired], statusCode: StatusCodes.Status422UnprocessableEntity);

            var stillPending = await service.ApplyBatchAsync(batchId, purgeOnSuccess: purgeOnSuccess ?? false);
            return stillPending is null
                ? Results.Ok()
                : Results.Problem(
                    detail: localizer[ApiMessages.ImportActionBatchNotFullyDecided],
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    extensions: new Dictionary<string, object?> { ["pendingActionIds"] = stillPending.PendingActionIds });
        })
        .WithName("ApplyImportActionBatch")
        .WithSummary("Apply every decided action in a batch")
        .WithDescription(
            "Applies every action sharing `batchId`, atomically, once every one of them has a " +
            "decision recorded — mirrors git: resolving individual actions doesn't commit anything " +
            "until every action in the batch has been decided. If any are still pending, applies " +
            "nothing and returns `422` with the list of action ids still needing a decision. " +
            "Returns `422` if `batchId` is missing. " +
            "`purgeOnSuccess=true` (#249) purges the batch's conflict-resolution history (`Import_Action` " +
            "rows) immediately once it applies fully — has no effect on the `422`/still-pending outcome. " +
            "Purging forfeits `POST /import/actions/reverse` for that batch, since it depends on those " +
            "same rows. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        adminGroup.MapPost("/actions/discard", async (
            string? batchId,
            IImportActionService service,
            IApiLocalizer localizer) =>
        {
            if (string.IsNullOrWhiteSpace(batchId))
                return Results.Problem(detail: localizer[ApiMessages.ImportActionBatchIdRequired], statusCode: StatusCodes.Status422UnprocessableEntity);

            try
            {
                await service.DiscardBatchAsync(batchId);
                return Results.NoContent();
            }
            catch (ImportBatchStateException)
            {
                return Results.Problem(detail: localizer[ApiMessages.ImportActionBatchInvalidState], statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        .WithName("DiscardImportActionBatch")
        .WithSummary("Discard every staged action in a batch")
        .WithDescription(
            "Marks every action sharing `batchId` as discarded in one statement — never touches any " +
            "domain table, since a discarded batch's Add actions never created anything to begin " +
            "with (creation is deferred to apply time). Returns `422` if `batchId` is missing, or if " +
            "the batch has already been applied, already been discarded, or has no staged actions " +
            "at all. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        adminGroup.MapPost("/actions/reverse", async (
            string? batchId,
            bool? preview,
            IImportActionService service,
            IApiLocalizer localizer) =>
        {
            if (string.IsNullOrWhiteSpace(batchId))
                return Results.Problem(detail: localizer[ApiMessages.ImportActionBatchIdRequired], statusCode: StatusCodes.Status422UnprocessableEntity);

            try
            {
                await service.ReverseBatchAsync(batchId, preview ?? false);
                return Results.Ok();
            }
            catch (ImportBatchNotFoundException)
            {
                return Results.Problem(detail: localizer[ApiMessages.ImportBatchNotFound], statusCode: StatusCodes.Status404NotFound);
            }
            catch (ImportBatchStateException)
            {
                return Results.Problem(detail: localizer[ApiMessages.ImportActionBatchNotReversible], statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        .WithName("ReverseImportActionBatch")
        .WithSummary("Undo an applied import batch")
        .WithDescription(
            "Reverses (undoes) every Applied action sharing `batchId` — Add actions are soft-deleted, " +
            "Modify actions are restored to their pre-change snapshot. Batches undo as a strict global " +
            "LIFO stack: only the most recently applied batch still live may be reversed, regardless " +
            "of whether an older batch shares any entities with it — reverse the newest batch first. " +
            "On success the batch's own record is itself soft-deleted, and it is the sole signal that " +
            "the batch is no longer live; its staged actions remain visible via `GET /import/actions`, " +
            "permanently marked Applied, as the historical record of what was done. " +
            "`?preview=true` runs every check without writing anything, so a caller can tell whether " +
            "the real call would succeed. Returns `422` if `batchId` is missing, or if the batch isn't " +
            "currently applied, isn't the top of the stack, has no actions, or a Modify's original " +
            "Source/Character/Person linkage can no longer be resolved. Returns `404` if `batchId` " +
            "doesn't exist or was already reversed. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");
    }

    // Case-insensitive: GET /import/actions/export's own JSON response uses ASP.NET's app-wide
    // camelCase default (via ConfigureHttpJsonOptions), but element.Deserialize<T>() with no explicit
    // options falls back to System.Text.Json's library default (case-sensitive, PascalCase-only) —
    // found live via T2, where re-submitting an unmodified export verbatim failed every row with
    // "missing required properties" despite the data being present under its camelCase name.
    private static readonly JsonSerializerOptions BulkDecideRowJsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Row-by-row, not JsonSerializer.Deserialize<List<ImportActionFieldRowDto>> in one call — a single
    // malformed element would otherwise abort the whole file's parse, violating the "one bad row never
    // aborts the rest" contract (#163 spec requirement 6) for the parse stage specifically.
    private static (List<ImportActionFieldRowDto> Rows, List<BulkDecideRowError> Errors) ParseJsonRows(string content)
    {
        var rows   = new List<ImportActionFieldRowDto>();
        var errors = new List<BulkDecideRowError>();

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(content).RootElement;
        }
        catch (JsonException ex)
        {
            errors.Add(new BulkDecideRowError { Message = $"Malformed JSON: {ex.Message}" });
            return (rows, errors);
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new BulkDecideRowError { Message = "Expected a JSON array of rows." });
            return (rows, errors);
        }

        var index = 0;
        foreach (var element in root.EnumerateArray())
        {
            index++;
            try
            {
                rows.Add(element.Deserialize<ImportActionFieldRowDto>(BulkDecideRowJsonOptions) ?? throw new JsonException("Row is null."));
            }
            catch (JsonException ex)
            {
                errors.Add(new BulkDecideRowError { Message = $"Row {index}: {ex.Message}" });
            }
        }

        return (rows, errors);
    }

    // Same per-row resilience as ParseJsonRows — one malformed CSV line is reported and skipped, not
    // an aborted parse of the whole file.
    private static (List<ImportActionFieldRowDto> Rows, List<BulkDecideRowError> Errors) ParseCsvRows(string content)
    {
        var rows   = new List<ImportActionFieldRowDto>();
        var errors = new List<BulkDecideRowError>();
        var lines  = CsvLineParser.Parse(content);

        for (var i = 1; i < lines.Count; i++) // row 0 is the header
        {
            try
            {
                rows.Add(ImportActionFieldRowMapper.FromCsvRow(lines[i]));
            }
            catch (FormatException ex)
            {
                errors.Add(new BulkDecideRowError { Message = $"Row {i}: {ex.Message}" });
            }
        }

        return (rows, errors);
    }

    private static async Task<IResult> HandleImportAsync(
        IFormFile? file, string? settingsJson, IQuoteImportService importService, IApiLocalizer localizer,
        ILogger<Log> logger, bool preview, bool purgeOnSuccess, CancellationToken cancellationToken)
    {
        logger.LogImportPreviewRequest(preview, file?.FileName);

        if (file is null || file.Length == 0)
            return Results.Problem(detail: localizer[ApiMessages.ImportFileMissing], statusCode: StatusCodes.Status422UnprocessableEntity);

        if (!ImportRequestSettingsParser.TryParse(settingsJson, out var settings))
            return Results.Problem(detail: localizer[ApiMessages.ImportSettingsInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);

        if (settings?.Enrich == true)
            return Results.Problem(detail: localizer[ApiMessages.ImportEnrichNotImplemented], statusCode: StatusCodes.Status501NotImplemented);

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await importService.ImportAsync(stream, file.FileName, settings, preview, purgeOnSuccess, cancellationToken);
            return ToStatusCodeResult(result);
        }
        catch (UnknownConverterException ex)
        {
            return Results.Problem(
                detail: localizer.Format(ApiMessages.ImportUnknownConverter, ex.ConverterName),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (QuoteImportValidationException ex)
        {
            return Results.Problem(
                detail: localizer.Format(ApiMessages.ImportFileInvalid, ex.Message),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    // batchId mode dispatches without ever calling this — a request with neither batchId nor a form
    // body reaches here and gets one clear validation message instead of the framework's own bare 400
    // (Minimal API's automatic IFormFile?/[FromForm] binding fails at the routing layer, not via a
    // thrown exception, for a request with no form content-type at all — see the route registration's
    // comment on `HttpRequest request` for why binding is done manually instead).
    private static async Task<IResult> HandleImportFromRequestAsync(
        HttpRequest request, bool purgeOnSuccess, IQuoteImportService importService, IApiLocalizer localizer,
        ILogger<Log> logger, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            return Results.Problem(detail: localizer[ApiMessages.ImportFileOrBatchIdRequired], statusCode: StatusCodes.Status422UnprocessableEntity);

        var form = await request.ReadFormAsync(cancellationToken);
        return await HandleImportAsync(
            form.Files["file"], form["settings"].FirstOrDefault(),
            importService, localizer, logger, preview: false, purgeOnSuccess, cancellationToken);
    }

    private static async Task<IResult> HandleApplyBatchAsync(
        string batchIdRaw, bool purgeOnSuccess, IQuoteImportService importService, IApiLocalizer localizer, ILogger<Log> logger, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(batchIdRaw, out var batchId))
            return Results.Problem(detail: localizer[ApiMessages.ImportBatchNotFound], statusCode: StatusCodes.Status404NotFound);

        logger.LogImportApplyingStagedBatch(batchId);

        try
        {
            var result = await importService.ApplyStagedBatchAsync(batchId, purgeOnSuccess, cancellationToken);
            return ToStatusCodeResult(result);
        }
        catch (ImportBatchNotFoundException)
        {
            return Results.Problem(detail: localizer[ApiMessages.ImportBatchNotFound], statusCode: StatusCodes.Status404NotFound);
        }
    }

    // 202 tells the caller up front that the batch has unresolved actions (any entity type — Quote,
    // Source, etc. — Pending, Blocked, or Stale) it must adjust the file or decide via /import/actions
    // before the batch can be applied — 200 means everything staged cleanly (and, for a non-preview
    // call, was actually applied). PendingActionIds (#165) is the authoritative signal — Conflicts
    // alone only ever covered Quote Modify actions and has no concept of Blocked/Stale or non-Quote
    // entities, so checking it in isolation would silently report 200 for a batch that Source's
    // Blocked or Stale status held from applying.
    private static IResult ToStatusCodeResult(ImportResultResponse result)
    {
        return result.PendingActionIds.Count > 0
            ? Results.Json(result, statusCode: StatusCodes.Status202Accepted)
            : Results.Ok(result);
    }
}
