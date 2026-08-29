namespace Quotinator.Data.Database;

/// <summary>
/// Removes a backup file (#349).
/// <para>
/// Separate from <see cref="IDatabaseBackupReader"/> so the destructive capability is injected only
/// where it is actually used, matching the reader/writer split every other pair in this project
/// follows. Deletion is the only operation here: taking a backup belongs to
/// <see cref="IDatabaseInitializer"/>, which already owns the connection and the version it is taken
/// at, and restoring one belongs to #352.
/// </para>
/// </summary>
public interface IDatabaseBackupWriter
{
    /// <summary>
    /// Deletes one backup file.
    /// </summary>
    /// <param name="name">The file name, as <see cref="IDatabaseBackupReader.List"/> reports it.</param>
    /// <returns>
    /// <see langword="true"/> when a file was removed; <see langword="false"/> when the name is unsafe
    /// or no such file exists. The caller distinguishes those two before calling, so that a "removed"
    /// and a "was never there" reach the operator as different answers.
    /// </returns>
    bool Delete(string name);
}
