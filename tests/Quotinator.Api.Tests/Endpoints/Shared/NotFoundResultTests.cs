using Microsoft.AspNetCore.Http.HttpResults;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Core.Services;

namespace Quotinator.Api.Tests.Endpoints.Shared;

[TestClass]
public class NotFoundResultTests
{
    private sealed class FakeLocalizer : IApiLocalizer
    {
        public string this[string key] => key;
    }

    private sealed class Widget
    {
        public string Name { get; init; } = string.Empty;
    }

    [TestMethod]
    public void OkOrNotFound_EntityNull_ReturnsProblem404()
    {
        var result = NotFoundResult.OkOrNotFound<Widget>(null, new FakeLocalizer(), "SomeNotFoundKey");

        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(result);
        Assert.AreEqual(404, problem.StatusCode);
        Assert.AreEqual("SomeNotFoundKey", problem.ProblemDetails.Detail);
    }

    [TestMethod]
    public void OkOrNotFound_EntityPresent_ReturnsOk200()
    {
        var widget = new Widget { Name = "Present" };

        var result = NotFoundResult.OkOrNotFound(widget, new FakeLocalizer(), "SomeNotFoundKey");

        var ok = Assert.IsInstanceOfType<Ok<Widget>>(result);
        Assert.AreEqual(widget, ok.Value);
    }
}
