using System.Globalization;
using Quotinator.Tools.TestProbe.Enums;

namespace Quotinator.Tools.TestProbe.Json;

/// <summary>Turns the <c>json</c> command's options into expectations.</summary>
internal static class JsonExpectationParser
{
    private static readonly Dictionary<string, JsonExpectationKind> KindsByOption = new()
    {
        ["--equals"] = JsonExpectationKind.EqualTo,
        ["--contains"] = JsonExpectationKind.Contains,
        ["--present"] = JsonExpectationKind.Present,
        ["--not-empty"] = JsonExpectationKind.NotEmpty,
        ["--greater-than"] = JsonExpectationKind.GreaterThan,
    };

    /// <summary>
    /// The expectations named by <c>--equals</c>, <c>--contains</c>, <c>--present</c> and
    /// <c>--not-empty</c>, in no particular order, and one error per option that could not be read.
    /// </summary>
    /// <param name="args">The command's arguments, without the command name.</param>
    /// <param name="errors">One line per unreadable option; empty when all were read.</param>
    internal static IReadOnlyList<JsonExpectation> Parse(IReadOnlyList<string> args, out IReadOnlyList<string> errors)
    {
        List<JsonExpectation> expectations = [];
        List<string> problems = [];

        for (int index = 0; index < args.Count; index++)
        {
            string option = args[index];

            if (!KindsByOption.TryGetValue(option, out JsonExpectationKind kind))
            {
                problems.Add($"unknown option '{option}'");
                break;
            }

            if (index + 1 >= args.Count)
            {
                problems.Add($"{option} needs a value");
                break;
            }

            string argument = args[++index];

            if (kind is JsonExpectationKind.Present or JsonExpectationKind.NotEmpty)
            {
                expectations.Add(new JsonExpectation(kind, argument, null));
                continue;
            }

            int separator = argument.IndexOf('=');

            if (separator <= 0)
            {
                problems.Add($"{option} needs path=value, got '{argument}'");
                continue;
            }

            string value = argument[(separator + 1)..];

            if (kind == JsonExpectationKind.GreaterThan && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                problems.Add($"{option} needs a number after '=', got '{value}'");
                continue;
            }

            expectations.Add(new JsonExpectation(kind, argument[..separator], value));
        }

        errors = problems;
        return expectations;
    }
}
