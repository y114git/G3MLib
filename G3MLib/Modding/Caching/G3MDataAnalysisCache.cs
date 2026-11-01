using System.Text.Json.Serialization;

namespace G3MLib.Modding.Caching;

public sealed class G3MDataAnalysisCache
{
	[JsonPropertyName("dataInfo")]
	public DataFileInfo DataInfo { get; set; } = new DataFileInfo();

	[JsonPropertyName("resourceHashes")]
	public Dictionary<string, Dictionary<string, string>> ResourceHashes { get; set; } = [];

	[JsonPropertyName("resourceNameCounts")]
	public Dictionary<string, Dictionary<string, int>> ResourceNameCounts { get; set; } = [];

	[JsonPropertyName("orderSensitiveNames")]
	public Dictionary<string, List<string>> OrderSensitiveNames { get; set; } = [];

	[JsonPropertyName("infoSnapshot")]
	public G3MDataInfoSnapshot? InfoSnapshot { get; set; }
}
