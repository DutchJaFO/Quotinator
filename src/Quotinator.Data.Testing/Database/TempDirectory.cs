using Microsoft.Data.Sqlite;

namespace Quotinator.Data.Testing.Database;

/// <summary>
/// A temporary directory a test owns, deleted when the test disposes it (#419).
/// <para>
/// One implementation of "clear the SQLite pools, then delete the directory", because it was written
/// out by hand in nineteen places across two test projects, and a copy in an
/// <c>ApplicationStopped</c> callback is what made a host's data directory outlive its host: deleting
/// while the host was still releasing handles threw, and the throw was swallowed where nobody could
/// see it. Disposing is a point the test chooses, which is why it belongs here rather than on a
/// lifetime event.
/// </para>
/// <para>
/// <b>Deletion is best effort.</b> A leaked temporary folder is untidy; a test failing in teardown is
/// worse, and tells you nothing about the thing the test was actually checking.
/// </para>
/// <para>
/// <b>But a failure is never silent.</b> Each one is written to the test run's error output and kept in
/// <see cref="CleanupFailures"/>, so a run that leaked says so and an assembly-level check can assert on
/// it. A cleanup that can fail invisibly is how this cost a bisecting session to find once already.
/// </para>
/// </summary>
/// <param name="prefix">Prefix for the directory name, identifying whatever asked for it.</param>
public sealed class TempDirectory(string prefix) : IDisposable
{
    private static readonly List<string> Failures = [];
    private static readonly Lock Gate = new();

    /// <summary>
    /// Absolute path to the directory, which exists from construction until disposal. Named after the
    /// prefix, so a directory that does leak says what left it behind.
    /// </summary>
    public string Path { get; } = Directory.CreateTempSubdirectory(prefix).FullName;

    /// <summary>
    /// Every deletion that failed this run, newest last. Empty on a clean run.
    /// <para>
    /// Process-wide, so each test project reports its own: an assembly cleanup can read this to report or
    /// assert that nothing leaked.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> CleanupFailures
    {
        get { lock (Gate) return [.. Failures]; }
    }

    /// <summary>Clears pooled SQLite connections, then deletes the directory. See the type summary.</summary>
    public void Dispose()
    {
        // A pooled connection keeps its file open, and the file keeps the directory.
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Record($"{Path}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Record(string failure)
    {
        lock (Gate)
            Failures.Add(failure);

        Console.Error.WriteLine($"[TempDirectory] could not delete {failure}");
    }
}
