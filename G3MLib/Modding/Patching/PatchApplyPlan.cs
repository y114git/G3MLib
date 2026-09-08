using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace G3MLib.Modding.Patching;

public class PatchApplyPlan
{
	[JsonPropertyName("mode")]
	public string? Mode { get; set; }

	[JsonPropertyName("requiresCodePipeline")]
	public bool RequiresCodePipeline { get; set; }

	[JsonPropertyName("requiresTexturePipeline")]
	public bool RequiresTexturePipeline { get; set; }

	[JsonPropertyName("requiresAssetReorder")]
	public bool RequiresAssetReorder { get; set; }

	[JsonPropertyName("requiresHeavyFinalize")]
	public bool RequiresHeavyFinalize { get; set; }

	[JsonPropertyName("supportsDirectResourceApply")]
	public bool SupportsDirectResourceApply { get; set; }

	[JsonPropertyName("simpleResourceTypes")]
	public List<string>? SimpleResourceTypes { get; set; }

	[JsonPropertyName("heavyResourceTypes")]
	public List<string>? HeavyResourceTypes { get; set; }
}
