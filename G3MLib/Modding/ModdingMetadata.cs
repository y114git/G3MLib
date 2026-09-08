namespace G3MLib.Modding;

public static class ModdingMetadata
{
    public static string ToolName { get; set; } = "G3MLib";

    public static string ToolVersion { get; set; } = typeof(ModdingMetadata).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
