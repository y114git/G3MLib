using G3MLib.Modding.Patching;

namespace G3MLib.Modding.Patching;

public sealed class PatchApplyResult
{
	public bool Success { get; set; }
	public string? Error { get; set; }
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
