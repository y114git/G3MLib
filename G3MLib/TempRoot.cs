namespace G3MLib;

/// <summary>Folder for the library's temporary files; a test can redirect it for the current async flow only.</summary>
internal static class TempRoot
{
    private static readonly AsyncLocal<string?> Override = new();

    public static string Path => Override.Value ?? System.IO.Path.GetTempPath();

    public static void Redirect(string? folder) => Override.Value = folder;
}
