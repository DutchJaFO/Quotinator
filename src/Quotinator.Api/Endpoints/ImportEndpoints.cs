using Quotinator.Api.Endpoints.Filters;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Services;
using Quotinator.Data.Import;
using Quotinator.Engine.Models;
using Quotinator.Engine.Services;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/import</c> endpoints — the manual conflict-review workflow (#149).</summary>
internal static class ImportEndpoints
{
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
                            .AddEndpointFilter<AdminApiKeyFilter>();

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
}
