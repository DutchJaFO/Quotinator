using System.Data;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class AdminAuditEndpointTests
{
    private const string TestKey = "test-admin-key";

    private static WebApplicationFactory<Program> CreateFactory(
        IAuditEntryReader?  auditReader  = null,
        IAuditEntryWriter?  auditWriter  = null,
        IChangeReader?      changeReader = null,
        string?        adminApiKey  = TestKey,
        int?           maxExportRows = null)
    {
        var reader   = auditReader  ?? new NoOpAuditEntryReader();
        var writer   = auditWriter  ?? new NoOpAuditEntryWriter();
        var changes  = changeReader ?? NoOpChangeReader.Instance;

        return new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<IAuditEntryWriter>(writer);
                services.AddSingleton<IAuditEntryReader>(reader);
                services.AddSingleton<IChangeReader>(changes);
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
            });
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Quotinator:AdminApiKey"] = adminApiKey,
                    ["Quotinator:AdminAuditExportMaxRows"] = maxExportRows?.ToString(),
                });
            });
        });
    }

    // ── Response shape ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetAudit_CorrectKey_Returns200WithPageShape()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);
        var doc  = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("totalCount", out _), "response must have totalCount");
        Assert.IsTrue(root.TryGetProperty("totalPages",    out _), "response must have totalPages");
        Assert.IsTrue(root.TryGetProperty("page",          out _), "response must have page");
        Assert.IsTrue(root.TryGetProperty("pageSize",      out _), "response must have pageSize");
        Assert.IsTrue(root.TryGetProperty("items",         out _), "response must have items");
    }

    [TestMethod]
    public async Task GetAudit_EmptyResult_ReturnsZeroTotals()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit", TestContext.CancellationToken);
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(0, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task GetAudit_WithItems_ReturnsItems()
    {
        var entry = new AuditEntryEntity
        {
            TableName   = "Quotes",
            RecordId    = Guid.Empty.ToString("D").ToUpperInvariant(),
            Operation   = AuditOperation.Insert,
            Agent       = "TestRunner/1.0",
            PerformedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };

        var stubReader = new StubAuditReader(new PagedItems<AuditEntryEntity>([entry], 1, 50, 1));
        using var factory = CreateFactory(stubReader);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit", TestContext.CancellationToken);
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(1, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task GetAuditLog_ResponseShape_NoSafeValueWrapperInJson()
    {
        var entry = new AuditEntryEntity
        {
            TableName   = "Quotes",
            RecordId    = Guid.Empty.ToString("D").ToUpperInvariant(),
            Operation   = AuditOperation.Insert,
            Agent       = "TestRunner/1.0",
            PerformedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        var stubReader = new StubAuditReader(new PagedItems<AuditEntryEntity>([entry], 1, 50, 1));
        using var factory = CreateFactory(stubReader);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit", TestContext.CancellationToken);
        var body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.DoesNotContain("\"raw\":", body, "SafeValue<T>'s internal wrapper must not leak into the response JSON");
        Assert.DoesNotContain("\"isValid\":", body, "SafeValue<T>'s internal wrapper must not leak into the response JSON");
    }

    [TestMethod]
    public async Task GetAuditLog_ResponseShape_PreservesDateModifiedWhenSet()
    {
        var modifiedAt = new DateTime(2026, 2, 2, 8, 0, 0, DateTimeKind.Utc);
        var entry = new AuditEntryEntity
        {
            TableName    = "Quotes",
            RecordId     = Guid.Empty.ToString("D").ToUpperInvariant(),
            Operation    = AuditOperation.Update,
            PerformedAt  = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            DateModified = SafeDateValue.From(modifiedAt),
        };
        var stubReader = new StubAuditReader(new PagedItems<AuditEntryEntity>([entry], 1, 50, 1));
        using var factory = CreateFactory(stubReader);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit", TestContext.CancellationToken);
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        var item     = doc.RootElement.GetProperty("items")[0];

        Assert.AreEqual(modifiedAt, item.GetProperty("dateModified").GetDateTime(),
            "a genuinely modified row's dateModified must survive the SafeValue unwrap, not be dropped");
    }

    // ── Pagination contract (#195) ────────────────────────────────────────────

    [TestMethod]
    public async Task Audit_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit?pageSize=999", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    /// <summary>
    /// A global <c>BadHttpRequestException</c> safety net already maps malformed binding failures to
    /// 422 (see <c>BadRequestExceptionHandler</c>), so this was never a bare 400 — the genuine gap is
    /// that it falls through to the generic <c>ErrorNumericParameterInvalid</c> message instead of the
    /// specific pageSize detail #195's shared parser produces.
    /// </summary>
    [TestMethod]
    public async Task Audit_PageSizeMalformed_Returns422WithSpecificDetailNotGenericFallback()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit?pageSize=abc", TestContext.CancellationToken);
        var body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("Numeric parameters (yearFrom", body, "must not fall through to the generic BadHttpRequestException safety-net message");
        Assert.Contains("pageSize", body);
    }

    [TestMethod]
    public async Task Audit_PageZero_Returns422NotSilentlyPageOne()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit?page=0", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "page=0 must be rejected, not silently reinterpreted as page 1");
    }

    [TestMethod]
    public async Task Audit_PageSizeOmitted_DefaultsTo20NotFifty()
    {
        int? capturedPageSize = null;
        var  capturingReader  = new CapturingAuditReader(ps => capturedPageSize = ps);

        using var factory = CreateFactory(capturingReader);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        await client.GetAsync("/api/v1/admin/audit", TestContext.CancellationToken);

        Assert.AreEqual(20, capturedPageSize, "the standard shared default is 20, not audit's old default of 50");
    }

    [TestMethod]
    public async Task Audit_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit?page=abc", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task Audit_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit?pageSize=-1", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task Audit_PageSizeZero_Succeeds()
    {
        var entry = new AuditEntryEntity
        {
            TableName   = "Quotes",
            RecordId    = Guid.Empty.ToString("D").ToUpperInvariant(),
            Operation   = AuditOperation.Insert,
            Agent       = "TestRunner/1.0",
            PerformedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        var stubReader = new StubAuditReader(new PagedItems<AuditEntryEntity>([entry], 1, 1, 1));
        using var factory = CreateFactory(stubReader);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit?pageSize=0", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "pageSize=0 means every row as one page — must succeed, not 422");
    }

    [TestMethod]
    public async Task Audit_PageBeyondLast_Returns422()
    {
        var entry = new AuditEntryEntity
        {
            TableName   = "Quotes",
            RecordId    = Guid.Empty.ToString("D").ToUpperInvariant(),
            Operation   = AuditOperation.Insert,
            Agent       = "TestRunner/1.0",
            PerformedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        var stubReader = new StubAuditReader(new PagedItems<AuditEntryEntity>([entry], 1, 1, 1));
        using var factory = CreateFactory(stubReader);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit?page=5", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "page beyond the last page must be rejected");
    }

    // ── GET audit — no auth required ─────────────────────────────────────────

    [TestMethod]
    public async Task GetAudit_NoApiKey_Returns200()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        // No X-Api-Key header — GET audit is public.
        var response = await client.GetAsync("/api/v1/admin/audit", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ── DELETE audit — auth required ─────────────────────────────────────────

    [TestMethod]
    public async Task DeleteAudit_NoKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        var response      = await client.DeleteAsync("/api/v1/admin/audit", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteAudit_CorrectKey_Returns204()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);
        var response = await client.DeleteAsync("/api/v1/admin/audit", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteAudit_WithTable_PassesTableToClearAsync()
    {
        string? capturedTable = "not-called";
        var capturingWriter   = new CapturingAuditWriter(t => capturedTable = t);

        using var factory = CreateFactory(auditWriter: capturingWriter);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        await client.DeleteAsync("/api/v1/admin/audit?table=Quotes", TestContext.CancellationToken);

        Assert.AreEqual("Quotes", capturedTable, "table query parameter must be forwarded to ClearAsync");
    }

    [TestMethod]
    public async Task DeleteAudit_NoTable_PassesNullToClearAsync()
    {
        string? capturedTable = "not-called";
        var capturingWriter   = new CapturingAuditWriter(t => capturedTable = t);

        using var factory = CreateFactory(auditWriter: capturingWriter);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        await client.DeleteAsync("/api/v1/admin/audit", TestContext.CancellationToken);

        Assert.IsNull(capturedTable, "null must be forwarded to ClearAsync when no table param is supplied");
    }

    // ── GET audit/date-range ─────────────────────────────────────────────────

    [TestMethod]
    public async Task GetAuditDateRange_BothTablesEmpty_ReturnsNulls()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit/date-range", TestContext.CancellationToken);
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        // Null properties are omitted app-wide (DefaultIgnoreCondition.WhenWritingNull) — absent means null here.
        Assert.IsFalse(doc.RootElement.TryGetProperty("earliestDate", out _), "earliestDate must be absent (null) when neither table has data");
        Assert.IsFalse(doc.RootElement.TryGetProperty("latestDate", out _), "latestDate must be absent (null) when neither table has data");
    }

    [TestMethod]
    public async Task GetAuditDateRange_CombinesBothTables_ReturnsOverallEarliestAndLatest()
    {
        var stubAuditReader = new StubAuditReader(new PagedItems<AuditEntryEntity>([], 1, 20, 0))
        {
            DateRange = (new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        var stubChangeReader = new StubChangeReader
        {
            DateRange = (new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
        };

        using var factory = CreateFactory(stubAuditReader, changeReader: stubChangeReader);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit/date-range", TestContext.CancellationToken);
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), doc.RootElement.GetProperty("earliestDate").GetDateTime(), "earliest must be the overall minimum across both tables, not just Audit_Entry's own");
        Assert.AreEqual(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), doc.RootElement.GetProperty("latestDate").GetDateTime(), "latest must be the overall maximum across both tables, not just Audit_Change's own");
    }

    [TestMethod]
    public async Task GetAuditDateRange_OnlyOneTableHasData_ReturnsThatTablesRange()
    {
        var stubAuditReader = new StubAuditReader(new PagedItems<AuditEntryEntity>([], 1, 20, 0))
        {
            DateRange = (new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        var stubChangeReader = new StubChangeReader { DateRange = (null, null) };

        using var factory = CreateFactory(stubAuditReader, changeReader: stubChangeReader);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit/date-range", TestContext.CancellationToken);
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), doc.RootElement.GetProperty("earliestDate").GetDateTime());
        Assert.AreEqual(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), doc.RootElement.GetProperty("latestDate").GetDateTime());
    }

    // ── GET audit/export ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExportAudit_ReturnsBothTablesData()
    {
        var entry = new AuditEntryEntity
        {
            TableName   = "Quotes",
            Operation   = AuditOperation.Insert,
            PerformedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        var change = new ChangeEntity
        {
            EntityType = "quote",
            EntityId   = Guid.NewGuid().ToString(),
            OccurredAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        var stubAuditReader  = new StubAuditReader(new PagedItems<AuditEntryEntity>([entry], 1, 20, 1));
        var stubChangeReader = new StubChangeReader { Items = [change] };

        using var factory = CreateFactory(stubAuditReader, changeReader: stubChangeReader);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit/export", TestContext.CancellationToken);
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, doc.RootElement.GetProperty("entries").GetArrayLength());
        Assert.AreEqual(1, doc.RootElement.GetProperty("changes").GetArrayLength());
        Assert.IsNotNull(response.Content.Headers.ContentDisposition, "must be a downloaded file, not an inline response");
        Assert.AreEqual("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
    }

    [TestMethod]
    public async Task ExportAuditTrail_ResponseShape_NoSafeValueWrapperInJson()
    {
        var entry = new AuditEntryEntity
        {
            TableName   = "Quotes",
            Operation   = AuditOperation.Insert,
            PerformedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        var change = new ChangeEntity
        {
            EntityType = "quote",
            EntityId   = Guid.NewGuid().ToString(),
            OccurredAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        var stubAuditReader  = new StubAuditReader(new PagedItems<AuditEntryEntity>([entry], 1, 20, 1));
        var stubChangeReader = new StubChangeReader { Items = [change] };

        using var factory = CreateFactory(stubAuditReader, changeReader: stubChangeReader);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit/export", TestContext.CancellationToken);
        var body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.DoesNotContain("\"raw\":", body, "SafeValue<T>'s internal wrapper must not leak into either entries or changes");
        Assert.DoesNotContain("\"isValid\":", body, "SafeValue<T>'s internal wrapper must not leak into either entries or changes");
    }

    [TestMethod]
    public async Task ExportAuditTrail_ChangeResponseShape_ActionIsEnumNotString()
    {
        var change = new ChangeEntity
        {
            EntityType      = "quote",
            EntityId        = Guid.NewGuid().ToString(),
            OccurredAt      = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Action          = new SafeValue<ChangeAction?>(ChangeAction.Modified.ToString(), ChangeAction.Modified),
            InitiatedByType = new SafeValue<InitiatorType?>(InitiatorType.WriteEndpoint.ToString(), InitiatorType.WriteEndpoint),
        };
        var stubAuditReader  = new StubAuditReader(new PagedItems<AuditEntryEntity>([], 1, 20, 0));
        var stubChangeReader = new StubChangeReader { Items = [change] };

        using var factory = CreateFactory(stubAuditReader, changeReader: stubChangeReader);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit/export", TestContext.CancellationToken);
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        var changeEl = doc.RootElement.GetProperty("changes")[0];

        Assert.AreEqual(JsonValueKind.String, changeEl.GetProperty("action").ValueKind, "action must serialize as a plain enum-name string, not a SafeValue wrapper object");
        Assert.AreEqual("Modified", changeEl.GetProperty("action").GetString());
        Assert.AreEqual(JsonValueKind.String, changeEl.GetProperty("initiatedByType").ValueKind);
        Assert.AreEqual("WriteEndpoint", changeEl.GetProperty("initiatedByType").GetString());
    }

    [TestMethod]
    public async Task ExportAudit_NoData_ReturnsEmptyArraysNot422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit/export", TestContext.CancellationToken);
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(0, doc.RootElement.GetProperty("entries").GetArrayLength());
        Assert.AreEqual(0, doc.RootElement.GetProperty("changes").GetArrayLength());
    }

    [TestMethod]
    public async Task ExportAudit_MalformedStartDate_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit/export?startDate=not-a-date", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ExportAudit_MalformedEndDate_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit/export?endDate=not-a-date", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ExportAudit_StartDateAfterEndDate_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/admin/audit/export?startDate=2026-03-01&endDate=2026-01-01", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ExportAudit_CombinedRowCountExceedsCap_Returns422NotTruncatedFile()
    {
        var stubAuditReader  = new StubAuditReader(new PagedItems<AuditEntryEntity>([], 1, 20, 0)) { CountOverride = 3 };
        var stubChangeReader = new StubChangeReader { CountOverride = 3 };

        using var factory = CreateFactory(stubAuditReader, changeReader: stubChangeReader, maxExportRows: 5);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit/export", TestContext.CancellationToken);
        var body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "combined 3+3=6 rows exceeds the 5-row cap");
        Assert.Contains("6", body);
        Assert.Contains("5", body);
    }

    [TestMethod]
    public async Task ExportAudit_CombinedRowCountAtCap_Succeeds()
    {
        var stubAuditReader  = new StubAuditReader(new PagedItems<AuditEntryEntity>([], 1, 20, 0)) { CountOverride = 2 };
        var stubChangeReader = new StubChangeReader { CountOverride = 3 };

        using var factory = CreateFactory(stubAuditReader, changeReader: stubChangeReader, maxExportRows: 5);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit/export", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "combined 2+3=5 rows is exactly at the cap, must not be rejected");
    }

    [TestMethod]
    public async Task ExportAudit_NoApiKey_Returns200()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        // No X-Api-Key header — export is public, matching GET /admin/audit's precedent.
        var response = await client.GetAsync("/api/v1/admin/audit/export", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class StubAuditReader(PagedItems<AuditEntryEntity> result) : IAuditEntryReader
    {
        public (DateTime?, DateTime?) DateRange { get; init; } = (null, null);
        public int? CountOverride { get; init; }

        public Task<PagedItems<AuditEntryEntity>> GetPagedAsync(string? table, string? recordId, int page, int pageSize)
            => Task.FromResult(result);
        public Task<IReadOnlyList<AuditEntryEntity>> GetAllInRangeAsync(DateTime? startDate, DateTime? endDate)
            => Task.FromResult<IReadOnlyList<AuditEntryEntity>>([.. result.Items]);
        public Task<int> CountInRangeAsync(DateTime? startDate, DateTime? endDate)
            => Task.FromResult(CountOverride ?? result.Items.Count);
        public Task<(DateTime? Earliest, DateTime? Latest)> GetDateRangeAsync()
            => Task.FromResult(DateRange);
    }

    private sealed class StubChangeReader : IChangeReader
    {
        public IReadOnlyList<ChangeEntity> Items { get; init; } = [];
        public (DateTime?, DateTime?) DateRange { get; init; } = (null, null);
        public int? CountOverride { get; init; }

        public Task<IReadOnlyList<ChangeEntity>> GetHistoryAsync(string entityType, string entityId)
            => Task.FromResult(Items);
        public Task<IReadOnlyList<ChangeEntity>> GetAllInRangeAsync(DateTime? startDate, DateTime? endDate)
            => Task.FromResult(Items);
        public Task<int> CountInRangeAsync(DateTime? startDate, DateTime? endDate)
            => Task.FromResult(CountOverride ?? Items.Count);
        public Task<(DateTime? Earliest, DateTime? Latest)> GetDateRangeAsync()
            => Task.FromResult(DateRange);
    }

    private sealed class CapturingAuditReader(Action<int> onCall) : IAuditEntryReader
    {
        public Task<PagedItems<AuditEntryEntity>> GetPagedAsync(string? table, string? recordId, int page, int pageSize)
        {
            onCall(pageSize);
            return Task.FromResult(new PagedItems<AuditEntryEntity>([], page, pageSize, 0));
        }
        public Task<IReadOnlyList<AuditEntryEntity>> GetAllInRangeAsync(DateTime? startDate, DateTime? endDate)
            => Task.FromResult<IReadOnlyList<AuditEntryEntity>>([]);
        public Task<int> CountInRangeAsync(DateTime? startDate, DateTime? endDate)
            => Task.FromResult(0);
        public Task<(DateTime? Earliest, DateTime? Latest)> GetDateRangeAsync()
            => Task.FromResult<(DateTime?, DateTime?)>((null, null));
    }

    private sealed class CapturingAuditWriter(Action<string?> onClear) : IAuditEntryWriter
    {
        public Task WriteAsync(AuditEntryEntity entry, IDbConnection connection, IDbTransaction? transaction = null)
            => Task.CompletedTask;
        public Task WriteAsync(IReadOnlyList<AuditEntryEntity> entries, IDbConnection connection, IDbTransaction? transaction = null)
            => Task.CompletedTask;
        public Task WriteAsync(AuditEntryEntity entry)
            => Task.CompletedTask;
        public Task ClearAsync(string? table = null)
        {
            onClear(table);
            return Task.CompletedTask;
        }
    }

    public TestContext TestContext { get; set; }
}
