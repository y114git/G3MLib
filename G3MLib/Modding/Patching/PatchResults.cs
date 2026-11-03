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
	/// <summary>When <see langword="true"/>, validates the patch archive and prepares required helpers before mutating the target <see cref="G3MLib.DataFile.GameMakerData"/>.</summary>
	public bool ValidateBeforeApply { get; set; } = true;

	/// <summary>Optional path to the source data file on disk when external audio group files should be resolved relative to its directory.</summary>
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
