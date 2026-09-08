using System.Text.Json.Serialization;

namespace G3MLib.Modding.Patching;

public class ToolInfo
{
	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("version")]
	public string? Version { get; set; }
}
