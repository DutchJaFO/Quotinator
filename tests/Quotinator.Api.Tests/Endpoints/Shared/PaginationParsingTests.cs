using Microsoft.AspNetCore.Http.HttpResults;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Constants.Api;
using Quotinator.Core.Services;

namespace Quotinator.Api.Tests.Endpoints.Shared;

[TestClass]
public class PaginationParsingTests
{
    private sealed class FakeLocalizer : IApiLocalizer
    {
        public string this[string key] => key;
    }

    private static readonly FakeLocalizer Localizer = new();

    [TestMethod]
    public void TryParse_Malformed_Returns422WithDetail()
    {
        var ok = PaginationParsing.TryParse("abc", null, Localizer, out _, out _, out var error);

        Assert.IsFalse(ok);
        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(error);
        Assert.AreEqual(422, problem.StatusCode);
        Assert.AreEqual(ApiMessages.PageOutOfRange, problem.ProblemDetails.Detail);
    }

    [TestMethod]
    public void TryParse_PageBelowOne_Returns422()
    {
        var ok = PaginationParsing.TryParse("0", null, Localizer, out _, out _, out var error);

        Assert.IsFalse(ok);
        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(error);
        Assert.AreEqual(422, problem.StatusCode);
        Assert.AreEqual(ApiMessages.PageOutOfRange, problem.ProblemDetails.Detail);
    }

    [TestMethod]
    public void TryParse_PageSizeNegative_Returns422()
    {
        var ok = PaginationParsing.TryParse(null, "-1", Localizer, out _, out _, out var error);

        Assert.IsFalse(ok);
        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(error);
        Assert.AreEqual(422, problem.StatusCode);
        Assert.AreEqual(ApiMessages.PageSizeOutOfRange, problem.ProblemDetails.Detail);
    }

    [TestMethod]
    public void TryParse_PageSizeAbove500_Returns422()
    {
        var ok = PaginationParsing.TryParse(null, "501", Localizer, out _, out _, out var error);

        Assert.IsFalse(ok);
        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(error);
        Assert.AreEqual(422, problem.StatusCode);
        Assert.AreEqual(ApiMessages.PageSizeOutOfRange, problem.ProblemDetails.Detail);
    }

    [TestMethod]
    public void TryParse_PageSizeExactly500_Succeeds()
    {
        var ok = PaginationParsing.TryParse(null, "500", Localizer, out _, out var parsedPageSize, out var error);

        Assert.IsTrue(ok);
        Assert.IsNull(error);
        Assert.AreEqual(500, parsedPageSize);
    }

    [TestMethod]
    public void TryParse_PageSizeZero_SucceedsWithZero()
    {
        var ok = PaginationParsing.TryParse(null, "0", Localizer, out _, out var parsedPageSize, out var error);

        Assert.IsTrue(ok);
        Assert.IsNull(error);
        Assert.AreEqual(0, parsedPageSize, "pageSize=0 is valid — 'every row as one page'; the effective size is reported by the caller after the query runs, not by this parse step");
    }

    [TestMethod]
    public void TryParse_Omitted_UsesStandardDefaultOf20()
    {
        var ok = PaginationParsing.TryParse(null, null, Localizer, out var parsedPage, out var parsedPageSize, out var error);

        Assert.IsTrue(ok);
        Assert.IsNull(error);
        Assert.AreEqual(QueryParamDefaults.Page, parsedPage);
        Assert.AreEqual(QueryParamDefaults.PageSize, parsedPageSize);
    }

    [TestMethod]
    public void ValidatePageBeyondLast_PageBeyondLastPage_Returns422WithDistinctDetail()
    {
        var error = PaginationParsing.ValidatePageBeyondLast(page: 5, totalPages: 2, Localizer);

        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(error);
        Assert.AreEqual(422, problem.StatusCode);
        Assert.AreEqual(ApiMessages.PageBeyondLastPage, problem.ProblemDetails.Detail);
        Assert.AreNotEqual(ApiMessages.PageOutOfRange, problem.ProblemDetails.Detail);
        Assert.AreNotEqual(ApiMessages.PageSizeOutOfRange, problem.ProblemDetails.Detail);
    }

    [TestMethod]
    public void ValidatePageBeyondLast_PageWithinRange_ReturnsNull()
        => Assert.IsNull(PaginationParsing.ValidatePageBeyondLast(page: 2, totalPages: 2, Localizer));

    [TestMethod]
    public void ValidatePageBeyondLast_ZeroTotalPages_ReturnsNull()
        => Assert.IsNull(PaginationParsing.ValidatePageBeyondLast(page: 1, totalPages: 0, Localizer),
            "an empty result set has no pages at all — page 1 of nothing is not 'beyond the last page'");
}
