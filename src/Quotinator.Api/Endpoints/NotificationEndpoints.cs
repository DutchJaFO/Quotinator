using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using Quotinator.Api.Endpoints.Filters;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Endpoints;

/// <summary>
/// Registers <c>/api/v1/notifications</c> (#278). Mirrors
/// <see cref="ImportFileResourceEndpoints"/>'s own precedent exactly: a read-only <c>publicGroup</c>
/// (no API key) for listing, and a destructive <c>adminGroup</c> (<c>X-Api-Key</c> required) for
/// dismissing. Its own <see cref="ApiTags.Notifications"/> category — status infrastructure, not
/// database administration, matching why <see cref="ImportFileResourceEndpoints"/> isn't tagged
/// <see cref="ApiTags.Admin"/> either.
/// </summary>
internal static class NotificationEndpoints
{
    internal static void MapNotificationEndpoints(this WebApplication app)
    {
        var publicGroup = app.MapGroup("/api/v1/notifications")
                             .WithTags(ApiTags.Notifications)
                             .RequireRateLimiting(RateLimitPolicies.Admin);

        var adminGroup = app.MapGroup("/api/v1/notifications")
                            .WithTags(ApiTags.Notifications)
                            .RequireRateLimiting(RateLimitPolicies.Admin)
                            .AddEndpointFilter<AdminApiKeyFilter>()
                            .WithMetadata(AdminApiKeyRequiredMarker.Instance);

        publicGroup.MapGet("/", async (
            INotificationReader notifications,
            IApiLocalizer localizer,
            [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
            [Description("Number of entries per page (0-500). 0 means every notification as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null) =>
        {
            if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
                return pageError!;

            var result = await notifications.GetPagedAsync(pageValue, pageSizeValue);

            var beyondLastError = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
            if (beyondLastError is not null) return beyondLastError;

            var mapped = new PagedItems<NotificationResponse>(
                [.. result.Items.Select(ToResponse)], result.Page, result.PageSize, result.TotalCount);
            return Results.Ok(mapped);
        })
        .WithName("GetNotifications")
        .WithSummary("List notifications")
        .Produces<PagedItems<NotificationResponse>>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Returns a paginated list of the full notification history (#278) — including dismissed " +
            "and expired notifications, newest first. See `GET /api/v1/health`/the startup modals for " +
            "the active-only subset. Maximum `pageSize` is 500.");

        adminGroup.MapPost("/{id}/dismiss", async (
            string id,
            INotificationWriter notificationWriter,
            IApiLocalizer localizer) =>
        {
            if (!Guid.TryParse(id, out var notificationId))
                return Results.Problem(detail: localizer[ApiMessages.NotificationNotFound], statusCode: StatusCodes.Status404NotFound);

            var dismissed = await notificationWriter.DismissAsync(notificationId);
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

    private static NotificationResponse ToResponse(NotificationEntity entity) => new()
    {
        Id                = entity.Id.ToCanonicalId(),
        Type              = entity.Type.Parsed?.ToString().ToLowerInvariant() ?? entity.Type.Raw,
        Title             = entity.Title,
        Body              = entity.Body,
        Metadata          = entity.Metadata,
        MetadataKind      = entity.MetadataKind.Parsed?.ToString().ToLowerInvariant()
                            ?? (entity.MetadataKind.Raw.Length > 0 ? entity.MetadataKind.Raw : null),
        CreatedAt         = entity.DateCreated.Parsed,
        ExpiresAt         = entity.ExpiresAt.Parsed,
        IsDismissed       = entity.IsDismissed,
        DismissedAt       = entity.DismissedAt.Parsed,
        DismissTriggerKey = entity.DismissTriggerKey.Parsed?.ToString().ToLowerInvariant() ?? (entity.DismissTriggerKey.Raw.Length > 0 ? entity.DismissTriggerKey.Raw : null),
    };
}
