using System.Text.Json.Serialization;

namespace G3MLib.Modding.Patching;

public class GeneralInfoData
{
	[JsonPropertyName("displayName")]
	public string? DisplayName { get; set; }

	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("fileName")]
	public string? FileName { get; set; }

	[JsonPropertyName("config")]
	public string? Config { get; set; }

	[JsonPropertyName("gameID")]
	public uint GameID { get; set; }

	[JsonPropertyName("directPlayGuid")]
	public string? DirectPlayGuid { get; set; }

	[JsonPropertyName("major")]
	public uint Major { get; set; }

	[JsonPropertyName("minor")]
	public uint Minor { get; set; }

	[JsonPropertyName("release")]
	public uint Release { get; set; }

	[JsonPropertyName("build")]
	public uint Build { get; set; }

	[JsonPropertyName("defaultWindowWidth")]
	public uint DefaultWindowWidth { get; set; }

	[JsonPropertyName("defaultWindowHeight")]
	public uint DefaultWindowHeight { get; set; }

	[JsonPropertyName("infoFlags")]
	public uint InfoFlags { get; set; }

	[JsonPropertyName("licenseCRC32")]
	public int LicenseCRC32 { get; set; }

	[JsonPropertyName("timestamp")]
	public ulong Timestamp { get; set; }

	[JsonPropertyName("activeTargets")]
	public ulong ActiveTargets { get; set; }

	[JsonPropertyName("functionClassifications")]
	public ulong FunctionClassifications { get; set; }

	[JsonPropertyName("steamAppID")]
	public int SteamAppID { get; set; }

	[JsonPropertyName("debuggerPort")]
	public int DebuggerPort { get; set; }

	[JsonPropertyName("gms2FPS")]
	public float GMS2FPS { get; set; }

	[JsonPropertyName("gms2AllowStatistics")]
	public bool GMS2AllowStatistics { get; set; }

	[JsonPropertyName("roomOrderCount")]
	public int RoomOrderCount { get; set; }
}
