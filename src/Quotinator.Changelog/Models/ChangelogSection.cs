namespace Quotinator.Changelog.Models;

/// <summary>A group of changes within a release (e.g. Added, Fixed, Changed).</summary>
/// <param name="Category">Category name (e.g. <c>Added</c>).</param>
/// <param name="Items">Change items for this category.</param>
public sealed record ChangelogSection(string Category, IReadOnlyList<string> Items);
