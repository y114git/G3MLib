using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace G3MLib.DataFile.Interop;

/// <summary>
/// GameMaker project file layout to write.
/// </summary>
public enum YyFormat
{
    /// <summary>LTS 2022 (2.3 format): discriminators are absent, resource versions are 1.x.</summary>
    Lts2022,

    /// <summary>LTS 2026 format: <c>$GMType</c> and <c>%Name</c> keys, resource version 2.0.</summary>
    Lts2026,
}

/// <summary>
/// Reading and writing helpers shared by the <c>.yy</c> converters.
/// </summary>
public static class YyFile
{
    private static readonly JsonDocumentOptions ReadOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Parses a <c>.yy</c> or <c>.yyp</c> file. GameMaker writes trailing commas, which are accepted.
    /// </summary>
    public static JsonElement Parse(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream, ReadOptions);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Writes <paramref name="root"/> as indented JSON, creating the folder when needed.
    /// </summary>
    public static void Write(string path, JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, root.ToJsonString(WriteOptions));
    }

    internal static int Int(this JsonElement e, string name, int fallback = 0) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)v.GetDouble() : fallback;

    internal static uint Uint(this JsonElement e, string name, uint fallback = 0) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (uint)v.GetDouble() : fallback;

    internal static float Float(this JsonElement e, string name, float fallback = 0) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    internal static bool Bool(this JsonElement e, string name, bool fallback = false) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    internal static string Str(this JsonElement e, string name, string? fallback = null) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : fallback;

    /// <summary>
    /// Returns the child object or array, or an undefined element when absent or null.
    /// </summary>
    internal static JsonElement Child(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? v : default;

    /// <summary>
    /// Items of an array child; nothing when absent.
    /// </summary>
    internal static JsonElement.ArrayEnumerator Items(this JsonElement e, string name) =>
        e.Child(name) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : default;

    /// <summary>
    /// A stable GUID for a generated sub-item (frame, layer), so writing twice gives identical files.
    /// </summary>
    internal static string StableGuid(string seed) => new Guid(MD5.HashData(Encoding.UTF8.GetBytes(seed))).ToString();

    /// <summary>
    /// Starts a resource object with the keys every <c>.yy</c> file has.
    /// </summary>
    internal static JsonObject Resource(YyFormat format, string type, string name, string version2022, string discriminator)
    {
        JsonObject o = [];
        if (format == YyFormat.Lts2026)
        {
            o[$"${type}"] = discriminator;
            o["%Name"] = name;
        }
        o["resourceType"] = type;
        o["resourceVersion"] = format == YyFormat.Lts2026 ? "2.0" : version2022;
        o["name"] = name;
        return o;
    }

    internal static JsonObject Ref(string kind, string name) => new() { ["name"] = name, ["path"] = $"{kind}/{name}/{name}.yy" };
}
