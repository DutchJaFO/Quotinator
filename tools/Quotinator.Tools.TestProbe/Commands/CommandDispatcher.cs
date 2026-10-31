using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary>Selects the command named by the first argument and maps its result to an exit code.</summary>
internal static class CommandDispatcher
{
    private static readonly Dictionary<string, IProbeCommand> Commands = new()
    {
        ["wait-field"] = new WaitFieldCommand(),
        ["json"] = new JsonCommand(),
        ["file"] = new FileCommand(),
        ["log"] = new LogCommand(),
        ["url"] = new UrlCommand(),
        ["seed-sources"] = new SeedSourcesCommand(),
        ["seed-noops"] = new SeedNoOpsCommand(),
    };

    /// <summary>
    /// Runs the command named by <paramref name="args"/>[0] with the rest as its arguments: <c>0</c> when
    /// every expectation held, <c>1</c> otherwise, including an unknown command or unreadable options.
    /// </summary>
    internal static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || !Commands.TryGetValue(args[0], out IProbeCommand? command))
        {
            stderr.WriteLine($"usage: <command> [options], where <command> is one of {string.Join(", ", Commands.Keys)}");
            return 1;
        }

        ExpectationResult result = command.Run(args[1..], stdin);

        foreach (string observation in result.Observations)
        {
            stdout.WriteLine(observation);
        }

        foreach (string failure in result.Failures)
        {
            stderr.WriteLine($"FAILED: {failure}");
        }

        return result.Holds ? 0 : 1;
    }
}
