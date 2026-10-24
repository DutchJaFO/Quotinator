namespace Quotinator.Tools.TestProbe.Arguments;

/// <summary>Reads options from a command's arguments.</summary>
internal static class OptionReader
{
    /// <summary>
    /// Every occurrence of <paramref name="option"/>, each with the <paramref name="arity"/> values that
    /// follow it. An occurrence with too few values after it is reported in <paramref name="incomplete"/>.
    /// </summary>
    /// <param name="args">The command's arguments, without the command name.</param>
    /// <param name="option">The option, including its leading dashes.</param>
    /// <param name="arity">How many values follow each occurrence.</param>
    /// <param name="incomplete">True when an occurrence lacked its values.</param>
    internal static IReadOnlyList<string[]> All(IReadOnlyList<string> args, string option, int arity, out bool incomplete)
    {
        List<string[]> found = [];
        incomplete = false;

        for (int index = 0; index < args.Count; index++)
        {
            if (args[index] != option)
            {
                continue;
            }

            if (index + arity >= args.Count)
            {
                incomplete = true;
                break;
            }

            found.Add([.. args.Skip(index + 1).Take(arity)]);
            index += arity;
        }

        return found;
    }

    /// <summary>The value after the last occurrence of <paramref name="option"/>, or null when it is absent.</summary>
    /// <param name="args">The command's arguments, without the command name.</param>
    /// <param name="option">The option, including its leading dashes.</param>
    internal static string? Single(IReadOnlyList<string> args, string option)
    {
        IReadOnlyList<string[]> found = All(args, option, 1, out _);

        return found.Count == 0 ? null : found[^1][0];
    }
}
