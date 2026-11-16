namespace Quotinator.Api.Tests.Solution;

public partial class RepositoryStructureTests
{
    // A property, not a static field: static initializers across a partial class's files run in no
    // guaranteed order, and a field built from RepoRoot could read it before it is set.
    private static string ProgramCs => Path.Combine(RepoRoot, "src", "Quotinator.Api", "Program.cs");

    private static string SequenceCs => Path.Combine(RepoRoot, "src", "Quotinator.Api", "Startup", "DatabaseInitialisationSequence.cs");

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
    /// <remarks>
    /// #490 step 17 moved the wait, with the work it waits for, into <c>DatabaseInitialisationSequence.CompleteAsync</c>,
    /// which startup and the migrate endpoint both run. So the wait is asserted there, and its last statement, after
    /// everything that starts work; startup calling it is asserted by the test below.
    /// </remarks>
    [TestMethod]
    public void Program_WaitsForTheBackgroundWorkItStarted()
    {
        string sequence = File.ReadAllText(SequenceCs);
        int complete = sequence.IndexOf("public async Task CompleteAsync()", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, complete, "DatabaseInitialisationSequence.CompleteAsync was not found.");
        Assert.IsGreaterThan(sequence.LastIndexOf("backgroundWork.Start(", StringComparison.Ordinal),
            sequence.IndexOf("await backgroundWork.WhenAllCompletedAsync();", complete, StringComparison.Ordinal),
            "CompleteAsync does not wait for the background work, after the last place it starts some.");
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

        int waits = program.IndexOf("await initialisationSequence.CompleteAsync();", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, waits, "Program.cs does not run DatabaseInitialisationSequence.CompleteAsync, which holds the wait.");
        Assert.IsLessThan(
            program.IndexOf("MarkComplete();", StringComparison.Ordinal),
            waits,
            "startup marks itself complete before waiting for the work it began, so the gate opens while that work is still running");
    }
}
