using Quotinator.Tools.TestProbe.Arguments;
using Quotinator.Tools.TestProbe.Json;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary>
/// <c>json</c>: asserts on a JSON document read from the file named by <c>--file</c>, or from stdin when
/// none is named.
/// </summary>
internal sealed class JsonCommand : IProbeCommand
{
    private const string FileOption = "--file";

    /// <summary>PowerShell 5.1 prefixes one when it pipes a string into a native process.</summary>
    private const char ByteOrderMark = '﻿';

    /// <inheritdoc/>
    public ExpectationResult Run(IReadOnlyList<string> args, TextReader stdin)
    {
        string? file = OptionReader.Single(args, FileOption);
        IReadOnlyList<JsonArrayCondition> conditions = JsonArrayFilter.Parse(WithoutFileOption(args), out IReadOnlyList<string> remaining, out IReadOnlyList<string> filterErrors);
        IReadOnlyList<JsonExpectation> expectations = JsonExpectationParser.Parse(remaining, out IReadOnlyList<string> errors);

        if (filterErrors.Count > 0 || errors.Count > 0)
        {
            return new ExpectationResult([], [.. filterErrors, .. errors]);
        }

        if (file is not null && !File.Exists(file))
        {
            return ExpectationResult.Failed($"{file} is not a file; nothing to read");
        }

        string json = (file is null ? stdin.ReadToEnd() : File.ReadAllText(file)).TrimStart(ByteOrderMark);

        if (conditions.Count == 0)
        {
            return JsonExpectationEvaluator.Evaluate(json, expectations);
        }

        string? filtered = JsonArrayFilter.Apply(json, conditions, out string? problem);

        if (filtered is null)
        {
            return ExpectationResult.Failed(problem ?? "the array could not be narrowed");
        }

        ExpectationResult result = JsonExpectationEvaluator.Evaluate(filtered, expectations);

        return result with { Observations = [.. JsonArrayFilter.Describe(conditions), .. result.Observations] };
    }

    private static List<string> WithoutFileOption(IReadOnlyList<string> args)
    {
        List<string> remaining = [];

        for (int index = 0; index < args.Count; index++)
        {
            if (args[index] == FileOption)
            {
                index++;
                continue;
            }

            remaining.Add(args[index]);
        }

        return remaining;
    }
}
