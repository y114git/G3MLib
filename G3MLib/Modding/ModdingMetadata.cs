using System.Reflection;

namespace G3MLib.Modding;

public static class ModdingMetadata
{
    public static string ToolName { get; set; } = "G3MLib";

    public static string LibraryVersion { get; } = typeof(ModdingMetadata).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(ModdingMetadata).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public static string ToolVersion { get; set; } = LibraryVersion;
}
