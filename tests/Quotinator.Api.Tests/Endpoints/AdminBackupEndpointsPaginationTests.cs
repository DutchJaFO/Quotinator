using System.Net;
using System.Text.Json;
using Quotinator.Constants.Api;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>
/// The standard pagination contract, applied to <c>GET /api/v1/admin/backups</c> (#349).
/// <para>
/// All eight cases, because `CLAUDE.md` requires them of every new paginated GET and because coverage
/// of exactly these was what had to be closed retroactively across `/quotes`, `/admin/audit` and
/// `/import/actions` — this endpoint does not repeat that.
/// </para>
/// </summary>
[TestClass]
public class AdminBackupEndpointsPaginationTests
{
    private const string List = "/api/v1/admin/backups";

    /// <summary>Enough files that a default page does not cover them all, so page 2 is real.</summary>
    private const int FileCount = 25;

    /// <summary>Case 1 — page=0 is invalid, not silently treated as the first page.</summary>
    [TestMethod]
    public async Task Page_Zero_Returns422() => await AssertStatusAsync("?page=0", HttpStatusCode.UnprocessableEntity);

    /// <summary>Case 2 — a malformed page is 422, not the framework binder's bare 400.</summary>
    [TestMethod]
    public async Task Page_Malformed_Returns422() => await AssertStatusAsync("?page=abc", HttpStatusCode.UnprocessableEntity);

    /// <summary>Case 3 — a malformed pageSize is 422.</summary>
    [TestMethod]
    public async Task PageSize_Malformed_Returns422() => await AssertStatusAsync("?pageSize=abc", HttpStatusCode.UnprocessableEntity);

    /// <summary>Case 4 — a negative pageSize is 422.</summary>
    [TestMethod]
    public async Task PageSize_Negative_Returns422() => await AssertStatusAsync("?pageSize=-1", HttpStatusCode.UnprocessableEntity);

    /// <summary>Case 5 — above the maximum is refused, never silently clamped down to it.</summary>
    [TestMethod]
    public async Task PageSize_AboveMax_Returns422_NeverClamped() =>
        await AssertStatusAsync($"?pageSize={QueryParamDefaults.PageSizeMax + 1}", HttpStatusCode.UnprocessableEntity);

    /// <summary>Case 6 — pageSize=0 means every row, and the response reports the count it actually returned.</summary>
    [TestMethod]
    public async Task PageSize_Zero_ReturnsEveryRow_AndReportsTheActualCount()
    {
        using BackupTestHarness harness = CreateHarnessWithFiles();

        JsonDocument doc = await GetAsync(harness, "?pageSize=0");

        Assert.AreEqual(FileCount, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(FileCount, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(FileCount, doc.RootElement.GetProperty("pageSize").GetInt32(),
                        "the effective size is the count actually returned, not the literal 0 requested");
    }

    /// <summary>Case 7 — an omitted pageSize defaults to 20, asserted on the response field rather than on a 200.</summary>
    [TestMethod]
    public async Task PageSize_Omitted_DefaultsToTwenty()
    {
        using BackupTestHarness harness = CreateHarnessWithFiles();

        JsonDocument doc = await GetAsync(harness, string.Empty);

        Assert.AreEqual(QueryParamDefaults.PageSize, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.AreEqual(QueryParamDefaults.PageSize, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(FileCount, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    /// <summary>Case 8 — a page past the last one is its own 422, distinct from case 1 and never an empty page.</summary>
    [TestMethod]
    public async Task Page_BeyondLastPage_Returns422_DistinctFromPageZero()
    {
        using BackupTestHarness harness = CreateHarnessWithFiles();

        // 25 files at 20 per page is 2 pages; page 3 is past the end.
        HttpResponseMessage response = await harness.AuthenticatedClient()
            .GetAsync($"{List}?page=3", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(body), "a page past the end explains itself rather than returning an empty page");
    }

    /// <summary>A valid second page returns the remainder — the positive control for the eight refusals above.</summary>
    [TestMethod]
    public async Task Page_Two_ReturnsTheRemainingRows()
    {
        using BackupTestHarness harness = CreateHarnessWithFiles();

        JsonDocument doc = await GetAsync(harness, "?page=2");

        Assert.AreEqual(FileCount - QueryParamDefaults.PageSize, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(2, doc.RootElement.GetProperty("page").GetInt32());
    }

    private static BackupTestHarness CreateHarnessWithFiles()
    {
        BackupTestHarness harness = new BackupTestHarness();
        for (int i = 0; i < FileCount; i++)
            harness.WriteBackup($"quotinatordata_v5_2026010{i / 10}T0000000{i % 10:00}Z.db", sizeBytes: 16);
        return harness;
    }

    private async Task<JsonDocument> GetAsync(BackupTestHarness harness, string query)
    {
        HttpResponseMessage response = await harness.AuthenticatedClient().GetAsync($"{List}{query}", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    private async Task AssertStatusAsync(string query, HttpStatusCode expected)
    {
        using BackupTestHarness harness = CreateHarnessWithFiles();
        HttpResponseMessage response = await harness.AuthenticatedClient().GetAsync($"{List}{query}", TestContext.CancellationToken);
        Assert.AreEqual(expected, response.StatusCode);
    }

    public TestContext TestContext { get; set; }
}
