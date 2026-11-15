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

    /// <summary>
    /// #490: both add-on configurations default <c>database_startup_check</c> to the depth the application uses
    /// when the setting is absent, for the reason <see cref="AddOnAutoUpdateSourcesDefault_MatchesTheApplicationDefault"/>
    /// gives. The add-on writes the depth lower case; the application reads it case-insensitively.
    /// </summary>
    [TestMethod]
    public void AddOnDatabaseStartupCheckDefault_MatchesTheApplicationDefault()
    {
        string expected = Quotinator.Api.Startup.DatabaseStartupCheckSetting.Default.ToString().ToLowerInvariant();
        List<string> mismatches = [];

        foreach (string addOn in (string[])["addon", "addon-beta"])
        {
            string config = File.ReadAllText(Path.Combine(RepoRoot, addOn, "config.yaml"));
            Match option = AddOnDatabaseStartupCheckOption().Match(config);

            string actual = option.Success ? option.Groups[1].Value : "(not found)";
            if (actual != expected)
                mismatches.Add($"{addOn}/config.yaml says {actual}");
        }

        Assert.IsEmpty(mismatches, $"the application defaults to {expected}: " + string.Join("; ", mismatches));
    }

    // The database_startup_check line inside the options block, not the schema block's type declaration.
    [GeneratedRegex(@"^options:\s*$(?:\r?\n  .*)*?\r?\n  database_startup_check: (\w+)", RegexOptions.Multiline)]
    private static partial Regex AddOnDatabaseStartupCheckOption();

    /// <summary>
    /// #490: every option an add-on's schema declares has a name and description in each of its translation
    /// files, for both channels. <c>CLAUDE.md</c>'s add-on option checklist asks for this by hand; nothing
    /// checked it, so an option could ship showing its raw key in the Home Assistant configuration panel.
    /// </summary>
    [TestMethod]
    public void EveryAddOnOption_IsTranslatedInEveryLanguage()
    {
        List<string> missing = [];

        foreach (string addOn in (string[])["addon", "addon-beta"])
        {
            string config = File.ReadAllText(Path.Combine(RepoRoot, addOn, "config.yaml"));
            string[] options = [.. SchemaBlockKeys().Matches(SchemaBlock().Match(config).Value).Select(m => m.Groups[1].Value)];
            Assert.IsNotEmpty(options, $"Positive control failed: no options were read from {addOn}/config.yaml, so this test could not fail.");

            foreach (string language in (string[])["en", "nl", "de"])
            {
                string translations = File.ReadAllText(Path.Combine(RepoRoot, addOn, "translations", $"{language}.yaml"));
                foreach (string option in options)
                {
                    if (!Regex.IsMatch(translations, $@"^  {Regex.Escape(option)}:\s*\r?\n    name: \S", RegexOptions.Multiline))
                        missing.Add($"{addOn}/translations/{language}.yaml: {option}");
                }
            }
        }

        Assert.IsEmpty(missing, "Add-on options without a translated name:\n  " + string.Join("\n  ", missing));
    }

    // The schema block: from its heading up to the next line that is not indented.
    [GeneratedRegex(@"^schema:\s*$(?:\r?\n  .*)*", RegexOptions.Multiline)]
    private static partial Regex SchemaBlock();

    // One option inside the schema block: a two-space-indented key.
    [GeneratedRegex(@"^  ([a-z_]+):", RegexOptions.Multiline)]
    private static partial Regex SchemaBlockKeys();
}
