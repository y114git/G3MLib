using System.Text.Json.Serialization;

namespace G3MLib.Modding.Diffing;

public enum DiffReportMode
{
	Standard,
	Full
}

public sealed class DiffResourceTypeChanges
{
	public List<string> Changed { get; } = [];
	public List<string> New { get; } = [];
	public List<string> Deleted { get; } = [];
}

public sealed class DiffTypeSummary
{
	[JsonPropertyName("changed")]
	public int Changed { get; set; }
	[JsonPropertyName("new")]
	public int New { get; set; }
	[JsonPropertyName("deleted")]
	public int Deleted { get; set; }
}

public sealed class TextFileDiff
{
	public string ResourceType { get; set; } = "";
	public string ResourceName { get; set; } = "";
	public string FileName { get; set; } = "";
	public string UnifiedDiff { get; set; } = "";
}

public sealed class ResourceDiffSummary
{
	public Dictionary<string, DiffResourceTypeChanges> ByType { get; } = [];
	public List<TextFileDiff> TextDiffs { get; } = [];
	public int TotalChanged { get; set; }
	public int TotalNew { get; set; }
	public int TotalDeleted { get; set; }
}

public sealed class DiffResult
{
	public bool Success { get; set; }
	public string? Error { get; set; }
	public int DifferenceCount { get; set; }
	public string? OutputPath { get; set; }
	public string? Mode { get; set; }
	public int? TotalChanged { get; set; }
	public int? TotalNew { get; set; }
	public int? TotalDeleted { get; set; }
	public int? TextDiffCount { get; set; }
	public Dictionary<string, DiffTypeSummary>? ByType { get; set; }
}
