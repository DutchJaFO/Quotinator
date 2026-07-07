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

/// <summary>Registers all <c>/api/v1/import</c> endpoints — file import (#45, #65) and the manual conflict-review workflow (#149).</summary>
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
             .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
             .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
             .Produces<ProblemDetails>(StatusCodes.Status501NotImplemented)
             .WithName("PreviewImportQuotes")
             .WithSummary("Preview a quote import")
             .WithDescription(
                 "Runs the full import pipeline and returns exactly what it would do, then rolls back — nothing is persisted, no " +
                 "`ImportBatch` is created. Iterate against this endpoint until `conflicts`/`errors` look right, then call " +
                 "`POST /api/v1/import` with the same payload to commit. " + ImportDescription);

        adminGroup.MapPost("/", (
                [Description("The source file to import — Quotinator's canonical JSON schema, or a raw upstream format when `settings.converter` names a compiled converter.")] IFormFile file,
                [Description("Optional JSON text field: `converter`, `duplicateResolution` (policy object), `enrich` (boolean).")] [FromForm] string? settings,
                IQuoteImportService importService,
                IApiLocalizer localizer,
                ILogger<Log> logger,
                CancellationToken cancellationToken) =>
                    HandleImportAsync(file, settings, importService, localizer, logger, preview: false, cancellationToken))
             .DisableAntiforgery()
             .Produces<ImportResultResponse>(StatusCodes.Status200OK)
             .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
             .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
             .Produces<ProblemDetails>(StatusCodes.Status501NotImplemented)
             .WithName("ImportQuotes")
             .WithSummary("Import quotes")
             .WithDescription(ImportDescription);

        publicGroup.MapGet("/conflicts", async (
            string? status,
            string? batchId,
            IConflictResolutionService service,
            int page     = 1,
            int pageSize = 50) =>
        {
            if (page < 1)       page     = 1;
            if (pageSize < 1)   pageSize = 1;
            if (pageSize > 200) pageSize = 200;

            var result = await service.GetPagedAsync(batchId, status, page, pageSize);
            return Results.Ok(result);
        })
        .WithName("GetImportConflicts")
        .WithSummary("List import conflicts")
        .WithDescription(
            "Returns a paginated list of import conflicts, newest first. " +
            "Filter by `status` (`pending`, `decided`, `resolved`) and/or `batchId`. " +
            "Each conflict includes both sides' field values, human-readable batch labels for " +
            "`existing`/`incoming`, `sameFile` (true when both sides came from the same imported file), " +
            "and `ambiguousFields` — the fields that genuinely need an explicit decision (empty once no " +
            "longer pending). Maximum `pageSize` is 200.");

        adminGroup.MapPost("/conflicts/{id}/decide", async (
            string id,
            ConflictDecisionRequest request,
            IConflictResolutionService service,
            IApiLocalizer localizer) =>
        {
            if (!Guid.TryParse(id, out var conflictId))
                return Results.Problem(detail: localizer[ApiMessages.ConflictNotFound], statusCode: StatusCodes.Status404NotFound);

            try
            {
                await service.DecideAsync(conflictId, request);
                return Results.NoContent();
            }
            catch (ConflictNotFoundException)
            {
                return Results.Problem(detail: localizer[ApiMessages.ConflictNotFound], statusCode: StatusCodes.Status404NotFound);
            }
            catch (ConflictStateException)
            {
                return Results.Problem(detail: localizer[ApiMessages.ConflictAlreadyResolved], statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (UnresolvedFieldConflictException ex)
            {
                return Results.Problem(
                    detail: string.Format(localizer[ApiMessages.ConflictAmbiguousFieldsUnresolved], string.Join(", ", ex.FieldNames)),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        .WithName("DecideImportConflict")
        .WithSummary("Stage a per-field decision for one conflict")
        .WithDescription(
            "Records a per-field keep/replace/custom decision for one conflict — git-merge-style: an " +
            "explicit decision always wins for that field, even if it wasn't actually ambiguous. A field " +
            "left out auto-resolves (empty-side wins, equal values keep existing); a field that is " +
            "genuinely ambiguous (both sides non-empty and differ) with no decision returns `422`. " +
            "Nothing is written to any quote data yet — call `POST /conflicts/apply` once every conflict " +
            "in the batch has a decision. Calling this again for the same conflict overwrites the prior " +
            "decision. Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        adminGroup.MapPost("/conflicts/{id}/undo", async (
            string id,
            IConflictResolutionService service,
            IApiLocalizer localizer) =>
        {
            if (!Guid.TryParse(id, out var conflictId))
                return Results.Problem(detail: localizer[ApiMessages.ConflictNotFound], statusCode: StatusCodes.Status404NotFound);

            try
            {
                await service.UndoDecisionAsync(conflictId);
                return Results.NoContent();
            }
            catch (ConflictNotFoundException)
            {
                return Results.Problem(detail: localizer[ApiMessages.ConflictNotFound], statusCode: StatusCodes.Status404NotFound);
            }
            catch (ConflictStateException)
            {
                return Results.Problem(detail: localizer[ApiMessages.ConflictNotDecided], statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        .WithName("UndoImportConflictDecision")
        .WithSummary("Undo a staged decision")
        .WithDescription(
            "Reverts a conflict's staged decision back to pending. Only valid while the conflict has a " +
            "decision recorded but its batch hasn't been applied yet. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        adminGroup.MapPost("/conflicts/apply", async (
            string batchId,
            IConflictResolutionService service,
            IApiLocalizer localizer) =>
        {
            var stillPending = await service.ApplyBatchAsync(batchId);
            return stillPending is null
                ? Results.Ok()
                : Results.Problem(
                    detail: localizer[ApiMessages.ConflictBatchNotFullyDecided],
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    extensions: new Dictionary<string, object?> { ["pendingConflictIds"] = stillPending.PendingConflictIds });
        })
        .WithName("ApplyImportConflictBatch")
        .WithSummary("Apply every decided conflict in a batch")
        .WithDescription(
            "Applies every conflict sharing `batchId`, atomically, once every one of them has a " +
            "decision recorded — mirrors git: resolving individual conflicts doesn't commit anything " +
            "until every conflict in the batch has been decided. If any are still pending, applies " +
            "nothing and returns `422` with the list of conflict ids still needing a decision. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");
    }

    private static async Task<IResult> HandleImportAsync(
        IFormFile file, string? settingsJson, IQuoteImportService importService, IApiLocalizer localizer,
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
            return Results.Ok(result);
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
}
