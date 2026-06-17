namespace Quotinator.Core.Data;

/// <summary>How the seeder handles a duplicate record when the same primary key appears in more than one source file.</summary>
public enum DuplicateResolutionPolicy
{
    /// <summary>Keep the first occurrence; silently skip all later files that contain the same record.</summary>
    Skip,

    /// <summary>Replace the existing record with the version from the later file.</summary>
    Overwrite
}
