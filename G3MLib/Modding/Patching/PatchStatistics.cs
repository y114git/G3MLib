using System.Text.Json.Serialization;

namespace G3MLib.Modding.Patching;

public class PatchStatistics
{
	[JsonPropertyName("totalChanged")]
	public int TotalChanged { get; set; }

	[JsonPropertyName("totalNew")]
	public int TotalNew { get; set; }

	[JsonPropertyName("totalDeleted")]
	public int TotalDeleted { get; set; }

	[JsonPropertyName("totalChangedFiles")]
	public int TotalChangedFiles { get; set; }

	[JsonPropertyName("totalNewFiles")]
	public int TotalNewFiles { get; set; }
}
