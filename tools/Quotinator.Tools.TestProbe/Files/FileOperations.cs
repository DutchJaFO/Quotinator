using System.Text;
using System.Text.RegularExpressions;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Files;

/// <summary>File set-up and file checks for a test, each returning what it did or observed.</summary>
internal static class FileOperations
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="path"/>, replacing anything there, as UTF-8 without a
    /// byte-order mark, creating its directory when missing.
    /// </summary>
    internal static ExpectationResult WriteText(string path, string text)
    {
        CreateParentDirectory(path);
        File.WriteAllText(path, text, Utf8WithoutBom);
        return ExpectationResult.Passed($"wrote {path} ({new FileInfo(path).Length} bytes)");
    }

    /// <summary>Holds when <paramref name="path"/> is a file holding at least one byte, observing its length.</summary>
    internal static ExpectationResult ExpectNotEmpty(string path)
    {
        if (!File.Exists(path))
        {
            return ExpectationResult.Failed($"{path} is not a file");
        }

        long length = new FileInfo(path).Length;

        return length > 0
            ? ExpectationResult.Passed($"{path} holds {length} bytes")
            : ExpectationResult.Failed($"{path} is empty");
    }

    /// <summary>
    /// Copies the file at <paramref name="source"/> to <paramref name="destination"/>, replacing anything
    /// there, creating its directory when missing. Fails when the source does not exist.
    /// </summary>
    internal static ExpectationResult Copy(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return ExpectationResult.Failed($"{source} is not a file; nothing to copy");
        }

        CreateParentDirectory(destination);
        File.Copy(source, destination, overwrite: true);
        return ExpectationResult.Passed($"copied {source} to {destination}");
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

    /// <summary>
    /// Creates a file of <paramref name="bytes"/> bytes by setting its length, without writing its content,
    /// creating its directory when missing.
    /// </summary>
    internal static ExpectationResult Sparse(string path, long bytes)
    {
        CreateParentDirectory(path);
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
        stream.SetLength(bytes);

        return ExpectationResult.Passed($"created {path} at {stream.Length} bytes");
    }

    /// <summary>
    /// Sets the file's last write time, so a test can make it the oldest of several: for a backup the
    /// application orders by age. Fails when the file does not exist.
    /// </summary>
    internal static ExpectationResult SetWriteTime(string path, DateTime utc)
    {
        if (!File.Exists(path))
        {
            return ExpectationResult.Failed($"{path} is not a file", Describe(path));
        }

        File.SetLastWriteTimeUtc(path, utc);
        return ExpectationResult.Passed($"set {path}'s write time to {File.GetLastWriteTimeUtc(path):O}");
    }

    /// <summary>Creates the directory when missing. Fails when a file is in the way.</summary>
    internal static ExpectationResult CreateDirectory(string path)
    {
        if (File.Exists(path))
        {
            return ExpectationResult.Failed($"{path} is a file; cannot create a directory there");
        }

        Directory.CreateDirectory(path);
        return ExpectationResult.Passed($"{path} is a directory");
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

    /// <summary>
    /// Holds when some file directly in <paramref name="directory"/> holds exactly the same bytes as
    /// <paramref name="file"/>: for a downloaded copy of a stored file whose name is only known at run time.
    /// Every file in the directory is read, so a file another process still holds locked fails the check.
    /// </summary>
    internal static ExpectationResult ExpectIdenticalIn(string file, string directory)
    {
        if (!File.Exists(file))
        {
            return ExpectationResult.Failed($"{file} is not a file", Describe(file));
        }

        if (!Directory.Exists(directory))
        {
            return ExpectationResult.Failed($"{directory} is not a directory", Describe(directory));
        }

        string wanted = Sha256(file);
        string[] candidates = Directory.GetFiles(directory);
        string? match = candidates.FirstOrDefault(candidate => Sha256(candidate) == wanted);
        string observed = $"{file} SHA-256 {wanted}; {candidates.Length} file(s) in {directory}: {string.Join(", ", candidates.Select(Path.GetFileName))}";

        return match is not null
            ? ExpectationResult.Passed($"{file} is identical to {match} ({wanted})")
            : ExpectationResult.Failed($"expected a file in {directory} identical to {file}, found none", observed);
    }

    private static string Sha256(string path)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Holds when at least <paramref name="minimum"/> files directly in <paramref name="directory"/> match <paramref name="pattern"/>.</summary>
    internal static ExpectationResult ExpectMatches(string directory, string pattern, int minimum)
        => CheckMatches(directory, pattern, found => found >= minimum, $"at least {minimum}");

    private static ExpectationResult CheckMatches(string directory, string pattern, Func<int, bool> holds, string expected)
    {
        if (!Directory.Exists(directory))
        {
            return ExpectationResult.Failed($"{directory} is not a directory", Describe(directory));
        }

        string[] matches = Directory.GetFiles(directory, pattern);
        string observed = $"{matches.Length} match {pattern} in {directory}: {string.Join(", ", matches.Select(Path.GetFileName))}";

        return holds(matches.Length)
            ? ExpectationResult.Passed(observed)
            : ExpectationResult.Failed($"expected {expected} files matching {pattern} in {directory}, found {matches.Length}", observed);
    }

    /// <summary>Holds when nothing, neither file nor directory, exists at <paramref name="path"/>.</summary>
    internal static ExpectationResult ExpectAbsent(string path)
        => File.Exists(path) || Directory.Exists(path)
            ? ExpectationResult.Failed($"expected nothing at {path}", Describe(path))
            : ExpectationResult.Passed(Describe(path));

    /// <summary>Holds when exactly <paramref name="count"/> files directly in <paramref name="directory"/> match <paramref name="pattern"/>.</summary>
    internal static ExpectationResult ExpectMatchCount(string directory, string pattern, int count)
        => CheckMatches(directory, pattern, found => found == count, $"exactly {count}");

    /// <summary>Holds when the file's text matches the regular expression <paramref name="pattern"/>, ignoring case.</summary>
    internal static ExpectationResult ExpectText(string path, string pattern)
    {
        if (!File.Exists(path))
        {
            return ExpectationResult.Failed($"{path} is not a file", Describe(path));
        }

        return Regex.IsMatch(File.ReadAllText(path), pattern, RegexOptions.IgnoreCase)
            ? ExpectationResult.Passed($"{path} matches {pattern}")
            : ExpectationResult.Failed($"expected {path} to match {pattern}");
    }

    /// <summary>Holds when the file exists and its text does not match the regular expression <paramref name="pattern"/>, ignoring case.</summary>
    internal static ExpectationResult ExpectNoText(string path, string pattern)
    {
        if (!File.Exists(path))
        {
            return ExpectationResult.Failed($"{path} is not a file", Describe(path));
        }

        return Regex.IsMatch(File.ReadAllText(path), pattern, RegexOptions.IgnoreCase)
            ? ExpectationResult.Failed($"expected {path} not to match {pattern}")
            : ExpectationResult.Passed($"{path} does not match {pattern}");
    }

    private static void CreateParentDirectory(string path)
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(path));

        if (parent is not null)
        {
            Directory.CreateDirectory(parent);
        }
    }

    private static string Describe(string path)
        => Directory.Exists(path) ? $"{path} is a directory" : File.Exists(path) ? $"{path} is a file" : $"{path} does not exist";
}
