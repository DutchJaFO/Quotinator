namespace Quotinator.Data.Import;

/// <summary>
/// Duplicate-resolution policy for a source directory, read from its <c>manifest.json</c>
/// or derived from application configuration.
/// </summary>
/// <param name="Default">Policy applied to all entity types that do not have a type-specific override.</param>
/// <param name="Quotes">Override for quote rows. Null means use <see cref="Default"/>.</param>
/// <param name="Sources">Override for source rows. Null means use <see cref="Default"/>.</param>
/// <param name="Characters">Override for character rows. Null means use <see cref="Default"/>.</param>
/// <param name="People">Override for people rows. Null means use <see cref="Default"/>.</param>
/// <param name="Translations">Override for translation rows (both quote and source). Null means use <see cref="Default"/>.</param>
public record ManifestPolicy(
    DuplicateResolutionPolicy  Default,
    DuplicateResolutionPolicy? Quotes       = null,
    DuplicateResolutionPolicy? Sources      = null,
    DuplicateResolutionPolicy? Characters   = null,
    DuplicateResolutionPolicy? People       = null,
    DuplicateResolutionPolicy? Translations = null)
{
    /// <summary>Effective policy for quotes — falls back to <see cref="Default"/> when no type-specific override is set.</summary>
    public DuplicateResolutionPolicy ForQuotes       => Quotes       ?? Default;

    /// <summary>Effective policy for sources — falls back to <see cref="Default"/> when no type-specific override is set.</summary>
    public DuplicateResolutionPolicy ForSources      => Sources      ?? Default;

    /// <summary>Effective policy for characters — falls back to <see cref="Default"/> when no type-specific override is set.</summary>
    public DuplicateResolutionPolicy ForCharacters   => Characters   ?? Default;

    /// <summary>Effective policy for people — falls back to <see cref="Default"/> when no type-specific override is set.</summary>
    public DuplicateResolutionPolicy ForPeople       => People       ?? Default;

    /// <summary>Effective policy for translations — falls back to <see cref="Default"/> when no type-specific override is set.</summary>
    public DuplicateResolutionPolicy ForTranslations => Translations ?? Default;

    /// <summary>Fallback used when neither the manifest nor application configuration specifies a policy.</summary>
    public static ManifestPolicy HardcodedDefault => new(DuplicateResolutionPolicy.Skip);

    /// <summary>
    /// Returns the manifest's own policy when present; otherwise returns the configuration-level policy.
    /// The manifest is the highest-priority tier — if it has a <c>duplicateResolution</c> section it wins entirely.
    /// </summary>
    public static ManifestPolicy Resolve(ManifestPolicy? fromManifest, ManifestPolicy fromConfig)
        => fromManifest ?? fromConfig;
}
