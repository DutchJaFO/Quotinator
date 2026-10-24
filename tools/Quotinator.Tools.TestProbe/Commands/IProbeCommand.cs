using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary>One command of the tool.</summary>
internal interface IProbeCommand
{
    /// <summary>Runs the command; its observations go to stdout and its failures to stderr.</summary>
    /// <param name="args">The command's arguments, without the command name.</param>
    /// <param name="stdin">The tool's standard input.</param>
    ExpectationResult Run(IReadOnlyList<string> args, TextReader stdin);
}
