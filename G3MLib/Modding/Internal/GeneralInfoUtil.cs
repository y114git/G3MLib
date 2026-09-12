using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Modding.Internal;

public static class GeneralInfoUtil
{
	public static GeneralInfoData? ExtractGeneralInfo(GameMakerData data)
	{
		GameMakerGeneralInfo gi = data.GeneralInfo;
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
			GMS2FPS = ((gi.Major >= 2) ? gi.GMS2FPS : 0f),
			GMS2AllowStatistics = (gi.Major >= 2 && gi.GMS2AllowStatistics),
			RoomOrderCount = (gi.RoomOrder?.Count ?? 0)
		};
	}

	public static string GetVersionDisplay(GameMakerGeneralInfo? info)
	{
		if (info == null)
		{
			return "Unknown";
		}
		string rawVersion = $"{info.Major}.{info.Minor}.{info.Release}.{info.Build}";
		string interpretedVersion;
		if (info.Branch == GameMakerGeneralInfo.BranchType.LTS2022_0)
		{
			interpretedVersion = "2022.0";
		}
		else
		{
			if (info.Major == 1)
			{
				return "GMS " + rawVersion;
			}
			interpretedVersion = $"{info.Major}.{info.Minor}";
			if (info.Release != 0)
			{
				interpretedVersion += $".{info.Release}";
				if (info.Build != 0)
				{
					interpretedVersion += $".{info.Build}";
				}
			}
		}
		string prefix = ((info.Major < 2022 || (info.Major == 2022 && info.Minor < 3)) ? "GMS" : "GM");
		if (rawVersion != interpretedVersion && info.Branch == GameMakerGeneralInfo.BranchType.LTS2022_0)
		{
			return $"{prefix} {interpretedVersion} (raw: {rawVersion})";
		}
		return prefix + " " + interpretedVersion;
	}

}
