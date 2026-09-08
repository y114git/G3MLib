using System;
using System.IO;
using System.Reflection;

namespace G3MLib.DataFile.Util;

/// <summary>
/// Provides embedded source-control version information when it is available.
/// </summary>
public static class GitVersion
{
    private const string UnknownVersion = "unknownGitCommit";

    /// <summary>
    /// Gets the embedded commit and branch information.
    /// </summary>
    public static string GetGitVersion()
    {
        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.EndsWith(".gitversion.txt", StringComparison.Ordinal))
                    continue;

                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                using StreamReader reader = new(stream);
                string[] lines = reader.ReadToEnd().Trim().Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                return lines.Length >= 2 ? $"{lines[0]} ({lines[1]})" : lines.Length == 1 ? lines[0] : UnknownVersion;
            }
        }
        catch
        {
        }

        return UnknownVersion;
    }
}
