using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

/// <summary>
/// Verifies <see cref="InitiatorContext"/>'s <see cref="AsyncLocal{T}"/> isolation — the same
/// pattern <see cref="CallerContext"/> already relies on, extended here to cover the additional
/// <see cref="IInitiatorContext.InitiatedByType"/>/<see cref="IInitiatorContext.InitiatedById"/>
/// properties (#56).
/// </summary>
[TestClass]
public class InitiatorContextTests
{
    [TestMethod]
    public async Task ConcurrentAsyncFlows_DoNotSeeEachOthersInitiatorValues()
    {
        var context = new InitiatorContext();

        var taskA = Task.Run(async () =>
        {
            context.InitiatedByType = InitiatorType.Seed;
            context.InitiatedById   = "seed-batch";
            await Task.Delay(50);
            return (context.InitiatedByType, context.InitiatedById);
        });

        var taskB = Task.Run(async () =>
        {
            context.InitiatedByType = InitiatorType.Import;
            context.InitiatedById   = "import-batch";
            await Task.Delay(50);
            return (context.InitiatedByType, context.InitiatedById);
        });

        var (typeA, idA) = await taskA;
        var (typeB, idB) = await taskB;

        Assert.AreEqual(InitiatorType.Seed, typeA);
        Assert.AreEqual("seed-batch", idA);
        Assert.AreEqual(InitiatorType.Import, typeB);
        Assert.AreEqual("import-batch", idB);
    }

    [TestMethod]
    public void NewInstance_InitiatedByTypeAndId_DefaultToNull()
    {
        var context = new InitiatorContext();

        Assert.IsNull(context.InitiatedByType);
        Assert.IsNull(context.InitiatedById);
        Assert.IsNull(context.Agent);
    }

    [TestMethod]
    public void Agent_SetIndependentlyOfInitiatorFields()
    {
        var context = new InitiatorContext { Agent = "test-agent", InitiatedByType = InitiatorType.WriteEndpoint, InitiatedById = "PUT /api/v1/quotes/{id}" };

        Assert.AreEqual("test-agent", context.Agent);
        Assert.AreEqual(InitiatorType.WriteEndpoint, context.InitiatedByType);
        Assert.AreEqual("PUT /api/v1/quotes/{id}", context.InitiatedById);
    }
}
