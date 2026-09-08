using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace G3MLib.Modding.Caching;

public sealed class G3MDataInfoSnapshot
{
	[JsonPropertyName("file")]
	public string File { get; set; } = "";

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("game")]
	public string Game { get; set; } = "Unknown";

	[JsonPropertyName("bytecodeVersion")]
	public int BytecodeVersion { get; set; }

	[JsonPropertyName("version")]
	public string? Version { get; set; }

	[JsonPropertyName("generalInfo")]
	public GeneralInfoData? GeneralInfo { get; set; }

	[JsonPropertyName("resourceCounts")]
	public Dictionary<string, int> ResourceCounts { get; set; } = new Dictionary<string, int>();

	[JsonPropertyName("variablesByInstanceType")]
	public Dictionary<string, int> VariablesByInstanceType { get; set; } = new Dictionary<string, int>();

	[JsonPropertyName("firstFunction")]
	public string? FirstFunction { get; set; }

	[JsonPropertyName("lastFunction")]
	public string? LastFunction { get; set; }

	[JsonPropertyName("topLevelCodeCount")]
	public int TopLevelCodeCount { get; set; }

	[JsonPropertyName("childCodeCount")]
	public int ChildCodeCount { get; set; }

	[JsonPropertyName("audioGroups")]
	public List<string> AudioGroups { get; set; } = new List<string>();

	[JsonPropertyName("extensions")]
	public List<string> Extensions { get; set; } = new List<string>();

	[JsonPropertyName("roomOrderPreview")]
	public List<string> RoomOrderPreview { get; set; } = new List<string>();

	[JsonPropertyName("roomOrderCount")]
	public int RoomOrderCount { get; set; }
}
