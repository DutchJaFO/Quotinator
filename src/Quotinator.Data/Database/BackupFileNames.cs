namespace Quotinator.Data.Database;

/// <summary>
/// Decides whether a caller-supplied backup identifier is a file name this application will act on
/// (#349), and where it resolves to.
/// <para>
/// One guard, called by every route that takes a <c>{name}</c> — delete, download, and #352's restore.
/// Written once deliberately: a path-traversal check implemented twice is a path-traversal check that
/// will eventually differ in one of the two places, and only one of them will be the one an attacker
/// finds.
/// </para>
/// <para>
/// Both halves matter. The name is rejected before the filesystem is touched at all, <em>and</em> the
/// resolved path is verified to sit inside the backups folder — the second check catches anything the
/// first did not anticipate, rather than trusting the first to have been exhaustive.
/// </para>
/// </summary>
public static class BackupFileNames
{
    /// <summary>
    /// The zero-byte file <see cref="DatabaseInitializer.CheckBackupReadiness"/> writes and deletes to
    /// prove the backups folder is genuinely writable.
    /// <para>
    /// Named here rather than inline at the one place that writes it, because a second place now has to
    /// recognise it: the folder is enumerated as a list of backups, and a probe left behind by a failed
    /// delete — or by a process that stopped between the write and the delete — is not one. Two string
    /// literals for the same file would eventually disagree, and the failure would be a scratch file
    /// offered to an operator as a restore point.
    /// </para>
    /// </summary>
    public const string ProbeFileName = ".writable-probe";

    /// <summary>
    /// Whether a file in the backups folder is a backup, rather than an artefact this application
    /// writes there for its own purposes.
    /// <para>
    /// Applied everywhere a file is treated as a backup — listed, opened, or removed — so the answer is
    /// the same whichever route asks. A probe left behind by a failed delete is consistently "not
    /// found" rather than listable through one endpoint and missing from another.
    /// </para>
    /// <para>
    /// Deliberately not applied to the storage total: the probe is zero bytes, and the quota is a claim
    /// about what the folder occupies on disk, which includes anything in it.
    /// </para>
    /// </summary>
    /// <param name="name">The file name to classify.</param>
    public static bool IsBackup(string name) =>
        !string.Equals(name, ProbeFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves <paramref name="name"/> to a full path inside <paramref name="backupsPath"/>, or
    /// rejects it.
    /// </summary>
    /// <param name="backupsPath">The backups folder every backup must resolve inside.</param>
    /// <param name="name">The caller-supplied identifier — a file name, never a path.</param>
    /// <param name="fullPath">The resolved absolute path, when the name is accepted.</param>
    /// <returns><see langword="true"/> when the name is safe to act on.</returns>
    public static bool TryResolve(string backupsPath, string? name, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(backupsPath))
            return false;

        // A file name is a name, not a path. Anything that survives GetFileName unchanged carries no
        // separator, no drive, and no parent segment — which rejects "..", "a/b", "/etc/passwd" and
        // "C:\x" in one comparison rather than a list of patterns to keep current.
        if (!string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal))
            return false;

        if (name is "." or "..")
            return false;

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        string root     = Path.GetFullPath(backupsPath);
        string resolved = Path.GetFullPath(Path.Combine(root, name));

        // The second half: whatever the name looked like, the place it actually points at must be
        // inside the backups folder. Compared with the separator appended so a sibling folder whose
        // name merely starts with the same characters cannot pass.
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!resolved.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = resolved;
        return true;
    }
}
