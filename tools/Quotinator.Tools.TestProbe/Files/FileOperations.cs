using System.Text;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Files;

/// <summary>File set-up and file checks for a test, each returning what it did or observed.</summary>
internal static class FileOperations
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/>, replacing anything there, as UTF-8 without a byte-order mark.</summary>
    internal static ExpectationResult WriteText(string path, string text)
    {
        File.WriteAllText(path, text, Utf8WithoutBom);
        return ExpectationResult.Passed($"wrote {path} ({new FileInfo(path).Length} bytes)");
    }

    /// <summary>Cuts the file to half its length, rounded down, observing both lengths. Fails when the file does not exist.</summary>
    internal static ExpectationResult TruncateHalf(string path)
    {
        if (!File.Exists(path))
        {
            return ExpectationResult.Failed($"{path} is not a file; nothing to truncate");
        }

        using FileStream stream = new(path, FileMode.Open, FileAccess.Write);
        long before = stream.Length;
        stream.SetLength(before / 2);

        return ExpectationResult.Passed($"truncated {path} from {before} to {stream.Length} bytes");
    }

    /// <summary>Creates a file of <paramref name="bytes"/> bytes by setting its length, without writing its content.</summary>
    internal static ExpectationResult Sparse(string path, long bytes)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
        stream.SetLength(bytes);

        return ExpectationResult.Passed($"created {path} at {stream.Length} bytes");
    }

    /// <summary>Deletes the file, or the directory with everything in it, when it exists; nothing to delete is not a failure.</summary>
    internal static ExpectationResult Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return ExpectationResult.Passed($"deleted directory {path}");
        }

        if (File.Exists(path))
        {
            File.Delete(path);
            return ExpectationResult.Passed($"deleted file {path}");
        }

        return ExpectationResult.Passed($"{path} did not exist");
    }

    /// <summary>Holds when <paramref name="path"/> is a file.</summary>
    internal static ExpectationResult ExpectFile(string path)
        => File.Exists(path)
            ? ExpectationResult.Passed($"{path} is a file")
            : ExpectationResult.Failed($"{path} is not a file", Describe(path));

    /// <summary>Holds when <paramref name="path"/> is a directory.</summary>
    internal static ExpectationResult ExpectDirectory(string path)
        => Directory.Exists(path)
            ? ExpectationResult.Passed($"{path} is a directory")
            : ExpectationResult.Failed($"{path} is not a directory", Describe(path));

    /// <summary>Holds when <paramref name="path"/> is a directory with nothing in it.</summary>
    internal static ExpectationResult ExpectEmptyDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return ExpectationResult.Failed($"{path} is not a directory", Describe(path));
        }

        string[] entries = Directory.GetFileSystemEntries(path);

        return entries.Length == 0
            ? ExpectationResult.Passed($"{path} is empty")
            : ExpectationResult.Failed($"{path} is not empty", $"{path} holds: {string.Join(", ", entries.Select(Path.GetFileName))}");
    }

    /// <summary>Holds when the file is exactly <paramref name="bytes"/> long.</summary>
    internal static ExpectationResult ExpectLength(string path, long bytes)
    {
        if (!File.Exists(path))
        {
            return ExpectationResult.Failed($"{path} is not a file", Describe(path));
        }

        long length = new FileInfo(path).Length;
        string observed = $"{path} is {length} bytes";

        return length == bytes
            ? ExpectationResult.Passed(observed)
            : ExpectationResult.Failed($"expected {path} to be {bytes} bytes, it is {length}", observed);
    }

    /// <summary>Holds when at least <paramref name="minimum"/> files directly in <paramref name="directory"/> match <paramref name="pattern"/>.</summary>
    internal static ExpectationResult ExpectMatches(string directory, string pattern, int minimum)
    {
        if (!Directory.Exists(directory))
        {
            return ExpectationResult.Failed($"{directory} is not a directory", Describe(directory));
        }

        string[] matches = Directory.GetFiles(directory, pattern);
        string observed = $"{matches.Length} match {pattern} in {directory}: {string.Join(", ", matches.Select(Path.GetFileName))}";

        return matches.Length >= minimum
            ? ExpectationResult.Passed(observed)
            : ExpectationResult.Failed($"expected at least {minimum} files matching {pattern} in {directory}, found {matches.Length}", observed);
    }

    private static string Describe(string path)
        => Directory.Exists(path) ? $"{path} is a directory" : File.Exists(path) ? $"{path} is a file" : $"{path} does not exist";
}
