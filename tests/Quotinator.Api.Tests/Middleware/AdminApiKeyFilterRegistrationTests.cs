using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Endpoints.Filters;

namespace Quotinator.Api.Tests.Middleware;

/// <summary>
/// How <see cref="AdminApiKeyFilter"/> is registered, and that registering it that way costs no
/// exceptions (#397).
/// <para>
/// `AddEndpointFilter&lt;TFilterType&gt;()` builds its factory by probing for a constructor taking
/// `EndpointFilterFactoryContext` and catching the <see cref="InvalidOperationException"/> when there
/// is none (dotnet/runtime#67309). With six registrations that was six thrown exceptions on every
/// startup — invisible until this issue logged them. The instance overload closes over a filter and
/// never activates anything, so the probe does not happen.
/// </para>
/// </summary>
[TestClass]
public class AdminApiKeyFilterRegistrationTests
{
    private static readonly string ApiProjectRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Quotinator.Api"));

    /// <summary>
    /// No endpoint group activates the filter by type. This is the assertion that keeps the six
    /// exceptions from coming back, and it is source-scanned because the subject is how the
    /// registration is written.
    /// </summary>
    [TestMethod]
    public void NoEndpointGroupActivatesTheFilterByType()
    {
        List<string> violations =
        [
            .. Directory
                .GetFiles(ApiProjectRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(f => File.ReadAllText(f).Contains($"AddEndpointFilter<{nameof(AdminApiKeyFilter)}>", StringComparison.Ordinal))
                .Select(f => Path.GetFileName(f)),
        ];

        Assert.IsEmpty(violations,
            $"These files activate the filter by type:\n{string.Join("\n", violations)}\n\n"
            + "Each one throws an InvalidOperationException the framework catches internally, on every "
            + "startup. Pass the registered instance instead: "
            + ".AddEndpointFilter(app.Services.GetRequiredService<AdminApiKeyFilter>()).");
    }

    /// <summary>The filter is in the container, so the instance passed to each group is a resolved one.</summary>
    [TestMethod]
    public async Task TheFilterIsResolvableFromTheContainer()
    {
        await using QuotinatorWebApplicationFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();

        Assert.IsNotNull(scope.ServiceProvider.GetService<AdminApiKeyFilter>(),
            "the filter is constructed by DI, not by a bare new at six call sites");
    }

    /// <summary>A request without the key is refused — the filter's whole purpose, through the new constructor.</summary>
    [TestMethod]
    public async Task MissingKey_IsUnauthorized()
    {
        object? result = await Invoke(configuredKey: "expected-key", providedKey: null);

        Assert.IsNotNull(result);
        Assert.DoesNotContain("reached", result.ToString() ?? string.Empty,
            "the endpoint must not run when the key is missing");
    }

    /// <summary>A request with the matching key reaches the endpoint — the positive control.</summary>
    [TestMethod]
    public async Task MatchingKey_ReachesTheEndpoint()
    {
        object? result = await Invoke(configuredKey: "expected-key", providedKey: "expected-key");

        Assert.AreEqual("reached", result);
    }

    private static async ValueTask<object?> Invoke(string configuredKey, string? providedKey)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Quotinator:AdminApiKey"] = configuredKey })
            .Build();

        DefaultHttpContext httpContext = new();
        if (providedKey is not null)
            httpContext.Request.Headers["X-Api-Key"] = providedKey;

        AdminApiKeyFilter filter = new(configuration);

        return await filter.InvokeAsync(
            EndpointFilterInvocationContext.Create(httpContext),
            _ => ValueTask.FromResult<object?>("reached"));
    }
}
