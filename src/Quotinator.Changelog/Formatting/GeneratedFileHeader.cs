using System.Text;

namespace Quotinator.Changelog.Formatting;

/// <summary>Builds the generated-file notice header for changelog markdown files.</summary>
public static class GeneratedFileHeader
{
    /// <summary>Builds a header block identifying the file as generated, the source to edit, and the command to regenerate it.</summary>
    /// <param name="utcTimestamp">The UTC timestamp to embed in the header.</param>
    /// <param name="inputPath">Path to the source file that was used to generate this file.</param>
    /// <param name="regenerateCmd">The full command the user must run to regenerate the file.</param>
    /// <returns>A formatted markdown header block without a trailing newline.</returns>
    public static string Build(DateTime utcTimestamp, string inputPath, string regenerateCmd)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### *GENERATED FILE [{utcTimestamp:yyyy-MM-dd HH:mm} UTC] — do not edit by hand.*");
        sb.AppendLine();
        sb.AppendLine($"*Edit: `{inputPath}`*");
        sb.AppendLine();
        sb.Append($"*To regenerate: `{regenerateCmd}`*");
        return sb.ToString();
    }
}
