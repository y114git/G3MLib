using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace G3MLib.Modding.Patching;

public class ResourceChange
{
	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("files")]
	public Dictionary<string, string>? Files { get; set; }
}
