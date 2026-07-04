namespace Quotinator.Data.Import;

/// <summary>How the seeder handles a duplicate record when the same primary key appears in more than one source file.</summary>
public enum DuplicateResolutionPolicy
{
    /// <summary>Keep the first occurrence; silently skip all later files that contain the same record.</summary>
    Skip,

    /// <summary>Replace the existing record with the version from the later file.</summary>
    NewestWins,

    /// <summary>Auto-fill blank fields from either side. When both sides have differing non-empty values, keep the existing (first-seen) value.</summary>
    MergeOurs,

    /// <summary>Auto-fill blank fields from either side. When both sides have differing non-empty values, take the incoming (later-seen) value.</summary>
    MergeTheirs,

    /// <summary>Do not auto-resolve — behaves identically to <see cref="Skip"/> today. Reserved for a future human-review workflow.</summary>
    Review
}
