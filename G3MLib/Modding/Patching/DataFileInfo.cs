using System.Text.Json.Serialization;

namespace G3MLib.Modding.Patching;

public class DataFileInfo
{
	[JsonPropertyName("filename")]
	public string? Filename { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("md5")]
	public string? Md5 { get; set; }

	[JsonPropertyName("bytecodeVersion")]
	public int BytecodeVersion { get; set; }

	[JsonPropertyName("gmsVersion")]
	public string? GmsVersion { get; set; }

	[JsonPropertyName("generalInfo")]
	public GeneralInfoData? GeneralInfo { get; set; }
}
