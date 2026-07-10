using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Filters;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Import;
using Quotinator.Engine.Models;
using Quotinator.Engine.Services;

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
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.";

        adminGroup.MapPost("/preview", (
                [Description("The source file to import — Quotinator's canonical JSON schema, or a raw upstream format when `settings.converter` names a compiled converter.")] IFormFile file,
                [Description("Optional JSON text field: `converter`, `duplicateResolution` (policy object), `enrich` (boolean).")] [FromForm] string? settings,
                IQuoteImportService importService,
                IApiLocalizer localizer,
                ILogger<Log> logger,
                CancellationToken cancellationToken) =>
                    HandleImportAsync(file, settings, importService, localizer, logger, preview: true, cancellationToken))
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
                        ? HandleApplyBatchAsync(batchId, importService, localizer, logger, cancellationToken)
                        : HandleImportFromRequestAsync(request, importService, localizer, logger, cancellationToken))
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
                 ImportDescription);

        // ── #154: unified staging engine — /import/actions/* ────────────────────

        publicGroup.MapGet("/actions", async (
            string? status,
            string? batchId,
            string? entityType,
            IImportActionService service,
            int page     = 1,
            int pageSize = 50) =>
        {
            if (page < 1)       page     = 1;
            if (pageSize < 1)   pageSize = 1;
            if (pageSize > 200) pageSize = 200;

            var result = await service.GetPagedAsync(batchId, status, entityType, page, pageSize);
            return Results.Ok(result);
        })
        .WithName("GetImportActions")
        .WithSummary("List staged import actions")
        .WithDescription(
            "Returns a paginated list of staged import actions (#154), newest first — the review " +
            "surface for a staged batch, whether staged via `POST /import`, `POST /import/preview`, " +
            "or startup seeding. Filter by `status` (`Pending`, `Decided`, `Applied`, `Discarded`), " +
            "`batchId`, and/or `entityType` (`Quote`, `Source`, `Character`, `Person`). Each item " +
            "includes `relatedActionIds` (the Source/Character/Person actions in the same batch a " +
            "Quote action depends on) and `ambiguousFields` (the fields genuinely needing a decision, " +
            "populated only while `status` is `Pending`). Maximum `pageSize` is 200.");

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
                    detail: string.Format(localizer[ApiMessages.ImportActionNotDecidable], ex.EntityType),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (UnresolvedFieldConflictException ex)
            {
                return Results.Problem(
                    detail: string.Format(localizer[ApiMessages.ImportActionAmbiguousFieldsUnresolved], string.Join(", ", ex.FieldNames)),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        .WithName("DecideImportAction")
        .WithSummary("Stage a per-field decision for one staged action")
        .WithDescription(
            "Records a per-field keep/replace/custom decision for one staged Quote action — git-merge-" +
            "style: an explicit decision always wins for that field, even if it wasn't actually " +
            "ambiguous. A field left out auto-resolves (empty-side wins, equal values keep existing); " +
            "a field that is genuinely ambiguous (both sides non-empty and differ) with no decision " +
            "returns `422`. Only `Quote` actions can be decided — Source/Character/Person actions are " +
            "always already-Decided (an Add is never ambiguous), so targeting one returns `422`. " +
            "Nothing is written to any domain table yet — call `POST /import/actions/apply` once " +
            "every action in the batch has been decided. Calling this again for the same action " +
            "overwrites the prior decision. Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

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
            string batchId,
            IImportActionService service,
            IApiLocalizer localizer) =>
        {
            var stillPending = await service.ApplyBatchAsync(batchId);
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
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        adminGroup.MapPost("/actions/discard", async (
            string batchId,
            IImportActionService service,
            IApiLocalizer localizer) =>
        {
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
            "with (creation is deferred to apply time). Returns `422` if the batch has already been " +
            "applied, already been discarded, or has no staged actions at all. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        adminGroup.MapPost("/actions/reverse", async (
            string batchId,
            bool? preview,
            IImportActionService service,
            IApiLocalizer localizer) =>
        {
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
            "the real call would succeed. Returns `404` if `batchId` doesn't exist or was already " +
            "reversed; `422` if the batch isn't currently applied, isn't the top of the stack, has no " +
            "actions, or a Modify's original Source/Character/Person linkage can no longer be resolved. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");
    }

    private static async Task<IResult> HandleImportAsync(
        IFormFile? file, string? settingsJson, IQuoteImportService importService, IApiLocalizer localizer,
        ILogger<Log> logger, bool preview, CancellationToken cancellationToken)
    {
        logger.LogInformation("[Api - Import] preview={Preview} file={File}", preview, file?.FileName);

        if (file is null || file.Length == 0)
            return Results.Problem(detail: localizer[ApiMessages.ImportFileMissing], statusCode: StatusCodes.Status422UnprocessableEntity);

        if (!ImportRequestSettingsParser.TryParse(settingsJson, out var settings))
            return Results.Problem(detail: localizer[ApiMessages.ImportSettingsInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);

        if (settings?.Enrich == true)
            return Results.Problem(detail: localizer[ApiMessages.ImportEnrichNotImplemented], statusCode: StatusCodes.Status501NotImplemented);

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await importService.ImportAsync(stream, file.FileName, settings, preview, cancellationToken);
            return ToStatusCodeResult(result);
        }
        catch (UnknownConverterException ex)
        {
            return Results.Problem(
                detail: string.Format(localizer[ApiMessages.ImportUnknownConverter], ex.ConverterName),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (QuoteImportValidationException ex)
        {
            return Results.Problem(
                detail: string.Format(localizer[ApiMessages.ImportFileInvalid], ex.Message),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    // batchId mode dispatches without ever calling this — a request with neither batchId nor a form
    // body reaches here and gets one clear validation message instead of the framework's own bare 400
    // (Minimal API's automatic IFormFile?/[FromForm] binding fails at the routing layer, not via a
    // thrown exception, for a request with no form content-type at all — see the route registration's
    // comment on `HttpRequest request` for why binding is done manually instead).
    private static async Task<IResult> HandleImportFromRequestAsync(
        HttpRequest request, IQuoteImportService importService, IApiLocalizer localizer,
        ILogger<Log> logger, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            return Results.Problem(detail: localizer[ApiMessages.ImportFileOrBatchIdRequired], statusCode: StatusCodes.Status422UnprocessableEntity);

        var form = await request.ReadFormAsync(cancellationToken);
        return await HandleImportAsync(
            form.Files["file"], form["settings"].FirstOrDefault(),
            importService, localizer, logger, preview: false, cancellationToken);
    }

    private static async Task<IResult> HandleApplyBatchAsync(
        string batchIdRaw, IQuoteImportService importService, IApiLocalizer localizer, ILogger<Log> logger, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(batchIdRaw, out var batchId))
            return Results.Problem(detail: localizer[ApiMessages.ImportBatchNotFound], statusCode: StatusCodes.Status404NotFound);

        logger.LogInformation("[Api - Import] applying already-staged batch {BatchId}", batchId);

        try
        {
            var result = await importService.ApplyStagedBatchAsync(batchId, cancellationToken);
            return ToStatusCodeResult(result);
        }
        catch (ImportBatchNotFoundException)
        {
            return Results.Problem(detail: localizer[ApiMessages.ImportBatchNotFound], statusCode: StatusCodes.Status404NotFound);
        }
    }

    // 202 tells the caller up front that the batch has unresolved conflicts it must adjust the file
    // or decide via /import/actions before the batch can be applied — 200 means everything staged
    // cleanly (and, for a non-preview call, was actually applied).
    private static IResult ToStatusCodeResult(ImportResultResponse result)
    {
        var hasPending = result.Conflicts.Any(c => c.Status == "pending");
        return hasPending
            ? Results.Json(result, statusCode: StatusCodes.Status202Accepted)
            : Results.Ok(result);
    }
}
