namespace Quotinator.Api.Tests.Solution;

public partial class RepositoryStructureTests
{
    // A property, not a static field: static initializers across a partial class's files run in no
    // guaranteed order, and a field built from RepoRoot could read it before it is set.
    private static string ProgramCs => Path.Combine(RepoRoot, "src", "Quotinator.Api", "Program.cs");

    /// <summary>
    /// Every piece of work startup begins in the background goes through
    /// <see cref="Quotinator.Api.Startup.StartupBackgroundWork"/>, which the host waits for when it stops
    /// (#424). A bare <c>Task.Run</c> is work nothing waits for: the host stops with its queries still
    /// open, and the work runs on against a disposed container.
    /// </summary>
    [TestMethod]
    public void Program_StartsNoWorkItDoesNotWaitFor()
    {
        Assert.DoesNotContain("Task.Run(", File.ReadAllText(ProgramCs));
    }
}
