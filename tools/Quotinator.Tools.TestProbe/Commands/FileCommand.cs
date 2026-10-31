using System.Globalization;
using Quotinator.Tools.TestProbe.Files;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary>
/// <c>file</c>: performs file set-up and file checks in the order given, stopping at the first that fails.
/// </summary>
internal sealed class FileCommand : IProbeCommand
{
    private static readonly Dictionary<string, (int Arity, Func<string[], ExpectationResult> Operation)> Operations = new()
    {
        ["--write-text"] = (2, values => FileOperations.WriteText(values[0], values[1])),
        ["--truncate-half"] = (1, values => FileOperations.TruncateHalf(values[0])),
        ["--sparse"] = (2, values => WithNumber(values[1], bytes => FileOperations.Sparse(values[0], bytes))),
        ["--create-directory"] = (1, values => FileOperations.CreateDirectory(values[0])),
        ["--delete"] = (1, values => FileOperations.Delete(values[0])),
        ["--expect-file"] = (1, values => FileOperations.ExpectFile(values[0])),
        ["--expect-directory"] = (1, values => FileOperations.ExpectDirectory(values[0])),
        ["--expect-empty-directory"] = (1, values => FileOperations.ExpectEmptyDirectory(values[0])),
        ["--expect-length"] = (2, values => WithNumber(values[1], bytes => FileOperations.ExpectLength(values[0], bytes))),
        ["--expect-absent"] = (1, values => FileOperations.ExpectAbsent(values[0])),
        ["--expect-match-count"] = (3, values => WithNumber(values[2], count => FileOperations.ExpectMatchCount(values[0], values[1], (int)count))),
        ["--expect-matches"] = (3, values => WithNumber(values[2], minimum => FileOperations.ExpectMatches(values[0], values[1], (int)minimum))),
        ["--expect-text"] = (2, values => FileOperations.ExpectText(values[0], values[1])),
        ["--expect-no-text"] = (2, values => FileOperations.ExpectNoText(values[0], values[1])),
    };

    /// <inheritdoc/>
    public ExpectationResult Run(IReadOnlyList<string> args, TextReader stdin)
    {
        if (args.Count == 0)
        {
            return ExpectationResult.Failed("file needs at least one operation");
        }

        List<string> observations = [];

        for (int index = 0; index < args.Count; index++)
        {
            if (!Operations.TryGetValue(args[index], out (int Arity, Func<string[], ExpectationResult> Operation) entry))
            {
                return new ExpectationResult(observations, [$"unknown option '{args[index]}'"]);
            }

            if (index + entry.Arity >= args.Count)
            {
                return new ExpectationResult(observations, [$"{args[index]} needs {entry.Arity} value(s)"]);
            }

            ExpectationResult result = entry.Operation([.. args.Skip(index + 1).Take(entry.Arity)]);
            observations.AddRange(result.Observations);

            if (!result.Holds)
            {
                return new ExpectationResult(observations, result.Failures);
            }

            index += entry.Arity;
        }

        return new ExpectationResult(observations, []);
    }

    private static ExpectationResult WithNumber(string text, Func<long, ExpectationResult> operation)
        => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long number)
            ? operation(number)
            : ExpectationResult.Failed($"'{text}' is not a whole number");
}
