using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>Endpoint tests for <c>/api/v1/notifications</c> (#278).</summary>
[TestClass]
public class NotificationEndpointsTests
{
    private const string TestKey = "test-admin-key";

    private static WebApplicationFactory<Program> CreateFactory(
        string? adminApiKey = null, FakeNotificationReader? notificationReader = null, FakeNotificationWriter? notificationWriter = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(NoOpDatabaseInitializer.Instance);
                services.AddSingleton<IAuditEntryWriter>(new NoOpAuditEntryWriter());
                services.AddSingleton<IAuditEntryReader>(new NoOpAuditEntryReader());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
                services.AddSingleton<INotificationReader>(notificationReader ?? new FakeNotificationReader());
                services.AddSingleton<INotificationWriter>(notificationWriter ?? new FakeNotificationWriter());
            });

            // ConfigureAppConfiguration runs after all file-based sources (including
            // appsettings.local.json), so the in-memory value wins for the test.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Quotinator:AdminApiKey"] = adminApiKey
                });
            });
        });

    private static HttpClient CreateClientWithKey(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", TestKey);
        return client;
    }

    private static NotificationEntity BuildNotification(NotificationType type = NotificationType.Information, string message = "test message") => new()
    {
        Type    = new SafeValue<NotificationType?>(type.ToString(), type),
        Message = message,
    };

    // ── GET /notifications — list ────────────────────────────────────────────

    [TestMethod]
    public async Task GetNotifications_Returns200WithPageShape()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/notifications", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsTrue(doc.RootElement.TryGetProperty("items",      out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("page",       out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("pageSize",   out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("totalCount", out _));
    }

    [TestMethod]
    public async Task GetNotifications_IncludesDismissedNotifications()
    {
        var reader = new FakeNotificationReader();
        var dismissed = BuildNotification(message: "already dismissed");
        dismissed.IsDismissed = true;
        reader.Seed(dismissed);
        reader.Seed(BuildNotification(message: "still active"));

        using var factory = CreateFactory(notificationReader: reader);
        var response = await factory.CreateClient().GetAsync("/api/v1/notifications", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(2, doc.RootElement.GetProperty("items").GetArrayLength(), "the list endpoint returns full history, not just active notifications");
    }

    // ── Pagination contract (#183's 8-case matrix) ───────────────────────────

    [TestMethod]
    public async Task GetNotifications_PageZero_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/notifications?page=0", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetNotifications_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/notifications?page=abc", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetNotifications_PageSizeMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/notifications?pageSize=abc", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetNotifications_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/notifications?pageSize=-1", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetNotifications_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/notifications?pageSize=999", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetNotifications_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        var reader = new FakeNotificationReader();
        for (var i = 0; i < 3; i++) reader.Seed(BuildNotification(message: $"notification {i}"));

        using var factory = CreateFactory(notificationReader: reader);
        var response = await factory.CreateClient().GetAsync("/api/v1/notifications?pageSize=0", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task GetNotifications_PageSizeOmitted_DefaultsTo20()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/notifications", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetNotifications_PageBeyondLast_Returns422DistinctDetail()
    {
        var reader = new FakeNotificationReader();
        reader.Seed(BuildNotification());

        using var factory = CreateFactory(notificationReader: reader);
        var response = await factory.CreateClient().GetAsync("/api/v1/notifications?page=5", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── POST /notifications/{id}/dismiss ─────────────────────────────────────

    [TestMethod]
    public async Task DismissNotification_ExistingId_MarksDismissed()
    {
        var writer = new FakeNotificationWriter();
        var notification = BuildNotification();
        writer.Seed(notification);

        using var factory = CreateFactory(TestKey, notificationWriter: writer);
        var response = await CreateClientWithKey(factory)
            .PostAsync($"/api/v1/notifications/{notification.Id}/dismiss", null, TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(doc.RootElement.GetProperty("isDismissed").GetBoolean());
    }

    [TestMethod]
    public async Task DismissNotification_UnknownId_Returns404()
    {
        using var factory = CreateFactory(TestKey);
        var response = await CreateClientWithKey(factory)
            .PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/dismiss", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task DismissNotification_MalformedId_Returns404NotBadRequest()
    {
        using var factory = CreateFactory(TestKey);
        var response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/notifications/not-a-guid/dismiss", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task DismissNotification_NoApiKey_Returns401()
    {
        var writer = new FakeNotificationWriter();
        var notification = BuildNotification();
        writer.Seed(notification);

        using var factory = CreateFactory(TestKey, notificationWriter: writer);
        var response = await factory.CreateClient()
            .PostAsync($"/api/v1/notifications/{notification.Id}/dismiss", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── OpenAPI: tag, proven live ─────────────────────────────────────────────

    [TestMethod]
    public async Task NotificationEndpoints_OnLiveSpec_TaggedNotifications()
    {
        using var factory = CreateFactory();
        var doc = await factory.CreateClient().GetFromJsonAsync<JsonDocument>("/openapi/v1.json", TestContext.CancellationToken);

        var paths = doc!.RootElement.GetProperty("paths");

        var listTags    = paths.GetProperty("/api/v1/notifications").GetProperty("get").GetProperty("tags");
        var dismissTags = paths.GetProperty("/api/v1/notifications/{id}/dismiss").GetProperty("post").GetProperty("tags");

        Assert.Contains(t => t.GetString() == "Notifications", listTags.EnumerateArray());
        Assert.Contains(t => t.GetString() == "Notifications", dismissTags.EnumerateArray());
    }

    public TestContext TestContext { get; set; }
}
