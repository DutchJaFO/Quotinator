using Quotinator.Data.Enums;

namespace Quotinator.Data.Import;

/// <summary>
/// Splits raw file content into literal lines for <see cref="Entities.FileResourceLineEntity"/> storage
/// (#251), detecting the file's line-ending style and whether it ends with a trailing newline so
/// <see cref="Join"/> can later reconstruct it exactly. Confirmed project assumption: line endings are
/// uniform within a single file — a file mixing <c>\r\n</c> and bare <c>\r</c>/<c>\n</c> is classified
/// by whichever style its first line break uses.
/// </summary>
public static class FileContentSplitter
{
    /// <summary>Splits <paramref name="content"/> into lines, detecting its line-ending style and trailing-newline presence.</summary>
    public static (LineEndingStyle LineEnding, bool EndsWithTrailingNewline, IReadOnlyList<string> Lines) Split(string content)
    {
        if (content.Length == 0)
            return (LineEndingStyle.LF, false, []);

        var (lineEnding, terminator) = content.Contains("\r\n", StringComparison.Ordinal)
            ? (LineEndingStyle.CRLF, "\r\n")
            : content.Contains('\r')
                ? (LineEndingStyle.CR, "\r")
                : (LineEndingStyle.LF, "\n");

        var endsWithTrailingNewline = content.EndsWith(terminator, StringComparison.Ordinal);
        var lines = content.Split(terminator).ToList();
        if (endsWithTrailingNewline)
            lines.RemoveAt(lines.Count - 1);

        return (lineEnding, endsWithTrailingNewline, lines);
    }

    /// <summary>Reassembles <paramref name="lines"/> into the original text, using the requested line-ending style and trailing-newline presence.</summary>
    public static string Join(IReadOnlyList<string> lines, LineEndingStyle lineEnding, bool endsWithTrailingNewline)
    {
        var terminator = lineEnding switch
        {
            LineEndingStyle.CRLF => "\r\n",
            LineEndingStyle.CR   => "\r",
            _                    => "\n",
        };

        var joined = string.Join(terminator, lines);
        return endsWithTrailingNewline ? joined + terminator : joined;
    }
}
