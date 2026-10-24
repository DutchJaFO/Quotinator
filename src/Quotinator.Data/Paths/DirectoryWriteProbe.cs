namespace Quotinator.Data.Paths;

/// <summary>
/// Answers whether a file can actually be created in a directory, by creating one.
/// </summary>
/// <remarks>
/// <c>Directory.CreateDirectory</c> proves nothing when the directory already exists: it is a no-op then,
/// and returns happily on a read-only mount. Writing a zero-byte probe and deleting it is the only way to
/// answer the question. Found twice: by #348 for the backups folder, where a reset against a read-only data
/// directory cleared a <c>CreateDirectory</c> check and failed inside the table drop, and by #327 for the
/// DataProtection <c>keys/</c> folder, where a read-only <c>keys/</c> left startup reporting healthy while
/// every page failed on the key ring.
/// </remarks>
public static class DirectoryWriteProbe
{
    /// <summary>
    /// The probe's file name. A leading dot and no extension keep it out of every enumeration that reads
    /// the folders it is written to: backups are <c>*.db</c>, DataProtection keys are <c>*.xml</c>.
    /// </summary>
    public const string ProbeFileName = ".writable-probe";

    /// <summary>True when a file could be created in, and removed from, <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory to test; it is not created.</param>
    public static bool CanCreateFileIn(string directory)
    {
        string probePath = Path.Combine(directory, ProbeFileName);
        try
        {
            File.WriteAllBytes(probePath, []);
            File.Delete(probePath);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
