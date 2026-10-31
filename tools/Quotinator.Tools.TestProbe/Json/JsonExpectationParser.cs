using System.Globalization;
using System.Xml.Linq;
using Quotinator.Tools.TestProbe.Enums;

namespace Quotinator.Tools.TestProbe.Json;

/// <summary>Turns the <c>json</c> command's options into expectations.</summary>
internal static class JsonExpectationParser
{
    private const string EqualsXmlOption = "--equals-xml";

    private static readonly Dictionary<string, JsonExpectationKind> KindsByOption = new()
    {
        ["--equals"] = JsonExpectationKind.EqualTo,
        ["--contains"] = JsonExpectationKind.Contains,
        ["--present"] = JsonExpectationKind.Present,
        ["--not-empty"] = JsonExpectationKind.NotEmpty,
        ["--greater-than"] = JsonExpectationKind.GreaterThan,
        ["--equals-field"] = JsonExpectationKind.EqualToField,
        ["--length-equals-field"] = JsonExpectationKind.LengthEqualToField,
        ["--length-equals"] = JsonExpectationKind.LengthEquals,
    };

    /// <summary>
    /// The expectations named by <c>--equals</c>, <c>--contains</c>, <c>--present</c>,
    /// <c>--not-empty</c>, <c>--greater-than</c>, <c>--equals-field</c>, <c>--length-equals-field</c>,
    /// <c>--length-equals</c> and <c>--equals-xml</c>, in no particular order, and one error per option
    /// that could not be read. <c>--equals-xml</c> becomes an equality expectation on the
    /// text the named file holds now, so a value the repository owns is never copied into a document.
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
            bool isEqualsXml = option == EqualsXmlOption;
            JsonExpectationKind kind = JsonExpectationKind.EqualTo;

            if (!isEqualsXml && !KindsByOption.TryGetValue(option, out kind))
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

            if (isEqualsXml)
            {
                string? expected = ReadXmlElement(value, out string? problem);

                if (expected is null)
                {
                    problems.Add($"{option}: {problem}");
                    continue;
                }

                value = expected;
            }

            if (kind is JsonExpectationKind.GreaterThan or JsonExpectationKind.LengthEquals && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                problems.Add($"{option} needs a number after '=', got '{value}'");
                continue;
            }

            expectations.Add(new JsonExpectation(kind, argument[..separator], value));
        }

        errors = problems;
        return expectations;
    }

    private static string? ReadXmlElement(string source, out string? problem)
    {
        int separator = source.LastIndexOf('#');

        if (separator <= 0 || separator == source.Length - 1)
        {
            problem = $"needs path=file#element, got '{source}'";
            return null;
        }

        string file = source[..separator];
        string element = source[(separator + 1)..];

        if (!File.Exists(file))
        {
            problem = $"no file at '{file}'";
            return null;
        }

        string? text = XDocument.Load(file).Descendants().FirstOrDefault(candidate => candidate.Name.LocalName == element)?.Value;
        problem = text is null ? $"'{file}' has no element '{element}'" : null;

        return text;
    }
}
