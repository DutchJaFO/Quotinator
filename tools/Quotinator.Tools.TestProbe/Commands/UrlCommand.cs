using System.Text.Json;
using System.Text.RegularExpressions;
using Quotinator.Tools.TestProbe.Arguments;
using Quotinator.Tools.TestProbe.Files;
using Quotinator.Tools.TestProbe.Json;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary>
/// <c>url</c>: fills a URL template with values read from saved JSON responses and writes it to a file, so
/// a later <c>http.csx --url-file</c> can use an id an earlier response returned without a shell variable.
/// </summary>
internal sealed partial class UrlCommand : IProbeCommand
{
    private const string TemplateOption = "--template";
    private const string ValueOption = "--value";
    private const string UpperValueOption = "--upper-value";
    private const string OutOption = "--out";

    /// <inheritdoc/>
    public ExpectationResult Run(IReadOnlyList<string> args, TextReader stdin)
    {
        string? template = OptionReader.Single(args, TemplateOption);
        string? output = OptionReader.Single(args, OutOption);

        if (template is null || output is null)
        {
            return ExpectationResult.Failed($"url needs {TemplateOption} and {OutOption}");
        }

        string url = template;

        foreach ((string option, bool upper) in new[] { (ValueOption, false), (UpperValueOption, true) })
        {
            foreach (string[] value in OptionReader.All(args, option, 1, out _))
            {
                string? failure = Fill(ref url, value[0], upper);

                if (failure is not null)
                {
                    return ExpectationResult.Failed(failure);
                }
            }
        }

        if (Placeholder().IsMatch(url))
        {
            return ExpectationResult.Failed($"no value was given for a placeholder in '{url}'");
        }

        FileOperations.WriteText(output, url);
        return ExpectationResult.Passed(url);
    }

    private static string? Fill(ref string url, string argument, bool upper)
    {
        int nameEnd = argument.IndexOf('=');
        int pathStart = argument.LastIndexOf('#');

        if (nameEnd <= 0 || pathStart <= nameEnd + 1 || pathStart == argument.Length - 1)
        {
            return $"a value needs name=file#path, got '{argument}'";
        }

        string name = argument[..nameEnd];
        string file = argument[(nameEnd + 1)..pathStart];
        string path = argument[(pathStart + 1)..];
        string placeholder = $"{{{name}}}";

        if (!url.Contains(placeholder, StringComparison.Ordinal))
        {
            return $"the template has no {placeholder}";
        }

        if (!File.Exists(file))
        {
            return $"{file} is not a file; nothing to read {name} from";
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));

        if (!JsonPathReader.TryRead(document.RootElement, path, out JsonElement value))
        {
            return $"{file} has no field '{path}'";
        }

        string text = JsonPathReader.Render(value);
        url = url.Replace(placeholder, Uri.EscapeDataString(upper ? text.ToUpperInvariant() : text), StringComparison.Ordinal);
        return null;
    }

    [GeneratedRegex(@"\{[A-Za-z]+\}")]
    private static partial Regex Placeholder();
}
