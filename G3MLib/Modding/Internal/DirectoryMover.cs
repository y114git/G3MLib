using System;
using System.IO;
using System.Threading;

namespace G3MLib.Modding.Internal;

/// <summary>Moves and deletes folders that a virus scanner or a sync tool may hold for a moment after files were written into them.</summary>
internal static class DirectoryMover
{
    private const int Attempts = 8;

    // Win32 codes inside an IOException HResult: access denied, sharing violation, lock violation, directory not empty.
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorDirNotEmpty = 145;

    /// <summary>Moves a folder. Returns false when the folder had to be copied and the source could not be removed afterwards; the target is complete then, and the caller may remove the source later.</summary>
    public static bool Move(string source, string target)
    {
        source = Path.GetFullPath(source);
        target = Path.GetFullPath(target);
        if (string.Equals(source, target, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || IsInside(source, target))
        {
            throw new IOException($"Cannot move \"{source}\" onto or into itself.");
        }
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(source, target);
                return true;
            }
            // A target that exists or a source that is gone is a real error, not a scanner holding the folder: it is thrown at once.
            catch (Exception e) when (IsTransient(e) && Directory.Exists(source) && !Directory.Exists(target))
            {
                if (attempt == Attempts)
                {
                    return CopyThenDelete(source, target);
                }
                Thread.Sleep(100 * attempt);
            }
        }
    }

    public static void Delete(string folder)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(folder, true);
                return;
            }
            catch (Exception e) when (IsTransient(e))
            {
                if (attempt == Attempts)
                {
                    throw;
                }
                Thread.Sleep(100 * attempt);
            }
        }
    }

    // Only Windows holds a folder busy for a moment (scanners, sync tools) and reports it with these codes; elsewhere a permission error stays one.
    // Other IO errors (path too long, disk full, a missing folder) do not pass with time either, so they are not retried.
    private static bool IsTransient(Exception e)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }
        if (e is UnauthorizedAccessException)
        {
            return true;
        }
        return e is IOException io && (io.HResult & 0xFFFF) is ErrorAccessDenied or ErrorSharingViolation or ErrorLockViolation or ErrorDirNotEmpty;
    }

    private static bool IsInside(string parent, string child)
    {
        var relative = Path.GetRelativePath(parent, child);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    // A folder that cannot be renamed while something reads inside it can still be copied; what is left of the source is removed by the caller or the next export.
    private static bool CopyThenDelete(string source, string target)
    {
        // The copy is built beside the target and renamed into place, so the target never shows a half copy.
        var staging = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".copy-" + Guid.NewGuid().ToString("N");
        if (IsLink(new DirectoryInfo(source)))
        {
            throw new IOException($"\"{source}\" is a link and is not copied.");
        }
        Directory.CreateDirectory(staging);
        try
        {
            CopyTree(new DirectoryInfo(source), staging);
            Directory.Move(staging, target);
        }
        catch
        {
            RemoveStaging(staging); // only the folder created above, never the source or the target
            throw;
        }
        try
        {
            Delete(source);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false; // the copy is complete; what is left of the source is the caller's to remove
        }
    }

    private static bool IsLink(FileSystemInfo entry) => entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

    // A link is never followed or copied: a copy through it could read or write outside the folder, so it fails the copy instead.
    private static void CopyTree(DirectoryInfo source, string target)
    {
        foreach (var entry in source.EnumerateFileSystemInfos())
        {
            if (IsLink(entry))
            {
                throw new IOException($"\"{entry.FullName}\" is a link and is not copied.");
            }
            var destination = Path.Join(target, entry.Name);
            if (entry is DirectoryInfo directory)
            {
                Directory.CreateDirectory(destination);
                CopyTree(directory, destination);
            }
            else
            {
                File.Copy(entry.FullName, destination, false);
            }
        }
    }

    private static void RemoveStaging(string staging)
    {
        try
        {
            Delete(staging);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the original error is the one to report; a leftover staging folder is named after the target and can be deleted by hand
        }
    }
}
