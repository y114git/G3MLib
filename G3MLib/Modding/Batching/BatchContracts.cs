using System;
using System.Collections.Generic;
using G3MLib.Modding.Caching;

namespace G3MLib.Modding.Batching;

public sealed class BatchOptions
{
	public required string OriginalPath { get; init; }
	public string? OutDir { get; init; }
	public string? ApplyDir { get; init; }
	public G3MCacheOptions? CacheOptions { get; init; }
	public bool ContinueOnError { get; init; }
	public bool XdeltaFallback { get; init; }
	public bool IncludeXdeltaFallback { get; init; }
	public bool CreateXdelta { get; init; }
	public bool UseCodeMerge { get; init; }
	public bool UsePropertyMerge { get; init; }
	public bool WriteReports { get; init; }
}

public sealed class BatchItemResult
{
	public int Index { get; init; }
	public required string Kind { get; init; }
	public required string Key { get; init; }
	public required string[] Inputs { get; init; }
	public string[] Outputs { get; init; } = Array.Empty<string>();
	public bool Success { get; init; }
	public bool Deduplicated { get; init; }
	public string? Error { get; init; }
	public double Seconds { get; init; }
}

public sealed class BatchResult
{
	public bool Success => Failed == 0;
	public int Total { get; set; }
	public int Completed { get; set; }
	public int Failed { get; set; }
	public int Deduplicated { get; set; }
	public List<BatchItemResult> Items { get; } = new List<BatchItemResult>();
}
