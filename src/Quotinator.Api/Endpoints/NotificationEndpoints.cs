using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Quotinator.Api.Endpoints.Filters;
using Quotinator.Api.Enums;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Api.Services;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Constants.Routes;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Endpoints;

/// <summary>
/// Registers <c>/api/v1/notifications</c> (#278). Mirrors
/// <see cref="ImportFileResourceEndpoints"/>'s own precedent exactly: a read-only <c>publicGroup</c>
/// (no API key) for listing, and a destructive <c>adminGroup</c> (<c>X-Api-Key</c> required) for
/// dismissing. Its own <see cref="ApiTags.Notifications"/> category: status infrastructure, not
/// database administration, matching why <see cref="ImportFileResourceEndpoints"/> isn't tagged
/// <see cref="ApiTags.Admin"/> either.
/// </summary>
internal static class NotificationEndpoints
{
    // Held as a const, per CLAUDE.md's endpoint naming rule, so the operation id it publishes is named once.
    private const string RefreshNotificationsName = "RefreshNotifications";

    internal static void MapNotificationEndpoints(this WebApplication app)
    {
        RouteGroupBuilder publicGroup = app.MapGroup(ApiRoutes.Notifications)
                             .WithTags(ApiTags.Notifications)
                             .RequireRateLimiting(RateLimitPolicies.Admin);

        RouteGroupBuilder adminGroup = app.MapGroup(ApiRoutes.Notifications)
                            .WithTags(ApiTags.Notifications)
                            .RequireRateLimiting(RateLimitPolicies.Admin)
                            .AddEndpointFilter(app.Services.GetRequiredService<AdminApiKeyFilter>())
                            .WithMetadata(AdminApiKeyRequiredMarker.Instance);

        publicGroup.MapGet("/", async (
            INotificationReader notifications,
            INotificationActionExecutor actionExecutor,
            IApiLocalizer localizer,
            [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
            [Description("Number of entries per page (0-500). 0 means every notification as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null,
            [Description("ISO 639-1 language code for the notification's title and body. Falls back to the notification's original language when it has no translation for the requested one. Defaults to the request's Accept-Language.")] string? lang = null) =>
        {
            if (!PaginationParsing.TryParse(page, pageSize, localizer, out int pageValue, out int pageSizeValue, out IResult? pageError))
                return pageError!;

            if (!InputValidation.TryNormalizeLang(ref lang))
                return Results.Problem(detail: localizer[ApiMessages.LangInvalid], statusCode: StatusCodes.Status400BadRequest);

            PagedItems<NotificationEntity> result = await notifications.GetPagedAsync(pageValue, pageSizeValue, ResolveLanguage(lang));

            IResult? beyondLastError = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
            if (beyondLastError is not null) return beyondLastError;

            // Read once for the whole page, as the notifications page does, so no row queries on its own.
            NotificationActionAvailability availability = await actionExecutor.GetAvailabilityAsync();

            PagedItems<NotificationResponse> mapped = new(
                [.. result.Items.Select(n => ToResponse(n, OfferedOptions(n, actionExecutor, availability), availability.BackupCaution))],
                result.Page, result.PageSize, result.TotalCount);
            return Results.Ok(mapped);
        })
        .WithName("GetNotifications")
        .WithSummary("List notifications")
        .Produces<PagedItems<NotificationResponse>>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Returns a paginated list of the full notification history (#278), including dismissed " +
            "and expired notifications, newest first. See `GET /api/v1/health`/the startup modals for " +
            "the active-only subset. Maximum `pageSize` is 500. Each item carries `appVersionId`, the " +
            "application version that wrote it, or `null` where provenance could not be determined. Each item also carries " +
            "`availableActions`: what its action can do right now, lowercase (for example `backupthenreseed`, " +
            "`removeoldestbackupthenreseed`, `reseedwithoutbackup`, `resetdatabase`, `keepexisting`, `takeincoming`). An option " +
            "is listed only while it can actually run, and a dismissed notification lists none (#348). `backupCaution` is " +
            "`true` when the backups folder is at its quota and one of those options takes a backup, which may then " +
            "reach the ceiling and be refused (#348).");

        // #348: open to every user, not only administrators, and limited by the admin rate-limit policy
        // like the rest of this group (developer, 2026-09-26).
        publicGroup.MapPost("/refresh", async (NotificationConditionChecks conditionChecks) =>
        {
            IReadOnlyList<NotificationConditionCheckResult> results = await conditionChecks.RunAsync();

            return Results.Ok(new NotificationRefreshResponse
            {
                Checks = [.. results.Select(result => new NotificationConditionCheckDto
                {
                    Kind    = result.Kind.ToString().ToLowerInvariant(),
                    Outcome = result.Outcome.ToString().ToLowerInvariant(),
                })],
            });
        })
        .WithName(RefreshNotificationsName)
        .WithSummary("Re-check every notification whose condition can change")
        .Produces<NotificationRefreshResponse>(StatusCodes.Status200OK)
        .WithDescription(
            "Re-checks every notification whose condition can change while the application runs, and raises or " +
            "resolves each to match, without a restart (#348): for example the backup quota warning clearing " +
            "after backups were removed outside the application. Returns what each check did, as `checks`: its " +
            "`kind` and its `outcome` (`raised`, `cleared` or `unchanged`). The same checks also run at startup, " +
            "after a Reset, and after a backup is taken or removed. Needs no admin key.");

        adminGroup.MapPost("/{id}/dismiss", async (
            string id,
            INotificationWriter notificationWriter,
            IApiLocalizer localizer,
            [Description("ISO 639-1 language code for the returned notification's title and body. Defaults to the request's Accept-Language.")] string? lang = null) =>
        {
            if (!Guid.TryParse(id, out Guid notificationId))
                return Results.Problem(detail: localizer[ApiMessages.NotificationNotFound], statusCode: StatusCodes.Status404NotFound);

            if (!InputValidation.TryNormalizeLang(ref lang))
                return Results.Problem(detail: localizer[ApiMessages.LangInvalid], statusCode: StatusCodes.Status400BadRequest);

            NotificationEntity? dismissed = await notificationWriter.DismissAsync(notificationId, ResolveLanguage(lang));
            if (dismissed is null)
                return Results.Problem(detail: localizer[ApiMessages.NotificationNotFound], statusCode: StatusCodes.Status404NotFound);

            return Results.Ok(ToResponse(dismissed));
        })
        .WithName("DismissNotification")
        .WithSummary("Dismiss a notification")
        .Produces<NotificationResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
        .WithDescription(
            "Marks a notification dismissed (#278). Returns `404` for an unknown or malformed id. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");
    }

    private static NotificationResponse ToResponse(
        NotificationEntity entity, IReadOnlyList<NotificationActionOption>? offeredOptions = null, bool backupCaution = false) => new()
    {
        Id                = entity.Id.ToCanonicalId(),
        Type              = entity.Type.Parsed?.ToString().ToLowerInvariant() ?? entity.Type.Raw,
        Title             = entity.Title,
        Body              = entity.Body,
        Metadata          = entity.Metadata,
        MetadataKind      = entity.MetadataKind.Parsed?.ToString().ToLowerInvariant()
                            ?? (entity.MetadataKind.Raw.Length > 0 ? entity.MetadataKind.Raw : null),
        AppVersionId      = entity.AppVersionId?.ToCanonicalId(),
        CreatedAt         = entity.DateCreated.Parsed,
        ExpiresAt         = entity.ExpiresAt.Parsed,
        IsDismissed       = entity.IsDismissed,
        DismissedAt       = entity.DismissedAt.Parsed,
        DismissTriggerKey = entity.DismissTriggerKey.Parsed?.ToString().ToLowerInvariant() ?? (entity.DismissTriggerKey.Raw.Length > 0 ? entity.DismissTriggerKey.Raw : null),
        DismissReason     = entity.DismissReason.Parsed?.ToString().ToLowerInvariant() ?? (entity.DismissReason.Raw.Length > 0 ? entity.DismissReason.Raw : null),
        Resolution        = entity.Resolution.Parsed?.ToString().ToLowerInvariant() ?? (entity.Resolution.Raw.Length > 0 ? entity.Resolution.Raw : null),
        // EffectiveLanguage is null only when a caller bypasses the read projection (a fake in an
        // endpoint test); the original language is the honest answer there, since no translation was
        // resolved.
        Language          = entity.EffectiveLanguage ?? entity.OriginalLanguage,
        OriginalLanguage  = entity.OriginalLanguage,
        AvailableActions  = [.. (offeredOptions ?? []).Select(option => option.ToString().ToLowerInvariant())],
        // #348: at the quota, a row offering an option that takes a backup says so, the way the page does.
        BackupCaution     = backupCaution && (offeredOptions ?? []).Any(NotificationActionOptions.TakesABackup),
        IsTranslated      = !string.Equals(
                                entity.EffectiveLanguage ?? entity.OriginalLanguage,
                                entity.OriginalLanguage,
                                StringComparison.OrdinalIgnoreCase),
    };

    // #348: what the row's action can do right now. The same answer the notifications page renders its controls from, including that a
    // dismissed row offers nothing.
    private static IReadOnlyList<NotificationActionOption> OfferedOptions(
        NotificationEntity entity, INotificationActionExecutor executor, NotificationActionAvailability availability) =>
        !entity.IsDismissed && entity.DismissTriggerKey.Parsed is NotificationDismissTrigger trigger
            ? [.. executor.AvailableOptions(
                    trigger,
                    NotificationMetadataKinds.TryDeserialize(entity.MetadataKind.Parsed, entity.Metadata),
                    availability)]
            : [];

    // ?lang= selects the notification's *content* language, the way it does for quotes; Accept-Language
    // fills in only when it is absent. This is the deliberate extension CLAUDE.md's language rule does
    // not cover: a notification is persisted content that reads as a UI message, so it takes the
    // content treatment on the API and the UI treatment everywhere it renders. The prohibition that
    // still stands unchanged is the specific one: ?lang= never drives error-message language.
    private static string ResolveLanguage(string? lang) =>
        lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
}
