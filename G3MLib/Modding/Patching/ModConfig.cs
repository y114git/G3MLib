using System.IO;
using System.Text;
using System.Text.Json;

namespace G3MLib.Modding.Patching;

/// <summary>One patch operation flattened from a G3M <c>mod_config.json</c> manifest.</summary>
public sealed record ModPatchOperation(string Source, string Target, string Type = "patch", string? Group = null, PatchApplyOptions? Options = null)
{
	/// <summary>The file name of the data file targeted by this operation (the last segment of <see cref="Target"/>).</summary>
	public string TargetFileName => Path.GetFileName(Target.Replace('\\', '/').TrimEnd('/'));

	/// <summary>Alias for <see cref="TargetFileName"/> matching <c>ModImport.ModPatch.TargetFile</c>.</summary>
	public string TargetFile => TargetFileName;

	/// <summary>The archive member of the payload when <see cref="Source"/> lives inside the mod (<c>${mod_path}/name</c>); otherwise <see langword="null"/>.</summary>
	public string? Member => GetValidMember(Source);

	/// <summary>The patch payload path relative to the mod package (or <see cref="Source"/> when not prefixed with <c>${mod_path}/</c>).</summary>
	public string PatchFile => Member ?? Source;

	/// <summary>Whether this operation is a <c>patch</c> operation with an in-package <c>.g3mpatch</c> payload.</summary>
	public bool Supported => Type == "patch" && Member is not null && Path.GetExtension(Member).Equals(".g3mpatch", StringComparison.OrdinalIgnoreCase);

	private static string? GetValidMember(string source)
	{
		if (!source.StartsWith("${mod_path}/", StringComparison.Ordinal))
			return null;
		var path = source["${mod_path}/".Length..];
		if (string.IsNullOrEmpty(path))
			return null;
		if (path.Contains('\\'))
			return null;
		if (Path.IsPathRooted(path))
			return null;
		if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
			return null;
		var segments = path.Split('/');
		foreach (var segment in segments)
		{
			if (segment == "..")
				return null;
		}
		return path;
	}
}

/// <summary>Minimal reader for a G3M <c>mod_config.json</c> manifest (version 2.0.0) that flattens operation groups and extracts <c>patch</c> operations.</summary>
public sealed class ModConfig
{
	public const long MaxConfigBytes = 4 * 1024 * 1024;
	public const long MaxPayloadBytes = 4L * 1024 * 1024 * 1024;
	public const int MaxJsonDepth = 64;
	public const int MaxGroupDepth = 32;

	public string Name { get; }
	public string Version { get; }
	public string Game { get; }
	public IReadOnlyList<ModPatchOperation> Patches { get; }
	public IReadOnlyList<ModPatchOperation> Operations => Patches;

	public ModConfig(string name, string version, string game, IReadOnlyList<ModPatchOperation> patches)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(version);
		ArgumentNullException.ThrowIfNull(game);
		ArgumentNullException.ThrowIfNull(patches);
		Name = name;
		Version = version;
		Game = game;
		Patches = patches;
	}

	/// <summary>Parses a G3M <c>mod_config.json</c> string, enforcing the 4 MiB size cap, 64 JSON depth cap and 32 group recursion depth cap.</summary>
	public static ModConfig Parse(string json)
	{
		ArgumentNullException.ThrowIfNull(json);
		if (Encoding.UTF8.GetByteCount(json) > MaxConfigBytes)
			throw new InvalidDataException("mod_config.json is larger than 4 MiB.");

		using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
		return ParseCore(document.RootElement);
	}

	/// <summary>Parses a G3M <c>mod_config.json</c> UTF-8 stream, enforcing the 4 MiB size cap, 64 JSON depth cap and 32 group recursion depth cap.</summary>
	public static ModConfig Parse(Stream utf8Json)
	{
		ArgumentNullException.ThrowIfNull(utf8Json);
		if (utf8Json.CanSeek && utf8Json.Length - utf8Json.Position > MaxConfigBytes)
			throw new InvalidDataException("mod_config.json is larger than 4 MiB.");

		using var buffer = new MemoryStream();
		var chunk = new byte[8192];
		long totalRead = 0;
		int read;
		while ((read = utf8Json.Read(chunk, 0, chunk.Length)) > 0)
		{
			totalRead += read;
			if (totalRead > MaxConfigBytes)
				throw new InvalidDataException("mod_config.json is larger than 4 MiB.");
			buffer.Write(chunk, 0, read);
		}

		buffer.Position = 0;
		using var document = JsonDocument.Parse(buffer, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
		return ParseCore(document.RootElement);
	}

	/// <summary>Returns the <c>patch</c> operations targeting a data file with the given file name (case-insensitive), in manifest order.</summary>
	public IReadOnlyList<ModPatchOperation> ForDataFile(string dataFilePath)
	{
		ArgumentNullException.ThrowIfNull(dataFilePath);
		var fileName = Path.GetFileName(dataFilePath.Replace('\\', '/').TrimEnd('/'));
		return [.. Patches.Where(p => p.TargetFileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))];
	}

	/// <summary>Returns the <c>patch</c> operations of <paramref name="config"/> targeting a data file with the given file name (case-insensitive), in manifest order.</summary>
	public static IReadOnlyList<ModPatchOperation> ForDataFile(ModConfig config, string dataFilePath)
	{
		ArgumentNullException.ThrowIfNull(config);
		return config.ForDataFile(dataFilePath);
	}

	private static ModConfig ParseCore(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object)
			throw new InvalidDataException("mod_config.json is not an object.");

		string Text(string name) =>
			root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
				? value.GetString() ?? ""
				: "";

		var patches = new List<ModPatchOperation>();
		if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
			Collect(files, patches, depth: 0, groupName: null);

		return new ModConfig(Text("name"), Text("version"), Text("game"), patches);
	}

	internal static void Collect(JsonElement array, List<ModPatchOperation> into, int depth, string? groupName)
	{
		if (depth > MaxGroupDepth)
			throw new InvalidDataException($"Group recursion depth exceeded maximum of {MaxGroupDepth}.");

		foreach (var item in array.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
				continue;

			if (item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
			{
				if (type.GetString() == "patch" &&
					item.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String &&
					item.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String)
				{
					var options = ParseOptions(item);
					into.Add(new ModPatchOperation(source.GetString() ?? "", target.GetString() ?? "", "patch", groupName, options));
				}
				continue;
			}

			foreach (var group in item.EnumerateObject())
			{
				if (group.Value.ValueKind == JsonValueKind.Array)
					Collect(group.Value, into, depth + 1, group.Name);
			}
		}
	}

	private static PatchApplyOptions? ParseOptions(JsonElement operationElement)
	{
		if (!operationElement.TryGetProperty("options", out var optionsElement) || optionsElement.ValueKind != JsonValueKind.Object)
			return null;

		var options = new PatchApplyOptions();
		if (optionsElement.TryGetProperty("validateBeforeApply", out var validateElement) &&
			validateElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
		{
			options.ValidateBeforeApply = validateElement.GetBoolean();
		}

		if (optionsElement.TryGetProperty("dataFilePath", out var dataFilePathElement) &&
			dataFilePathElement.ValueKind == JsonValueKind.String)
		{
			options.DataFilePath = dataFilePathElement.GetString();
		}

		return options;
	}
}
