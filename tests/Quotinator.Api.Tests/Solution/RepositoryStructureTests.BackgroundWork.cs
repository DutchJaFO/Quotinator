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

    /// <summary>
    /// Startup waits for the work it began before it reports itself finished (#419).
    /// <para>
    /// <c>StartupBackgroundWorkTests</c> proves the helper waits; nothing proved that startup actually
    /// asks it to. Without this call the gate opened the moment the work was started, and a reset
    /// arriving in that window rebuilt the database while the what's-new write was still pointing at a
    /// version row it was about to remove.
    /// </para>
    /// </summary>
    [TestMethod]
    public void Program_WaitsForTheBackgroundWorkItStarted()
    {
        Assert.Contains("WhenAllCompletedAsync();", File.ReadAllText(ProgramCs), StringComparison.Ordinal);
    }

    /// <summary>
    /// And it waits <em>before</em> marking startup complete, which is the half that matters.
    /// <para>
    /// Pairs with <see cref="Program_WaitsForTheBackgroundWorkItStarted"/>: a call placed after
    /// <c>MarkComplete()</c> satisfies that test and fixes nothing, because the gate has already opened
    /// by the time anything is awaited. Compared by position for that reason, rather than by presence.
    /// </para>
    /// </summary>
    [TestMethod]
    public void Program_WaitsForThatWork_BeforeMarkingStartupComplete()
    {
        string program = File.ReadAllText(ProgramCs);

        Assert.IsLessThan(
            program.IndexOf("MarkComplete();", StringComparison.Ordinal),
            program.IndexOf("WhenAllCompletedAsync();", StringComparison.Ordinal),
            "startup marks itself complete before waiting for the work it began, so the gate opens while that work is still running");
    }
}
