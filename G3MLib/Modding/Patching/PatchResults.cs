namespace G3MLib.Modding.Patching;

public sealed class PatchApplyResult
{
	public bool Success { get; set; }
	public string? Error { get; set; }
	public int AppliedCount { get; set; }
	public int FailedCount { get; set; }
	public IReadOnlyList<string> Warnings { get; set; } = [];
}

public sealed class PatchApplyOptions
{
	/// <summary>When <see langword="true"/>, validates the patch archive (every JSON file parses, the manifest exists, and code entries come with their code metadata) and prepares required helpers before mutating the target <see cref="G3MLib.DataFile.GameMakerData"/>.</summary>
	public bool ValidateBeforeApply { get; set; } = true;

	/// <summary>Path of the data file on disk. An in-memory apply writes no files, so audio groups stored outside the data file are skipped whatever this holds.</summary>
	public string? DataFilePath { get; set; }
}

public sealed class PatchCreateResult
{
	public bool Success { get; set; }
	public string? Error { get; set; }
	public string? Message { get; set; }
	public PatchStatistics? Statistics { get; set; }
}

public sealed class PatchValidateResult
{
	public bool Success { get; set; }
	public string? Error { get; set; }
	public G3MPatchManifest? Manifest { get; set; }
}
