using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace G3MLib.Modding.Patching;

public class G3MPatchManifest
{
	[JsonPropertyName("createdAt")]
	public string? CreatedAt { get; set; }

	[JsonPropertyName("tool")]
	public ToolInfo? Tool { get; set; }

	[JsonPropertyName("library")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ToolInfo? Library { get; set; }

	[JsonPropertyName("original")]
	public DataFileInfo? Original { get; set; }

	[JsonPropertyName("modified")]
	public DataFileInfo? Modified { get; set; }

	[JsonPropertyName("resources")]
	public Dictionary<string, ResourceTypeChanges>? Resources { get; set; }

	[JsonPropertyName("statistics")]
	public PatchStatistics? Statistics { get; set; }

	[JsonPropertyName("applyPlan")]
	public PatchApplyPlan? ApplyPlan { get; set; }
}
