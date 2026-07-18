using Microsoft.AspNetCore.Http.HttpResults;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Constants.Api;
using Quotinator.Core.Services;

namespace Quotinator.Api.Tests.Endpoints.Shared;

[TestClass]
public class EntityFilterParsingTests
{
    private sealed class FakeLocalizer : IApiLocalizer
    {
        public string this[string key] => key switch
        {
            ApiMessages.MutuallyExclusiveEntityFilter => "Specify either {0} or {1}, not both.",
            ApiMessages.InvalidEntityFilterId          => "{0} must be a valid identifier.",
            ApiMessages.EntityFilterNoMatch            => "No {0} matches '{1}'.",
            _ => key,
        };
    }

    private static readonly FakeLocalizer Localizer = new();
    private static readonly Guid KnownId = Guid.NewGuid();
    private static readonly EntityFilterNames Names = new("Source", "sourceId", "source");

    private static Task<Guid?> ResolveKnownName(string name) =>
        Task.FromResult(name == "Airplane!" ? KnownId : (Guid?)null);

    [TestMethod]
    public async Task ResolveAsync_BothSupplied_ReturnsError()
    {
        var result = await EntityFilterParsing.ResolveAsync(KnownId.ToString(), "Airplane!", Names, ResolveKnownName, Localizer);

        Assert.AreEqual(EntityFilterOutcome.Error, result.Outcome);
        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(result.Error);
        Assert.AreEqual(422, problem.StatusCode);
        Assert.AreEqual("Specify either sourceId or source, not both.", problem.ProblemDetails.Detail);
    }

    [TestMethod]
    public async Task ResolveAsync_IdOnlyWellFormed_ReturnsResolved()
    {
        var result = await EntityFilterParsing.ResolveAsync(KnownId.ToString(), null, Names, ResolveKnownName, Localizer);

        Assert.AreEqual(EntityFilterOutcome.Resolved, result.Outcome);
        Assert.AreEqual(KnownId, result.Id);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task ResolveAsync_IdOnlyMalformed_ReturnsError()
    {
        var result = await EntityFilterParsing.ResolveAsync("not-a-guid", null, Names, ResolveKnownName, Localizer);

        Assert.AreEqual(EntityFilterOutcome.Error, result.Outcome);
        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(result.Error);
        Assert.AreEqual(422, problem.StatusCode);
        Assert.AreEqual("sourceId must be a valid identifier.", problem.ProblemDetails.Detail);
    }

    [TestMethod]
    public async Task ResolveAsync_NameResolves_ReturnsResolved()
    {
        var result = await EntityFilterParsing.ResolveAsync(null, "Airplane!", Names, ResolveKnownName, Localizer);

        Assert.AreEqual(EntityFilterOutcome.Resolved, result.Outcome);
        Assert.AreEqual(KnownId, result.Id);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task ResolveAsync_NameDoesNotResolve_ReturnsNotFoundWithMessage()
    {
        var result = await EntityFilterParsing.ResolveAsync(null, "Nonexistent Source", Names, ResolveKnownName, Localizer);

        Assert.AreEqual(EntityFilterOutcome.NotFound, result.Outcome);
        Assert.IsNull(result.Id);
        Assert.IsNull(result.Error, "a name that doesn't resolve is a legitimate zero-results case, not an error");
        Assert.AreEqual("No Source matches 'Nonexistent Source'.", result.Message);
    }

    [TestMethod]
    public async Task ResolveAsync_NeitherSupplied_ReturnsNoFilter()
    {
        var result = await EntityFilterParsing.ResolveAsync(null, null, Names, ResolveKnownName, Localizer);

        Assert.AreEqual(EntityFilterOutcome.NoFilter, result.Outcome);
        Assert.IsNull(result.Id);
        Assert.IsNull(result.Message);
        Assert.IsNull(result.Error);
    }
}
