using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace G3MLib.Modding.Patching;

public class ResourceTypeChanges
{
	[JsonPropertyName("changed")]
	public List<ResourceChange>? Changed { get; set; }

	[JsonPropertyName("new")]
	public List<ResourceChange>? New { get; set; }

	[JsonPropertyName("deleted")]
	public List<string>? Deleted { get; set; }

	[JsonIgnore]
	public bool HasChanges => (Changed?.Count ?? 0) + (New?.Count ?? 0) + (Deleted?.Count ?? 0) > 0;
}
