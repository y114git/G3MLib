using System;
using System.Text.Json.Serialization;

namespace G3MLib.Modding.Caching;

public sealed class G3MCacheManifest
{
	public const int CurrentSchema = 3;

	[JsonPropertyName("schema")]
	public int Schema { get; set; } = CurrentSchema;

	[JsonPropertyName("createdAt")]
	public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

	[JsonPropertyName("tool")]
	public ToolInfo Tool { get; set; } = new ToolInfo();

	[JsonPropertyName("sourceKind")]
	public string SourceKind { get; set; } = "data";

	[JsonPropertyName("sourceFile")]
	public string SourceFile { get; set; } = "";

	[JsonPropertyName("sourceSize")]
	public long SourceSize { get; set; }

	[JsonPropertyName("sourceLastWriteUtcTicks")]
	public long SourceLastWriteUtcTicks { get; set; }

	[JsonPropertyName("sourceMd5")]
	public string? SourceMd5 { get; set; }
}
