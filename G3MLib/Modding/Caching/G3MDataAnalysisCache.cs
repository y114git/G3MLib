using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace G3MLib.Modding.Caching;

public sealed class G3MDataAnalysisCache
{
	[JsonPropertyName("dataInfo")]
	public DataFileInfo DataInfo { get; set; } = new DataFileInfo();

	[JsonPropertyName("resourceHashes")]
	public Dictionary<string, Dictionary<string, string>> ResourceHashes { get; set; } = new Dictionary<string, Dictionary<string, string>>();

	[JsonPropertyName("resourceNameCounts")]
	public Dictionary<string, Dictionary<string, int>> ResourceNameCounts { get; set; } = new Dictionary<string, Dictionary<string, int>>();

	[JsonPropertyName("orderSensitiveNames")]
	public Dictionary<string, List<string>> OrderSensitiveNames { get; set; } = new Dictionary<string, List<string>>();

	[JsonPropertyName("infoSnapshot")]
	public G3MDataInfoSnapshot? InfoSnapshot { get; set; }
}
