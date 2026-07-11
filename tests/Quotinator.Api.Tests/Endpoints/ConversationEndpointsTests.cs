using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class ConversationEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
            }));

    [TestMethod]
    public async Task GetById_KnownId_ReturnsOkWithLinesInOrder()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/conversations/{FakeQuoteService.SampleConversation.Id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(FakeQuoteService.SampleConversation.Id, doc.RootElement.GetProperty("id").GetString());
        var lines = doc.RootElement.GetProperty("lines");
        Assert.AreEqual(2, lines.GetArrayLength());
        Assert.AreEqual(1, lines[0].GetProperty("order").GetInt32());
        Assert.AreEqual("stage_direction", lines[0].GetProperty("type").GetString());
        Assert.AreEqual(2, lines[1].GetProperty("order").GetInt32());
        Assert.AreEqual("quote", lines[1].GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task GetById_QuoteLine_HasNoRecursiveConversationsField()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/conversations/{FakeQuoteService.SampleConversation.Id}");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var quoteLine = doc.RootElement.GetProperty("lines")[1];
        Assert.IsFalse(quoteLine.GetProperty("quote").TryGetProperty("conversations", out _),
            "An embedded quote line must never carry its own conversations membership array");
    }

    [TestMethod]
    public async Task GetById_UppercaseCasedId_StillResolves()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/conversations/{FakeQuoteService.SampleConversation.Id.ToUpperInvariant()}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetById_UnknownId_Returns404WithLocalisedDetail()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations/00000000-0000-0000-0000-000000000000");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("detail", out var detail));
        Assert.IsFalse(string.IsNullOrWhiteSpace(detail.GetString()));
    }

    [TestMethod]
    public async Task GetById_UnknownId_WithAcceptLanguageNl_ReturnsDutchDetail()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("nl");
        var response = await client.GetAsync("/api/v1/conversations/00000000-0000-0000-0000-000000000000");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        StringAssert.Contains(doc.RootElement.GetProperty("detail").GetString(), "conversatie");
    }

    [TestMethod]
    public async Task GetById_InvalidLang_Returns400()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/conversations/{FakeQuoteService.SampleConversation.Id}?lang=not-a-lang-code-at-all");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
