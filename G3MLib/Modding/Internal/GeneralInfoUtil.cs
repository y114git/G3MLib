using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Modding.Internal;

public static class GeneralInfoUtil
{
	public static GeneralInfoData? ExtractGeneralInfo(GameMakerData data)
	{
		ArgumentNullException.ThrowIfNull(data);
		var gi = data.GeneralInfo;
		if (gi == null)
		{
			return null;
		}
		return new GeneralInfoData
		{
			DisplayName = gi.DisplayName?.Content,
			Name = gi.Name?.Content,
			FileName = gi.FileName?.Content,
			Config = gi.Config?.Content,
			GameID = gi.GameID,
			DirectPlayGuid = gi.DirectPlayGuid.ToString(),
			Major = gi.Major,
			Minor = gi.Minor,
			Release = gi.Release,
			Build = gi.Build,
			DefaultWindowWidth = gi.DefaultWindowWidth,
			DefaultWindowHeight = gi.DefaultWindowHeight,
			InfoFlags = (uint)gi.Info,
			LicenseCRC32 = (int)gi.LicenseCRC32,
			Timestamp = gi.Timestamp,
			ActiveTargets = gi.ActiveTargets,
			FunctionClassifications = (ulong)gi.FunctionClassifications,
			SteamAppID = gi.SteamAppID,
			DebuggerPort = (int)((gi.BytecodeVersion >= 14) ? gi.DebuggerPort : 0),
			GMS2FPS = (gi.Major >= 2) ? gi.GMS2FPS : 0f,
			GMS2AllowStatistics = gi.Major >= 2 && gi.GMS2AllowStatistics,
			RoomOrderCount = gi.RoomOrder?.Count ?? 0
		};
	}

	public static string GetVersionDisplay(GameMakerGeneralInfo? info)
	{
		if (info == null)
		{
			return "Unknown";
		}
		var rawVersion = $"{info.Major}.{info.Minor}.{info.Release}.{info.Build}";
		if (info.Major == 1)
		{
			return "GMS " + rawVersion;
		}
		var interpretedVersion = $"{info.Major}.{info.Minor}";
		if (info.Release != 0)
		{
			interpretedVersion += $".{info.Release}";
			if (info.Build != 0)
			{
				interpretedVersion += $".{info.Build}";
			}
		}
		// No format marker separates 2024.14 from the later runtimes until 2026.1, so the exact release is unknown.
		if (info.Major == 2024 && info.Minor == 14 && info.Release == 0 && info.Build == 0)
		{
			interpretedVersion += " or later";
		}
		var prefix = (info.Major < 2022 || info.Major == 2022 && info.Minor < 3) ? "GMS" : "GM";
		var suffix = info.Branch == GameMakerGeneralInfo.BranchType.LTS2022_0 ? " LTS" : "";
		return prefix + " " + interpretedVersion + suffix;
	}

	/// <summary>
	/// Describes where the version of the data file came from: the value stored in GEN8 or the chunk that raised it.
	/// </summary>
	public static string GetVersionBasis(GameMakerGeneralInfo? info)
	{
		if (info == null)
		{
			return "Unknown";
		}
		return info.VersionSource == null ? "stored in GEN8" : "detected from " + info.VersionSource;
	}

}
