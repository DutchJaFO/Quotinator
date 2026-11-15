using System.Runtime.InteropServices;

namespace Quotinator.Data.Database;

/// <summary>
/// Real <see cref="IDiskSpaceProvider"/>: the free space on the file system that holds a path, available to this
/// process (#490).
/// <para>
/// <see cref="DriveInfo"/> on the path's root is not that on Linux: the root of <c>/data/...</c> is <c>/</c>, so it
/// measured the container's root file system rather than the volume mounted at <c>/data</c>. Found in #490's T2,
/// where a full tmpfs volume read as about a terabyte free. Nothing in .NET's documentation maps an arbitrary path
/// to its mount on Linux, so each platform's own documented call is used:
/// </para>
/// <list type="bullet">
/// <item>
/// Linux: <c>statvfs</c>, which "shall obtain information about the file system containing the file named by
/// path" (POSIX, pubs.opengroup.org/onlinepubs/9799919799/functions/statvfs.html). <c>f_bavail</c> is the free
/// blocks available to a non-privileged process; POSIX does not say whether in <c>f_frsize</c> or <c>f_bsize</c>
/// units, so the smaller is used, which can only under-estimate the room. The struct layout read here, five
/// 8-byte fields first (<c>f_bsize</c>, <c>f_frsize</c>, <c>f_blocks</c>, <c>f_bfree</c>, <c>f_bavail</c>), is the
/// 64-bit Linux ABI the man page shows "approximately" (man7.org/linux/man-pages/man3/statvfs.3.html); the
/// image targets only 64-bit Linux.
/// </item>
/// <item>
/// Windows: <c>GetDiskFreeSpaceExW</c>, which "accepts any directory on a disk" and reports the free bytes
/// "available to the user who is associated with the calling thread"
/// (learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getdiskfreespaceexw).
/// </item>
/// <item>Anywhere else, or when either call fails: <see cref="DriveInfo"/> on the path's root, as before.</item>
/// </list>
/// </summary>
public sealed class DiskSpaceProvider : IDiskSpaceProvider
{
    // Comfortably larger than any 64-bit struct statvfs; only the first 40 bytes are read.
    private const int StatvfsBufferBytes = 256;

    /// <inheritdoc/>
    public long GetAvailableFreeSpaceBytes(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return (OperatingSystem.IsLinux() ? FromStatvfs(fullPath)
                : OperatingSystem.IsWindows() ? FromGetDiskFreeSpaceEx(fullPath)
                : null)
            ?? new DriveInfo(Path.GetPathRoot(fullPath) ?? fullPath).AvailableFreeSpace;
    }

    private static long? FromStatvfs(string path)
    {
        byte[] buffer = new byte[StatvfsBufferBytes];
        if (Statvfs(ExistingAncestor(path), buffer) != 0)
            return null;

        ulong blockSize    = BitConverter.ToUInt64(buffer, 0);
        ulong fragmentSize = BitConverter.ToUInt64(buffer, 8);
        ulong available    = BitConverter.ToUInt64(buffer, 32);
        ulong unit         = Math.Min(blockSize, fragmentSize);
        return unit == 0 ? null : (long)Math.Min(available * unit, long.MaxValue);
    }

    private static long? FromGetDiskFreeSpaceEx(string path) =>
        GetDiskFreeSpaceEx(ExistingAncestor(path), out ulong freeToCaller, out _, out _)
            ? (long)Math.Min(freeToCaller, long.MaxValue)
            : null;

    // Both calls need a path that exists; a backups folder or a work folder may not exist yet, and the room it
    // will have is the room of the file system holding its nearest existing parent.
    private static string ExistingAncestor(string path)
    {
        string? current = path;
        while (current is not null && !Directory.Exists(current) && !File.Exists(current))
            current = Path.GetDirectoryName(current);
        return current ?? path;
    }

    // DllImport rather than LibraryImport: the source-generated form needs unsafe code enabled for the whole
    // project, and these two signatures marshal without it.
    [DllImport("libc", EntryPoint = "statvfs")]
    private static extern int Statvfs([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] buffer);

    [DllImport("kernel32", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong freeBytesAvailableToCaller, out ulong totalBytes, out ulong totalFreeBytes);
}
