using System.Text.RegularExpressions;

namespace Quotinator.Core.Services;

/// <summary>A group of changes within a release (e.g. Added, Fixed, Changed).</summary>
/// <param name="Category">Category name as written in the changelog (e.g. <c>Added</c>).</param>
/// <param name="Items">Raw item strings, with inline markdown preserved.</param>
public sealed record ChangelogSection(string Category, IReadOnlyList<string> Items);

/// <summary>A single versioned release parsed from <c>CHANGELOG.md</c>.</summary>
/// <param name="Version">Version string, e.g. <c>1.0.15</c>.</param>
/// <param name="Date">Release date string as written in the file, e.g. <c>2026-06-15</c>.</param>
/// <param name="Sections">Change sections for this release, in the order they appear in the file.</param>
public sealed record ChangelogRelease(string Version, string Date, IReadOnlyList<ChangelogSection> Sections);

/// <summary>Provides parsed changelog entries from <c>CHANGELOG.md</c>.</summary>
public interface IChangelogService
{
    /// <summary>All parsed releases, newest first.</summary>
    IReadOnlyList<ChangelogRelease> Releases { get; }
}

/// <summary>Reads and parses <c>CHANGELOG.md</c> from <see cref="AppContext.BaseDirectory"/> at startup.</summary>
public sealed class ChangelogService : IChangelogService
{
    /// <inheritdoc/>
    public IReadOnlyList<ChangelogRelease> Releases { get; }

    /// <summary>Initialises the service; parses the file if it exists, returns an empty list otherwise.</summary>
    public ChangelogService()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        Releases = File.Exists(path) ? Parse(File.ReadAllText(path)) : [];
    }

    private static IReadOnlyList<ChangelogRelease> Parse(string content)
    {
        var releases = new List<ChangelogRelease>();

        // Normalise line endings, then split on version-level headings (## [...])
        var blocks = content.Replace("\r\n", "\n").Split("\n## ");
        foreach (var block in blocks)
        {
            if (!block.StartsWith('[')) continue;

            var firstNewline = block.IndexOf('\n');
            if (firstNewline < 0) continue;

            var header = block[..firstNewline].Trim();
            var body = block[firstNewline..];

            var headerMatch = Regex.Match(header, @"^\[([^\]]+)\]\s*-\s*(.+)$");
            if (!headerMatch.Success) continue;

            var version = headerMatch.Groups[1].Value.Trim();
            var date = headerMatch.Groups[2].Value.Trim();

            var sections = new List<ChangelogSection>();
            foreach (var sectionBlock in body.Split("\n### ").Skip(1))
            {
                var lines = sectionBlock.Split('\n');
                var category = lines[0].Trim();
                var items = lines
                    .Skip(1)
                    .Select(l => l.Trim())
                    .Where(l => l.StartsWith("- "))
                    .Select(l => l[2..])
                    .ToList();

                if (items.Count > 0)
                    sections.Add(new ChangelogSection(category, items));
            }

            releases.Add(new ChangelogRelease(version, date, sections));
        }

        return releases;
    }
}
