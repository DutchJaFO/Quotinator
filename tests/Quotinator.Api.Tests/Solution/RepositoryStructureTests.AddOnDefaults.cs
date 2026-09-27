using System.Text.RegularExpressions;
using Quotinator.Data.Import;

namespace Quotinator.Api.Tests.Solution;

public partial class RepositoryStructureTests
{
    /// <summary>
    /// Both add-on configurations default <c>auto_update_sources</c> to the value the application uses when
    /// the setting is absent (#424). The add-on passes its option to the application as
    /// <c>Quotinator__AutoUpdateSources</c>, so an add-on default that differs silently decides for every
    /// Home Assistant install what the application's own default decided against.
    /// <para>
    /// Read with a pattern over the one options line rather than a YAML parser: nothing else in the solution
    /// reads these files, and one line does not justify a package.
    /// </para>
    /// </summary>
    [TestMethod]
    public void AddOnAutoUpdateSourcesDefault_MatchesTheApplicationDefault()
    {
        string expected = SourceCacheUpdater.DefaultAutoUpdateSources ? "true" : "false";
        List<string> mismatches = [];

        foreach (string addOn in (string[])["addon", "addon-beta"])
        {
            string config = File.ReadAllText(Path.Combine(RepoRoot, addOn, "config.yaml"));
            Match option = AddOnAutoUpdateSourcesOption().Match(config);

            string actual = option.Success ? option.Groups[1].Value : "(not found)";
            if (actual != expected)
                mismatches.Add($"{addOn}/config.yaml says {actual}");
        }

        Assert.IsEmpty(mismatches, $"the application defaults to {expected}: " + string.Join("; ", mismatches));
    }

    // The auto_update_sources line inside the options block, not the schema block's type declaration.
    [GeneratedRegex(@"^options:\s*$(?:\r?\n  .*)*?\r?\n  auto_update_sources: (\w+)", RegexOptions.Multiline)]
    private static partial Regex AddOnAutoUpdateSourcesOption();
}
