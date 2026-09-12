using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project;
using G3MLib.DataFile.Util;
using ImageMagick;

namespace G3MLib.Modding.Resources;

public static class ResourceExportService
{
	private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
	{
		public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

		public bool Equals(T? x, T? y)
		{
			return x == y;
		}

		public int GetHashCode(T obj)
		{
			return RuntimeHelpers.GetHashCode(obj);
		}
	}

	private static readonly JsonSerializerOptions s_jsonOpts = new JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private static readonly char[] s_invalidChars = Path.GetInvalidFileNameChars();

	internal static string SafeName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return "_";
		}
		StringBuilder sb = new StringBuilder(name.Length);
		foreach (char ch in name)
		{
			sb.Append(s_invalidChars.Contains(ch) ? '_' : ch);
		}
		string safe = sb.ToString();
		if (safe.Length <= 96)
		{
			return safe;
		}
		string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..12];
		int prefixLength = 96 - hash.Length - 2;
		return safe.Substring(0, prefixLength) + "__" + hash;
	}

	private static string EnsureDir(string outputDir, string typeName)
	{
		string text = Path.Combine(outputDir, typeName);
		Directory.CreateDirectory(text);
		return text;
	}

	public static void ExportSelectiveExceptCode(GameMakerData data, string outputDir, string dataFilePath, Dictionary<string, HashSet<string>> changedNames)
	{
		Stopwatch totalSw = Stopwatch.StartNew();
		Stopwatch sw = new Stopwatch();
		sw.Restart();
		if (changedNames.ContainsKey("GeneralInfo"))
		{
			ExportGeneralInfo(data, outputDir);
		}
		if (changedNames.ContainsKey("Options"))
		{
			ExportOptions(data, outputDir);
		}
		if (changedNames.ContainsKey("GlobalScripts"))
		{
			ExportGlobalScripts(data, outputDir);
		}
		if (changedNames.ContainsKey("Language"))
		{
			ExportLanguage(data, outputDir);
		}
		if (changedNames.ContainsKey("FeatureFlags"))
		{
			ExportFeatureFlags(data, outputDir);
		}
		if (changedNames.ContainsKey("Tags"))
		{
			ExportTags(data, outputDir);
		}
		if (changedNames.ContainsKey("FilterEffects"))
		{
			ExportFilterEffects(data, outputDir, changedNames.GetValueOrDefault("FilterEffects"));
		}
		if (changedNames.ContainsKey("EmbeddedAudio"))
		{
			ExportEmbeddedAudio(data, outputDir, changedNames.GetValueOrDefault("EmbeddedAudio"));
		}
		if (changedNames.ContainsKey("TextureGroupInfo"))
		{
			ExportTextureGroupInfo(data, outputDir);
		}
		if (changedNames.ContainsKey("EmbeddedTextures"))
		{
			ExportEmbeddedTextures(data, outputDir, changedNames.GetValueOrDefault("EmbeddedTextures"));
		}
		if (changedNames.ContainsKey("TexturePageItems"))
		{
			ExportTexturePageItems(data, outputDir);
		}
		if (changedNames.ContainsKey("EmbeddedImages"))
		{
			ExportEmbeddedImages(data, outputDir, changedNames.GetValueOrDefault("EmbeddedImages"));
		}
		if (changedNames.ContainsKey("Extensions"))
		{
			ExportExtensions(data, outputDir);
		}
		bool shouldAppend;
		LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(30, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[Export Timing] Small types: ");
			handler.AppendFormatted(sw.Elapsed.TotalSeconds, "F1");
			handler.AppendLiteral("s");
		}
		LogService.Log(ref handler);
		Time("AudioGroups", delegate
		{
			ExportAudioGroups(data, outputDir, changedNames.GetValueOrDefault("AudioGroups"));
		});
		Time("Scripts", delegate
		{
			ExportScripts(data, outputDir, changedNames.GetValueOrDefault("Scripts"));
		});
		Time("Sprites", delegate
		{
			ExportSprites(data, outputDir, changedNames.GetValueOrDefault("Sprites"));
		});
		Time("Backgrounds", delegate
		{
			ExportBackgrounds(data, outputDir, changedNames.GetValueOrDefault("Backgrounds"));
		});
		Time("Fonts", delegate
		{
			ExportFonts(data, outputDir, changedNames.GetValueOrDefault("Fonts"));
		});
		Time("Sounds", delegate
		{
			ExportSounds(data, outputDir, dataFilePath, changedNames.GetValueOrDefault("Sounds"));
		});
		Time("Paths", delegate
		{
			ExportPaths(data, outputDir, changedNames.GetValueOrDefault("Paths"));
		});
		Time("Tilesets", delegate
		{
			ExportTilesets(data, outputDir, changedNames.GetValueOrDefault("Tilesets"));
		});
		Time("Shaders", delegate
		{
			ExportShaders(data, outputDir, changedNames.GetValueOrDefault("Shaders"));
		});
		Time("Timelines", delegate
		{
			ExportTimelines(data, outputDir, changedNames.GetValueOrDefault("Timelines"));
		});
		Time("GameObjects", delegate
		{
			ExportGameObjects(data, outputDir, changedNames.GetValueOrDefault("GameObjects"));
		});
		Time("ParticleSystemEmitters", delegate
		{
			ExportParticleSystemEmitters(data, outputDir, changedNames.GetValueOrDefault("ParticleSystemEmitters"));
		});
		Time("ParticleSystems", delegate
		{
			ExportParticleSystems(data, outputDir, changedNames.GetValueOrDefault("ParticleSystems"));
		});
		Time("Sequences", delegate
		{
			ExportSequences(data, outputDir, changedNames.GetValueOrDefault("Sequences"));
		});
		Time("Rooms", delegate
		{
			ExportRooms(data, outputDir, changedNames.GetValueOrDefault("Rooms"));
		});
		Time("AnimationCurves", delegate
		{
			ExportAnimationCurves(data, outputDir, changedNames.GetValueOrDefault("AnimationCurves"));
		});
		totalSw.Stop();
		LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(48, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler2.AppendLiteral("[Export Timing] TOTAL (selective, excl. code): ");
			handler2.AppendFormatted(totalSw.Elapsed.TotalSeconds, "F1");
			handler2.AppendLiteral("s");
		}
		LogService.Log(ref handler2);
		void Time(string name, Action action)
		{
			HashSet<string> filter = changedNames.GetValueOrDefault(name);
			if (filter != null && filter.Count != 0)
			{
				sw.Restart();
				action();
				bool shouldAppend2;
				LogInterpolatedStringHandler handler3 = new LogInterpolatedStringHandler(32, 3, out shouldAppend2);
				if (shouldAppend2)
				{
					handler3.AppendLiteral("[Export Timing] ");
					handler3.AppendFormatted(name);
					handler3.AppendLiteral(": ");
					handler3.AppendFormatted(sw.Elapsed.TotalSeconds, "F1");
					handler3.AppendLiteral("s (");
					handler3.AppendFormatted(filter.Count);
					handler3.AppendLiteral(" resources)");
				}
				LogService.Log(ref handler3);
			}
		}
	}

	public static void ExportTypes(GameMakerData data, string outputDir, string dataFilePath, IEnumerable<string> resourceTypes)
	{
		foreach (string type in resourceTypes)
		{
			ExportSingle(data, outputDir, dataFilePath, type);
		}
	}

	private static void ExportSingle(GameMakerData data, string outputDir, string dataFilePath, string resourceType)
	{
		if (resourceType == null)
		{
			return;
		}
		switch (resourceType.Length)
		{
		case 11:
			switch (resourceType[4])
			{
			case 'r':
				if (resourceType == "GeneralInfo")
				{
					ExportGeneralInfo(data, outputDir);
				}
				break;
			case 'o':
				if (resourceType == "AudioGroups")
				{
					ExportAudioGroups(data, outputDir);
				}
				break;
			case 'g':
				if (resourceType == "Backgrounds")
				{
					ExportBackgrounds(data, outputDir);
				}
				break;
			case 'O':
				if (resourceType == "GameObjects")
				{
					ExportGameObjects(data, outputDir);
				}
				break;
			case 'E':
				if (resourceType == "CodeEntries")
				{
					ExportCodeEntries(data, outputDir);
				}
				break;
			}
			break;
		case 7:
			switch (resourceType[4])
			{
			case 'o':
				if (resourceType == "Options")
				{
					ExportOptions(data, outputDir);
				}
				break;
			case 'p':
				if (resourceType == "Scripts")
				{
					ExportScripts(data, outputDir);
				}
				break;
			case 't':
				if (resourceType == "Sprites")
				{
					ExportSprites(data, outputDir);
				}
				break;
			case 'e':
				if (resourceType == "Shaders")
				{
					ExportShaders(data, outputDir);
				}
				break;
			}
			break;
		case 13:
			switch (resourceType[0])
			{
			case 'G':
				if (resourceType == "GlobalScripts")
				{
					ExportGlobalScripts(data, outputDir);
				}
				break;
			case 'F':
				if (resourceType == "FilterEffects")
				{
					ExportFilterEffects(data, outputDir);
				}
				break;
			case 'E':
				if (resourceType == "EmbeddedAudio")
				{
					ExportEmbeddedAudio(data, outputDir);
				}
				break;
			}
			break;
		case 8:
			switch (resourceType[0])
			{
			case 'L':
				if (resourceType == "Language")
				{
					ExportLanguage(data, outputDir);
				}
				break;
			case 'T':
				if (resourceType == "Tilesets")
				{
					ExportTilesets(data, outputDir);
				}
				break;
			}
			break;
		case 16:
			switch (resourceType[7])
			{
			case 'G':
				if (resourceType == "TextureGroupInfo")
				{
					ExportTextureGroupInfo(data, outputDir);
				}
				break;
			case 'd':
				if (resourceType == "EmbeddedTextures")
				{
					ExportEmbeddedTextures(data, outputDir);
				}
				break;
			case 'P':
				if (resourceType == "TexturePageItems")
				{
					ExportTexturePageItems(data, outputDir);
				}
				break;
			}
			break;
		case 5:
			switch (resourceType[0])
			{
			case 'F':
				if (resourceType == "Fonts")
				{
					ExportFonts(data, outputDir);
				}
				break;
			case 'P':
				if (resourceType == "Paths")
				{
					ExportPaths(data, outputDir);
				}
				break;
			case 'R':
				if (resourceType == "Rooms")
				{
					ExportRooms(data, outputDir);
				}
				break;
			}
			break;
		case 9:
			switch (resourceType[0])
			{
			case 'T':
				if (resourceType == "Timelines")
				{
					ExportTimelines(data, outputDir);
				}
				break;
			case 'S':
				if (resourceType == "Sequences")
				{
					ExportSequences(data, outputDir);
				}
				break;
			}
			break;
		case 15:
			switch (resourceType[0])
			{
			case 'P':
				if (resourceType == "ParticleSystems")
				{
					ExportParticleSystems(data, outputDir);
				}
				break;
			case 'A':
				if (resourceType == "AnimationCurves")
				{
					ExportAnimationCurves(data, outputDir);
				}
				break;
			}
			break;
		case 12:
			if (resourceType == "FeatureFlags")
			{
				ExportFeatureFlags(data, outputDir);
			}
			break;
		case 4:
			if (resourceType == "Tags")
			{
				ExportTags(data, outputDir);
			}
			break;
		case 14:
			if (resourceType == "EmbeddedImages")
			{
				ExportEmbeddedImages(data, outputDir);
			}
			break;
		case 6:
			if (resourceType == "Sounds")
			{
				ExportSounds(data, outputDir, dataFilePath);
			}
			break;
		case 22:
			if (resourceType == "ParticleSystemEmitters")
			{
				ExportParticleSystemEmitters(data, outputDir);
			}
			break;
		case 10:
			if (resourceType == "Extensions")
			{
				ExportExtensions(data, outputDir);
			}
			break;
		case 17:
		case 18:
		case 19:
		case 20:
		case 21:
			break;
		}
	}

	public static void ExportGeneralInfo(GameMakerData data, string outputDir)
	{
		if (data.GeneralInfo == null)
		{
			return;
		}
		using FileStream stream = new FileStream(Path.Combine(EnsureDir(outputDir, "GeneralInfo"), "GeneralInfo.json"), FileMode.Create);
		using Utf8JsonWriter w = new Utf8JsonWriter(stream, new JsonWriterOptions
		{
			Indented = true
		});
		GameMakerGeneralInfo gi = data.GeneralInfo;
		w.WriteStartObject();
		w.WriteBoolean("isDebuggerDisabled", gi.IsDebuggerDisabled);
		w.WriteNumber("bytecodeVersion", gi.BytecodeVersion);
		w.WriteNumber("padding", gi.Padding);
		w.WriteString("fileName", gi.FileName?.Content ?? "");
		w.WriteString("config", gi.Config?.Content ?? "");
		w.WriteNumber("lastObj", gi.LastObj);
		w.WriteNumber("lastTile", gi.LastTile);
		w.WriteNumber("gameID", gi.GameID);
		w.WriteString("directPlayGuid", gi.DirectPlayGuid.ToString());
		w.WriteString("name", gi.Name?.Content ?? "");
		w.WriteNumber("major", gi.Major);
		w.WriteNumber("minor", gi.Minor);
		w.WriteNumber("release", gi.Release);
		w.WriteNumber("build", gi.Build);
		w.WriteString("branch", gi.Branch.ToString());
		w.WriteNumber("defaultWindowWidth", gi.DefaultWindowWidth);
		w.WriteNumber("defaultWindowHeight", gi.DefaultWindowHeight);
		w.WriteNumber("infoFlags", (uint)gi.Info);
		w.WritePropertyName("infoFlagsDecoded");
		w.WriteStartObject();
		w.WriteBoolean("fullscreen", (gi.Info & GameMakerGeneralInfo.InfoFlags.Fullscreen) != 0);
		w.WriteBoolean("syncVertex1", (gi.Info & GameMakerGeneralInfo.InfoFlags.SyncVertex1) != 0);
		w.WriteBoolean("syncVertex2", (gi.Info & GameMakerGeneralInfo.InfoFlags.SyncVertex2) != 0);
		w.WriteBoolean("interpolate", (gi.Info & GameMakerGeneralInfo.InfoFlags.Interpolate) != 0);
		w.WriteBoolean("scale", (gi.Info & GameMakerGeneralInfo.InfoFlags.Scale) != 0);
		w.WriteBoolean("showCursor", (gi.Info & GameMakerGeneralInfo.InfoFlags.ShowCursor) != 0);
		w.WriteBoolean("sizeable", (gi.Info & GameMakerGeneralInfo.InfoFlags.Sizeable) != 0);
		w.WriteBoolean("screenKey", (gi.Info & GameMakerGeneralInfo.InfoFlags.ScreenKey) != 0);
		w.WriteBoolean("syncVertex3", (gi.Info & GameMakerGeneralInfo.InfoFlags.SyncVertex3) != 0);
		w.WriteBoolean("studioVersionB1", (gi.Info & GameMakerGeneralInfo.InfoFlags.StudioVersionB1) != 0);
		w.WriteBoolean("studioVersionB2", (gi.Info & GameMakerGeneralInfo.InfoFlags.StudioVersionB2) != 0);
		w.WriteBoolean("studioVersionB3", (gi.Info & GameMakerGeneralInfo.InfoFlags.StudioVersionB3) != 0);
		w.WriteBoolean("studioVersionMask", (gi.Info & GameMakerGeneralInfo.InfoFlags.StudioVersionMask) != 0);
		w.WriteBoolean("steamEnabled", (gi.Info & GameMakerGeneralInfo.InfoFlags.SteamEnabled) != 0);
		w.WriteBoolean("useAppDataSaveLocation", (gi.Info & GameMakerGeneralInfo.InfoFlags.UseAppDataSaveLocation) != 0);
		w.WriteBoolean("borderlessWindow", (gi.Info & GameMakerGeneralInfo.InfoFlags.BorderlessWindow) != 0);
		w.WriteBoolean("javaScriptMode", (gi.Info & GameMakerGeneralInfo.InfoFlags.JavaScriptMode) != 0);
		bool gameRunFromGms2Ide = (gi.Info & GameMakerGeneralInfo.InfoFlags.GameRunFromGMS2IDE) != 0;
		w.WriteBoolean("licenseExclusions", gameRunFromGms2Ide);
		w.WriteBoolean("gameRunFromGMS2IDE", gameRunFromGms2Ide);
		w.WriteEndObject();
		w.WriteNumber("licenseCRC32", gi.LicenseCRC32);
		w.WritePropertyName("licenseMD5");
		w.WriteStartArray();
		if (gi.LicenseMD5 != null)
		{
			byte[] licenseMD = gi.LicenseMD5;
			foreach (byte b in licenseMD)
			{
				w.WriteNumberValue(b);
			}
		}
		w.WriteEndArray();
		w.WriteNumber("timestamp", gi.Timestamp);
		w.WriteString("displayName", gi.DisplayName?.Content ?? "");
		w.WriteNumber("activeTargets", gi.ActiveTargets);
		w.WriteNumber("functionClassifications", (ulong)gi.FunctionClassifications);
		w.WriteNumber("steamAppID", gi.SteamAppID);
		if (gi.BytecodeVersion >= 14)
		{
			w.WriteNumber("debuggerPort", gi.DebuggerPort);
		}
		w.WritePropertyName("roomOrder");
		w.WriteStartArray();
		foreach (GameMakerResourceById<GameMakerRoom, GameMakerChunkROOM> roomRef in gi.RoomOrder)
		{
			if (roomRef != null && roomRef.Resource?.Name?.Content != null)
			{
				w.WriteStringValue(roomRef.Resource.Name.Content);
			}
			else
			{
				w.WriteNullValue();
			}
		}
		w.WriteEndArray();
		if (gi.Major >= 2)
		{
			w.WritePropertyName("gms2RandomUID");
			w.WriteStartArray();
			if (gi.GMS2RandomUID != null)
			{
				foreach (long uid in gi.GMS2RandomUID)
				{
					w.WriteNumberValue(uid);
				}
			}
			w.WriteEndArray();
			w.WriteNumber("gms2FPS", gi.GMS2FPS);
			w.WriteBoolean("gms2AllowStatistics", gi.GMS2AllowStatistics);
			w.WritePropertyName("gms2GameGUID");
			w.WriteStartArray();
			if (gi.GMS2GameGUID != null)
			{
				byte[] licenseMD = gi.GMS2GameGUID;
				foreach (byte b2 in licenseMD)
				{
					w.WriteNumberValue(b2);
				}
			}
			w.WriteEndArray();
		}
		w.WriteEndObject();
	}

	public static void ExportOptions(GameMakerData data, string outputDir)
	{
		if (data.Options != null)
		{
			string path = EnsureDir(outputDir, "Options");
			GameMakerOptions options = data.Options;
			File.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
			{
				["newFormat"] = options.NewFormat,
				["shaderExtensionFlag"] = options.ShaderExtensionFlag,
				["shaderExtensionVersion"] = options.ShaderExtensionVersion,
				["info"] = (ulong)options.Info,
				["scale"] = options.Scale,
				["windowColor"] = options.WindowColor,
				["colorDepth"] = options.ColorDepth,
				["resolution"] = options.Resolution,
				["frequency"] = options.Frequency,
				["vertexSync"] = options.VertexSync,
				["priority"] = options.Priority,
				["loadAlpha"] = options.LoadAlpha,
				["constants"] = options.Constants?.Select((GameMakerOptions.Constant c) => new Dictionary<string, object>
				{
					["name"] = c?.Name?.Content ?? "",
					["value"] = c?.Value?.Content ?? ""
				}).ToArray() ?? Array.Empty<Dictionary<string, object>>()
			}, s_jsonOpts), path: Path.Combine(path, "options.json"));
		}
	}

	public static void ExportGlobalScripts(GameMakerData data, string outputDir)
	{
		string path = EnsureDir(outputDir, "GlobalScripts");
		File.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["globalInitScripts"] = data.GlobalInitScripts?.Select((GameMakerGlobalInit s) => s?.Code?.Name?.Content ?? "").ToArray() ?? Array.Empty<string>(),
			["gameEndScripts"] = data.GameEndScripts?.Select((GameMakerGlobalInit s) => s?.Code?.Name?.Content ?? "").ToArray() ?? Array.Empty<string>()
		}, s_jsonOpts), path: Path.Combine(path, "global_scripts.json"));
	}

	public static void ExportScripts(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		if (data.Scripts == null)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Scripts");
		List<(GameMakerScript, string)> entries = BuildUniqueExportNames((from script in data.Scripts
			where script?.Name?.Content != null
			select (script: script, script.Name.Content)).ToList());
		if (filter != null)
		{
			entries = entries.Where<(GameMakerScript, string)>(((GameMakerScript Item, string ExportName) entry) => filter.Contains(entry.ExportName) || filter.Contains(entry.Item.Name?.Content ?? string.Empty)).ToList();
		}
		Parallel.ForEach<(GameMakerScript, string)>(entries, delegate((GameMakerScript Item, string ExportName) entry)
		{
			GameMakerScript item = entry.Item;
			string item2 = entry.ExportName;
			string text = Path.Combine(dir, item2);
			Directory.CreateDirectory(text);
			File.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
			{
				["name"] = item.Name.Content,
				["code"] = item.Code?.Name?.Content ?? "",
				["isConstructor"] = item.IsConstructor
			}, s_jsonOpts), path: Path.Combine(text, item2 + ".json"));
		});
	}

	private static List<(T Item, string ExportName)> BuildUniqueExportNames<T>(List<(T Item, string Name)> items)
	{
		Dictionary<string, int> used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		List<(T, string)> result = new List<(T, string)>(items.Count);
		foreach (var item2 in items)
		{
			T item = item2.Item;
			string safeName = SafeName(item2.Name);
			if (string.IsNullOrWhiteSpace(safeName))
			{
				safeName = "_";
			}
			if (!used.TryGetValue(safeName, out var count))
			{
				used[safeName] = 1;
				result.Add((item, safeName));
				continue;
			}
			string uniqueName;
			do
			{
				count++;
				uniqueName = $"{safeName}__{count}";
			}
			while (used.ContainsKey(uniqueName));
			used[safeName] = count;
			used[uniqueName] = 1;
			result.Add((item, uniqueName));
		}
		return result;
	}

	public static void ExportEmbeddedImages(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		if (data.EmbeddedImages == null)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "EmbeddedImages");
		Dictionary<GameMakerTexturePageItem, int> texturePageItemIndices = ((data.TexturePageItems == null) ? null : BuildReferenceIndex(data.TexturePageItems));
		Parallel.ForEach(data.EmbeddedImages.ToList(), delegate(GameMakerEmbeddedImage image)
		{
			if (image == null || image.Name?.Content == null || (filter != null && !filter.Contains(image.Name.Content)))
			{
				return;
			}
			string text = SafeName(image.Name.Content);
			string text2 = Path.Combine(dir, text);
			Directory.CreateDirectory(text2);
			Dictionary<string, object> obj = new Dictionary<string, object> { ["name"] = image.Name.Content };
			int num;
			if (image.TextureEntry != null)
			{
				Dictionary<GameMakerTexturePageItem, int> dictionary = texturePageItemIndices;
				if (dictionary != null && dictionary.TryGetValue(image.TextureEntry, out var value))
				{
					num = value;
					goto IL_00a6;
				}
			}
			num = -1;
			goto IL_00a6;
			IL_00a6:
			obj["texturePageItemIndex"] = num;
			obj["texturePageItemName"] = image.TextureEntry?.Name?.Content ?? "";
			Dictionary<string, object> value2 = obj;
			File.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(value2, s_jsonOpts));
		});
	}

	public static void ExportFeatureFlags(GameMakerData data, string outputDir)
	{
		if (data.FeatureFlags?.List != null)
		{
			string path = EnsureDir(outputDir, "FeatureFlags");
			File.WriteAllText(contents: JsonSerializer.Serialize(data.FeatureFlags.List.Select((GameMakerString f) => f?.Content ?? "").ToArray(), s_jsonOpts), path: Path.Combine(path, "feature_flags.json"));
		}
	}

	public static void ExportLanguage(GameMakerData data, string outputDir)
	{
		if (data.Language == null)
		{
			return;
		}
		string path = EnsureDir(outputDir, "Language");
		File.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["unknown1"] = data.Language.Unknown1,
			["entryIds"] = data.Language.EntryIDs?.Select((GameMakerString e) => e?.Content ?? "").ToArray() ?? Array.Empty<string>(),
			["languages"] = (from language in data.Language.Languages?.Where((GameMakerLanguage.LanguageData l) => l != null)
				select new Dictionary<string, object>
				{
					["name"] = language.Name?.Content ?? "",
					["region"] = language.Region?.Content ?? "",
					["entries"] = language.Entries?.Select((GameMakerString e) => e?.Content ?? "").ToArray() ?? Array.Empty<string>()
				}).ToArray() ?? Array.Empty<Dictionary<string, object>>()
		}, s_jsonOpts), path: Path.Combine(path, "language.json"));
	}

	public static void ExportTags(GameMakerData data, string outputDir)
	{
		if (data.Tags == null)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Tags");
		List<Dictionary<string, object>> assetTags = new List<Dictionary<string, object>>();
		if (data.Tags.AssetTags != null)
		{
			foreach (KeyValuePair<int, GameMakerSimpleListString> item in data.Tags.AssetTags.OrderBy<KeyValuePair<int, GameMakerSimpleListString>, int>((KeyValuePair<int, GameMakerSimpleListString> kvp) => kvp.Key))
			{
				item.Deconstruct(out var key, out var value);
				int assetId = key;
				GameMakerSimpleListString tags = value;
				(string, int, string) decoded = DecodeAssetTagId(data, assetId);
				assetTags.Add(new Dictionary<string, object>
				{
					["assetId"] = assetId,
					["assetType"] = decoded.Item1,
					["assetIndex"] = decoded.Item2,
					["assetName"] = decoded.Item3,
					["tags"] = tags?.Select((GameMakerString t) => t?.Content ?? "").ToArray() ?? Array.Empty<string>()
				});
			}
		}
		Dictionary<string, object> payload = new Dictionary<string, object>
		{
			["tags"] = data.Tags.Tags?.Select((GameMakerString t) => t?.Content ?? "").ToArray() ?? Array.Empty<string>(),
			["assetTags"] = assetTags
		};
		File.WriteAllText(Path.Combine(dir, "tags.json"), JsonSerializer.Serialize(payload, s_jsonOpts));
	}

	public static void ExportFilterEffects(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		List<GameMakerFilterEffect> items = data.FilterEffects?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "FilterEffects");
		Parallel.ForEach(items, delegate(GameMakerFilterEffect effect)
		{
			if (effect == null || effect.Name?.Content == null || (filter != null && !filter.Contains(effect.Name.Content)))
			{
				return;
			}
			try
			{
				string text = SafeName(effect.Name.Content);
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				File.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
				{
					["name"] = effect.Name.Content,
					["value"] = effect.Value?.Content ?? ""
				}, s_jsonOpts), path: Path.Combine(text2, text + ".json"), encoding: Encoding.UTF8);
			}
			catch (Exception ex)
			{
				bool shouldAppend;
				LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(43, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler.AppendLiteral("[ExportFilterEffects] Failed to export '");
					handler.AppendFormatted(effect.Name.Content);
					handler.AppendLiteral("': ");
					handler.AppendFormatted(ex.Message);
				}
				LogService.Log(ref handler);
			}
		});
	}

	private static (string Type, int Index, string Name) DecodeAssetTagId(GameMakerData data, int assetId)
	{
		ResourceType type = (ResourceType)(assetId >> 24);
		int rawIndex = assetId & 0xFFFFFF;
		int index = ((type == ResourceType.Script) ? (rawIndex - 100000) : rawIndex);
		string name = ResourceAssetUtil.GetAssetNameByTagType(data, type, index) ?? "";
		return (Type: type.ToString(), Index: index, Name: name);
	}

	public static void ExportAudioGroups(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		List<GameMakerAudioGroup> items = data.AudioGroups?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "AudioGroups");
		Parallel.ForEach(items, delegate(GameMakerAudioGroup ag)
		{
			if (ag == null || ag.Name?.Content == null || (filter != null && !filter.Contains(ag.Name.Content)))
			{
				return;
			}
			try
			{
				string text = SafeName(ag.Name.Content);
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				using FileStream utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
				{
					Indented = true
				});
				utf8JsonWriter.WriteStartObject();
				utf8JsonWriter.WriteString("name", ag.Name.Content);
				if (ag.Path != null)
				{
					utf8JsonWriter.WriteString("path", ag.Path.Content ?? "");
				}
				utf8JsonWriter.WriteEndObject();
			}
			catch
			{
			}
		});
	}

	public static void ExportTextureGroupInfo(GameMakerData data, string outputDir)
	{
		if (data.TextureGroupInfo == null || data.TextureGroupInfo.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "TextureGroupInfo");
		Parallel.ForEach(data.TextureGroupInfo.ToList(), delegate(GameMakerTextureGroupInfo tg)
		{
			if (tg == null || tg.Name?.Content == null)
			{
				return;
			}
			try
			{
				string text = SafeName(tg.Name.Content);
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				using FileStream utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
				{
					Indented = true
				});
				utf8JsonWriter.WriteStartObject();
				utf8JsonWriter.WriteString("name", tg.Name.Content);
				if (data.IsVersionAtLeast(2022u, 9u))
				{
					if (tg.Directory != null)
					{
						utf8JsonWriter.WriteString("directory", tg.Directory.Content ?? "");
					}
					if (tg.Extension != null)
					{
						utf8JsonWriter.WriteString("extension", tg.Extension.Content ?? "");
					}
					utf8JsonWriter.WriteNumber("loadType", (int)tg.LoadType);
					utf8JsonWriter.WriteString("loadTypeDescription", tg.LoadType.ToString());
				}
				utf8JsonWriter.WriteStartArray("texturePages");
				if (tg.TexturePages != null)
				{
					foreach (GameMakerResourceById<GameMakerEmbeddedTexture, GameMakerChunkTXTR> current in tg.TexturePages)
					{
						if (current.Resource?.Name?.Content != null)
						{
							utf8JsonWriter.WriteStringValue(current.Resource.Name.Content);
						}
					}
				}
				utf8JsonWriter.WriteEndArray();
				utf8JsonWriter.WriteStartArray("sprites");
				if (tg.Sprites != null)
				{
					foreach (GameMakerResourceById<GameMakerSprite, GameMakerChunkSPRT> current2 in tg.Sprites)
					{
						if (current2.Resource?.Name?.Content != null)
						{
							utf8JsonWriter.WriteStringValue(current2.Resource.Name.Content);
						}
					}
				}
				utf8JsonWriter.WriteEndArray();
				if (!data.IsNonLTSVersionAtLeast(2023u, 1u))
				{
					utf8JsonWriter.WriteStartArray("spineSprites");
					if (tg.SpineSprites != null)
					{
						foreach (GameMakerResourceById<GameMakerSprite, GameMakerChunkSPRT> current3 in tg.SpineSprites)
						{
							if (current3.Resource?.Name?.Content != null)
							{
								utf8JsonWriter.WriteStringValue(current3.Resource.Name.Content);
							}
						}
					}
					utf8JsonWriter.WriteEndArray();
				}
				utf8JsonWriter.WriteStartArray("fonts");
				if (tg.Fonts != null)
				{
					foreach (GameMakerResourceById<GameMakerFont, GameMakerChunkFONT> current4 in tg.Fonts)
					{
						if (current4.Resource?.Name?.Content != null)
						{
							utf8JsonWriter.WriteStringValue(current4.Resource.Name.Content);
						}
					}
				}
				utf8JsonWriter.WriteEndArray();
				utf8JsonWriter.WriteStartArray("tilesets");
				if (tg.Tilesets != null)
				{
					foreach (GameMakerResourceById<GameMakerBackground, GameMakerChunkBGND> current5 in tg.Tilesets)
					{
						if (current5.Resource?.Name?.Content != null)
						{
							utf8JsonWriter.WriteStringValue(current5.Resource.Name.Content);
						}
					}
				}
				utf8JsonWriter.WriteEndArray();
				utf8JsonWriter.WriteEndObject();
			}
			catch
			{
			}
		});
	}

	public static void ExportEmbeddedTextures(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		if (data.EmbeddedTextures == null || data.EmbeddedTextures.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "EmbeddedTextures");
		Parallel.For(0, data.EmbeddedTextures.Count, delegate(int i)
		{
			string text = $"texture_{i:D4}";
			if (filter != null && !filter.Contains(text))
			{
				return;
			}
			GameMakerEmbeddedTexture gameMakerEmbeddedTexture = data.EmbeddedTextures[i];
			try
			{
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				if (gameMakerEmbeddedTexture.TextureData?.Image != null)
				{
					bool flag = false;
					try
					{
						byte[] bytes = gameMakerEmbeddedTexture.TextureData.Image.ToSpan(data.IsVersionAtLeast(2022u, 5u)).ToArray();
						File.WriteAllBytes(Path.Combine(text2, text + ".bin"), bytes);
						flag = true;
					}
					catch (Exception ex)
					{
						LogService.Warning("[ExportEmbeddedTextures] Binary export failed for " + text + ", keeping PNG payload: " + ex.Message);
					}
					if (!flag)
					{
						using MagickImage magickImage = gameMakerEmbeddedTexture.TextureData.Image.GetMagickImage();
						magickImage.Strip();
						byte[] data2 = GMImage.FromMagickImage(magickImage).ConvertToPng().GetData();
						File.WriteAllBytes(Path.Combine(text2, text + ".png"), data2);
					}
				}
				Dictionary<string, object> value = new Dictionary<string, object>
				{
					["index"] = i,
					["name"] = gameMakerEmbeddedTexture.Name?.Content ?? "",
					["scaled"] = gameMakerEmbeddedTexture.Scaled,
					["generatedMips"] = gameMakerEmbeddedTexture.GeneratedMips,
					["format"] = gameMakerEmbeddedTexture.TextureData?.Image?.Format.ToString() ?? "Png"
				};
				File.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(value, s_jsonOpts), Encoding.UTF8);
			}
			catch (Exception ex2)
			{
				LogService.Warning("[ExportEmbeddedTextures] Failed to export " + text + ": " + ex2.Message);
			}
		});
	}

	public static void ExportTexturePageItems(GameMakerData data, string outputDir)
	{
		if (data.TexturePageItems == null)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "TexturePageItems");
		Dictionary<GameMakerEmbeddedTexture, int> embeddedTextureIndices = BuildReferenceIndex(data.EmbeddedTextures);
		List<Dictionary<string, object>> texturePageItems = new List<Dictionary<string, object>>(data.TexturePageItems.Count);
		for (int i = 0; i < data.TexturePageItems.Count; i++)
		{
			GameMakerTexturePageItem tpi = data.TexturePageItems[i];
			if (tpi == null)
			{
				texturePageItems.Add(new Dictionary<string, object>
				{
					["index"] = i,
					["isNull"] = true
				});
				continue;
			}
			int textureIndex;
			int texIdx = ((tpi.TexturePage != null && embeddedTextureIndices.TryGetValue(tpi.TexturePage, out textureIndex)) ? textureIndex : (-1));
			texturePageItems.Add(new Dictionary<string, object>
			{
				["index"] = i,
				["name"] = tpi.Name?.Content ?? "",
				["texturePageIndex"] = texIdx,
				["sourceX"] = tpi.SourceX,
				["sourceY"] = tpi.SourceY,
				["sourceWidth"] = tpi.SourceWidth,
				["sourceHeight"] = tpi.SourceHeight,
				["targetX"] = tpi.TargetX,
				["targetY"] = tpi.TargetY,
				["targetWidth"] = tpi.TargetWidth,
				["targetHeight"] = tpi.TargetHeight,
				["boundingWidth"] = tpi.BoundingWidth,
				["boundingHeight"] = tpi.BoundingHeight
			});
		}
		File.WriteAllText(Path.Combine(dir, "texture_page_items.json"), JsonSerializer.Serialize(texturePageItems, s_jsonOpts));
	}

	public static void ExportSprites(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		if (data.Sprites == null || data.Sprites.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Sprites");
		(GameMakerSprite, int)[] sprites = (from item in data.Sprites.Select((GameMakerSprite sprite, int index) => (Sprite: sprite, Index: index))
			where item.Sprite?.Name?.Content != null && (filter == null || filter.Contains(item.Sprite.Name.Content))
			select item).ToArray();
		if (sprites.Length == 0)
		{
			return;
		}
		Dictionary<GameMakerEmbeddedTexture, int> texturePageIndexes = new Dictionary<GameMakerEmbeddedTexture, int>(ReferenceEqualityComparer.Instance);
		for (int i = 0; i < (data.EmbeddedTextures?.Count ?? 0); i++)
		{
			GameMakerEmbeddedTexture texture = data.EmbeddedTextures[i];
			if (texture != null)
			{
				texturePageIndexes[texture] = i;
			}
		}
		TextureWorker worker = new TextureWorker();
		try
		{
			Parallel.ForEach(sprites, delegate((GameMakerSprite Sprite, int Index) item)
			{
				var (gameMakerSprite, _) = item;
				try
				{
					int item2 = item.Index;
					string text = SafeName(gameMakerSprite.Name?.Content ?? string.Empty);
					string text2 = (string.IsNullOrEmpty(text) ? $"__unnamed_sprite__idx{item2}" : text);
					string text3 = Path.Combine(dir, text2);
					Directory.CreateDirectory(text3);
					Dictionary<string, object> dictionary = new Dictionary<string, object>
					{
						["index"] = item2,
						["name"] = gameMakerSprite.Name?.Content ?? "",
						["width"] = gameMakerSprite.Width,
						["height"] = gameMakerSprite.Height,
						["marginLeft"] = gameMakerSprite.MarginLeft,
						["marginRight"] = gameMakerSprite.MarginRight,
						["marginTop"] = gameMakerSprite.MarginTop,
						["marginBottom"] = gameMakerSprite.MarginBottom,
						["originX"] = gameMakerSprite.OriginX,
						["originY"] = gameMakerSprite.OriginY,
						["transparent"] = gameMakerSprite.Transparent,
						["smooth"] = gameMakerSprite.Smooth,
						["preload"] = gameMakerSprite.Preload,
						["bboxMode"] = gameMakerSprite.BBoxMode,
						["sepMasks"] = (uint)gameMakerSprite.SepMasks,
						["sepMasksDescription"] = gameMakerSprite.SepMasks.ToString(),
						["textureCount"] = gameMakerSprite.Textures.Count
					};
					List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
					for (int j = 0; j < gameMakerSprite.Textures.Count; j++)
					{
						GameMakerTexturePageItem gameMakerTexturePageItem = gameMakerSprite.Textures[j]?.Texture;
						if (gameMakerTexturePageItem != null)
						{
							int value;
							int num = ((gameMakerTexturePageItem.TexturePage != null && texturePageIndexes.TryGetValue(gameMakerTexturePageItem.TexturePage, out value)) ? value : (-1));
							list.Add(new Dictionary<string, object>
							{
								["frameIndex"] = j,
								["texturePageIndex"] = num,
								["sourceX"] = gameMakerTexturePageItem.SourceX,
								["sourceY"] = gameMakerTexturePageItem.SourceY,
								["sourceWidth"] = gameMakerTexturePageItem.SourceWidth,
								["sourceHeight"] = gameMakerTexturePageItem.SourceHeight,
								["targetX"] = gameMakerTexturePageItem.TargetX,
								["targetY"] = gameMakerTexturePageItem.TargetY,
								["targetWidth"] = gameMakerTexturePageItem.TargetWidth,
								["targetHeight"] = gameMakerTexturePageItem.TargetHeight,
								["boundingWidth"] = gameMakerTexturePageItem.BoundingWidth,
								["boundingHeight"] = gameMakerTexturePageItem.BoundingHeight
							});
						}
						else
						{
							list.Add(new Dictionary<string, object>
							{
								["frameIndex"] = j,
								["isNull"] = true
							});
						}
					}
					dictionary["textureFrames"] = list;
					if (data.IsGameMaker2())
					{
						dictionary["isSpecialType"] = gameMakerSprite.IsSpecialType;
						dictionary["sVersion"] = gameMakerSprite.SVersion;
						dictionary["sSpriteType"] = (uint)gameMakerSprite.SSpriteType;
						dictionary["sSpriteTypeDescription"] = gameMakerSprite.SSpriteType.ToString();
						dictionary["gms2PlaybackSpeed"] = gameMakerSprite.GMS2PlaybackSpeed;
						dictionary["gms2PlaybackSpeedType"] = (uint)gameMakerSprite.GMS2PlaybackSpeedType;
						dictionary["gms2PlaybackSpeedTypeDescription"] = gameMakerSprite.GMS2PlaybackSpeedType.ToString();
					}
					ObservableCollection<GameMakerSprite.MaskEntry> collisionMasks = gameMakerSprite.CollisionMasks;
					if (collisionMasks != null && collisionMasks.Count > 0)
					{
						List<Dictionary<string, object>> list2 = new List<Dictionary<string, object>>();
						foreach (GameMakerSprite.MaskEntry current in gameMakerSprite.CollisionMasks)
						{
							if (current != null && current.Data?.Length > 0)
							{
								list2.Add(new Dictionary<string, object>
								{
									["width"] = current.Width,
									["height"] = current.Height,
									["data"] = Convert.ToBase64String(current.Data)
								});
							}
						}
						dictionary["collisionMasks"] = list2;
					}
					if (gameMakerSprite.V3NineSlice != null)
					{
						Dictionary<string, object> dictionary2 = new Dictionary<string, object>
						{
							["left"] = gameMakerSprite.V3NineSlice.Left,
							["top"] = gameMakerSprite.V3NineSlice.Top,
							["right"] = gameMakerSprite.V3NineSlice.Right,
							["bottom"] = gameMakerSprite.V3NineSlice.Bottom,
							["enabled"] = gameMakerSprite.V3NineSlice.Enabled
						};
						if (gameMakerSprite.V3NineSlice.TileModes != null)
						{
							dictionary2["tileModes"] = gameMakerSprite.V3NineSlice.TileModes.Select((GameMakerSprite.NineSlice.TileMode t) => (int)t).ToArray();
						}
						dictionary["nineSlice"] = dictionary2;
					}
					if (gameMakerSprite.IsSpineSprite)
					{
						dictionary["isSpineSprite"] = true;
						dictionary["spineVersion"] = gameMakerSprite.SpineVersion;
					}
					if (gameMakerSprite.IsYYSWFSprite)
					{
						dictionary["isYYSWFSprite"] = true;
						dictionary["swfVersion"] = gameMakerSprite.SWFVersion;
					}
					File.WriteAllText(Path.Combine(text3, text2 + ".json"), JsonSerializer.Serialize(dictionary, s_jsonOpts), Encoding.UTF8);
					for (int num2 = 0; num2 < gameMakerSprite.Textures.Count; num2++)
					{
						GameMakerTexturePageItem gameMakerTexturePageItem2 = gameMakerSprite.Textures[num2]?.Texture;
						if (gameMakerTexturePageItem2 != null)
						{
							try
							{
								worker.ExportAsPNG(gameMakerTexturePageItem2, Path.Combine(text3, $"{text}_{num2}.png"));
							}
							catch
							{
							}
						}
					}
				}
				catch
				{
				}
			});
		}
		finally
		{
			if (worker != null)
			{
				((IDisposable)worker).Dispose();
			}
		}
	}

	public static void ExportBackgrounds(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		List<GameMakerBackground> items = (data.IsGameMaker2() ? data.Backgrounds.Where((GameMakerBackground bg) => bg.GMS2TileWidth == 0 && bg.GMS2TileHeight == 0).ToList() : data.Backgrounds.ToList());
		if (items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Backgrounds");
		TextureWorker worker = new TextureWorker();
		try
		{
			Parallel.ForEach(items, delegate(GameMakerBackground bg)
			{
				if (bg == null || bg.Name?.Content == null || (filter != null && !filter.Contains(bg.Name.Content)))
				{
					return;
				}
				try
				{
					string text = SafeName(bg.Name.Content);
					string text2 = Path.Combine(dir, text);
					Directory.CreateDirectory(text2);
					if (bg.Texture != null)
					{
						worker.ExportAsPNG(bg.Texture, Path.Combine(text2, text + ".png"));
					}
					Dictionary<string, object> dictionary = new Dictionary<string, object>
					{
						["name"] = bg.Name?.Content ?? "",
						["transparent"] = bg.Transparent,
						["smooth"] = bg.Smooth,
						["preload"] = bg.Preload
					};
					if (data.IsGameMaker2())
					{
						dictionary["gms2UnknownAlways2"] = bg.GMS2UnknownAlways2;
					}
					File.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(dictionary, s_jsonOpts), Encoding.UTF8);
				}
				catch
				{
				}
			});
		}
		finally
		{
			if (worker != null)
			{
				((IDisposable)worker).Dispose();
			}
		}
	}

	public static void ExportFonts(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		if (data.Fonts == null || data.Fonts.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Fonts");
		TextureWorker worker = new TextureWorker();
		try
		{
			Parallel.ForEach(data.Fonts.ToList(), delegate(GameMakerFont font)
			{
				if (font == null || font.Name?.Content == null || (filter != null && !filter.Contains(font.Name.Content)))
				{
					return;
				}
				try
				{
					string path = SafeName(font.Name.Content);
					string text = Path.Combine(dir, path);
					Directory.CreateDirectory(text);
					if (font.Texture != null)
					{
						worker.ExportAsPNG(font.Texture, Path.Combine(text, "texture.png"));
					}
					using FileStream utf8Json = new FileStream(Path.Combine(text, "font.json"), FileMode.Create);
					using Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
					{
						Indented = true
					});
					utf8JsonWriter.WriteStartObject();
					utf8JsonWriter.WriteString("name", font.Name?.Content ?? "");
					utf8JsonWriter.WriteString("displayName", font.DisplayName?.Content ?? "");
					utf8JsonWriter.WriteNumber("emSize", font.EmSize);
					utf8JsonWriter.WriteBoolean("bold", font.Bold);
					utf8JsonWriter.WriteBoolean("italic", font.Italic);
					utf8JsonWriter.WriteNumber("rangeStart", font.RangeStart);
					utf8JsonWriter.WriteNumber("rangeEnd", font.RangeEnd);
					utf8JsonWriter.WriteNumber("charset", font.Charset);
					utf8JsonWriter.WriteNumber("antiAliasing", font.AntiAliasing);
					utf8JsonWriter.WriteNumber("scaleX", font.ScaleX);
					utf8JsonWriter.WriteNumber("scaleY", font.ScaleY);
					utf8JsonWriter.WriteBoolean("emSizeIsFloat", font.EmSizeIsFloat);
					if (data.GeneralInfo?.BytecodeVersion >= 17)
					{
						utf8JsonWriter.WriteNumber("ascenderOffset", font.AscenderOffset);
					}
					if (data.IsVersionAtLeast(2022u, 2u))
					{
						utf8JsonWriter.WriteNumber("ascender", font.Ascender);
					}
					if (data.IsVersionAtLeast(2023u, 2u))
					{
						utf8JsonWriter.WriteNumber("sdfSpread", font.SDFSpread);
					}
					if (data.IsVersionAtLeast(2023u, 6u))
					{
						utf8JsonWriter.WriteNumber("lineHeight", font.LineHeight);
					}
					utf8JsonWriter.WritePropertyName("glyphs");
					utf8JsonWriter.WriteStartArray();
					foreach (GameMakerFont.Glyph current in font.Glyphs)
					{
						utf8JsonWriter.WriteStartObject();
						utf8JsonWriter.WriteNumber("character", current.Character);
						utf8JsonWriter.WriteNumber("sourceX", current.SourceX);
						utf8JsonWriter.WriteNumber("sourceY", current.SourceY);
						utf8JsonWriter.WriteNumber("sourceWidth", current.SourceWidth);
						utf8JsonWriter.WriteNumber("sourceHeight", current.SourceHeight);
						utf8JsonWriter.WriteNumber("shift", current.Shift);
						utf8JsonWriter.WriteNumber("offset", current.Offset);
						GameMakerSimpleListShort<GameMakerFont.Glyph.GlyphKerning> kerning = current.Kerning;
						if (kerning != null && kerning.Count > 0)
						{
							utf8JsonWriter.WritePropertyName("kerning");
							utf8JsonWriter.WriteStartArray();
							foreach (GameMakerFont.Glyph.GlyphKerning current2 in current.Kerning)
							{
								utf8JsonWriter.WriteStartObject();
								utf8JsonWriter.WriteNumber("character", current2.Character);
								utf8JsonWriter.WriteNumber("shiftModifier", current2.ShiftModifier);
								utf8JsonWriter.WriteEndObject();
							}
							utf8JsonWriter.WriteEndArray();
						}
						utf8JsonWriter.WriteEndObject();
					}
					utf8JsonWriter.WriteEndArray();
					utf8JsonWriter.WriteEndObject();
				}
				catch
				{
				}
			});
		}
		finally
		{
			if (worker != null)
			{
				((IDisposable)worker).Dispose();
			}
		}
	}

	public static void ExportSounds(GameMakerData data, string outputDir, string dataFilePath, HashSet<string>? filter = null)
	{
		if (data.Sounds == null || data.Sounds.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Sounds");
		string dataDir = Path.GetDirectoryName(dataFilePath) ?? "";
		Dictionary<int, byte[]> audioGroupData = new Dictionary<int, byte[]>();
		foreach (GameMakerSound sound in data.Sounds)
		{
			if (sound == null)
			{
				continue;
			}
			int gid = sound.GroupID;
			if (!audioGroupData.ContainsKey(gid))
			{
				if (gid == 0 || data.AudioGroups == null || gid >= data.AudioGroups.Count)
				{
					audioGroupData[gid] = null;
				}
				else if (data.AudioGroups[gid]?.Name?.Content == null)
				{
					audioGroupData[gid] = null;
				}
				else if (!File.Exists(ResourceAssetUtil.ResolveAudioGroupPath(data, dataDir, gid)))
				{
					audioGroupData[gid] = null;
				}
				else
				{
					audioGroupData[gid] = null;
				}
			}
		}
		Parallel.ForEach(data.Sounds.ToList(), delegate(GameMakerSound gameMakerSound)
		{
			if (gameMakerSound == null || gameMakerSound.Name?.Content == null || (filter != null && !filter.Contains(gameMakerSound.Name.Content)))
			{
				return;
			}
			try
			{
				string text = SafeName(gameMakerSound.Name.Content);
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				if ((gameMakerSound.GroupID > 0 || gameMakerSound.AudioFile == null || gameMakerSound.AudioFile.Data == null || gameMakerSound.AudioFile.Data.Length == 0 || gameMakerSound.AudioID < 0) && gameMakerSound.AudioFile != null && gameMakerSound.AudioFile.Data != null && gameMakerSound.AudioFile.Data.Length != 0)
				{
					string text3 = gameMakerSound.Type?.Content ?? ".ogg";
					if (!text3.StartsWith('.'))
					{
						text3 = "." + text3;
					}
					File.WriteAllBytes(Path.Combine(text2, text + text3), gameMakerSound.AudioFile.Data);
				}
				else if (gameMakerSound.GroupID > 0 && data.AudioGroups != null && gameMakerSound.GroupID < data.AudioGroups.Count)
				{
					string path = ResourceAssetUtil.ResolveAudioGroupPath(data, dataDir, gameMakerSound.GroupID);
					if (File.Exists(path))
					{
						try
						{
							using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read);
							using GameMakerData gameMakerData = GameMakerIO.Read(stream);
							if (gameMakerData.EmbeddedAudio != null && gameMakerSound.AudioID >= 0 && gameMakerSound.AudioID < gameMakerData.EmbeddedAudio.Count)
							{
								GameMakerEmbeddedAudio gameMakerEmbeddedAudio = gameMakerData.EmbeddedAudio[gameMakerSound.AudioID];
								if (gameMakerEmbeddedAudio != null && gameMakerEmbeddedAudio.Data?.Length > 0)
								{
									string text4 = gameMakerSound.Type?.Content ?? ".ogg";
									if (!text4.StartsWith('.'))
									{
										text4 = "." + text4;
									}
									File.WriteAllBytes(Path.Combine(text2, text + text4), gameMakerEmbeddedAudio.Data);
								}
							}
						}
						catch
						{
						}
					}
				}
				using FileStream utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
				{
					Indented = true
				});
				utf8JsonWriter.WriteStartObject();
				utf8JsonWriter.WriteString("name", gameMakerSound.Name.Content);
				utf8JsonWriter.WriteString("type", gameMakerSound.Type?.Content ?? "");
				utf8JsonWriter.WriteString("file", gameMakerSound.File?.Content ?? "");
				utf8JsonWriter.WriteNumber("effects", gameMakerSound.Effects);
				utf8JsonWriter.WriteNumber("volume", gameMakerSound.Volume);
				utf8JsonWriter.WriteNumber("pitch", gameMakerSound.Pitch);
				utf8JsonWriter.WriteBoolean("preload", gameMakerSound.Preload);
				utf8JsonWriter.WriteNumber("audioID", gameMakerSound.AudioID);
				utf8JsonWriter.WriteNumber("groupID", gameMakerSound.GroupID);
				utf8JsonWriter.WriteNumber("flags", (uint)gameMakerSound.Flags);
				if (data.IsNonLTSVersionAtLeast(2024u, 6u))
				{
					utf8JsonWriter.WriteNumber("audioLength", gameMakerSound.AudioLength);
				}
				if (gameMakerSound.AudioGroup?.Name?.Content != null)
				{
					utf8JsonWriter.WriteString("audioGroupName", gameMakerSound.AudioGroup.Name.Content);
				}
				utf8JsonWriter.WriteEndObject();
			}
			catch
			{
			}
		});
	}

	public static void ExportEmbeddedAudio(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		if (data.EmbeddedAudio == null || data.EmbeddedAudio.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "EmbeddedAudio");
		Parallel.For(0, data.EmbeddedAudio.Count, delegate(int i)
		{
			string text = $"audio_{i:D4}";
			if (filter != null && !filter.Contains(text))
			{
				return;
			}
			try
			{
				GameMakerEmbeddedAudio gameMakerEmbeddedAudio = data.EmbeddedAudio[i];
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				File.WriteAllBytes(Path.Combine(text2, text + ".bin"), gameMakerEmbeddedAudio?.Data ?? Array.Empty<byte>());
				File.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
				{
					["index"] = i,
					["bytes"] = (gameMakerEmbeddedAudio?.Data?.Length).GetValueOrDefault()
				}, s_jsonOpts), path: Path.Combine(text2, text + ".json"), encoding: Encoding.UTF8);
			}
			catch
			{
			}
		});
	}

	public static void ExportPaths(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		List<GameMakerPath> items = data.Paths?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Paths");
		Parallel.ForEach(items, delegate(GameMakerPath path)
		{
			if (path == null || path.Name?.Content == null || (filter != null && !filter.Contains(path.Name.Content)))
			{
				return;
			}
			try
			{
				string text = SafeName(path.Name.Content);
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				using FileStream utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
				{
					Indented = true
				});
				utf8JsonWriter.WriteStartObject();
				utf8JsonWriter.WriteString("name", path.Name.Content);
				utf8JsonWriter.WriteBoolean("isSmooth", path.IsSmooth);
				utf8JsonWriter.WriteBoolean("isClosed", path.IsClosed);
				utf8JsonWriter.WriteNumber("precision", (int)path.Precision);
				utf8JsonWriter.WriteStartArray("points");
				foreach (GameMakerPath.PathPoint current in path.Points)
				{
					utf8JsonWriter.WriteStartObject();
					utf8JsonWriter.WriteNumber("x", current.X);
					utf8JsonWriter.WriteNumber("y", current.Y);
					utf8JsonWriter.WriteNumber("speed", current.Speed);
					utf8JsonWriter.WriteEndObject();
				}
				utf8JsonWriter.WriteEndArray();
				utf8JsonWriter.WriteEndObject();
			}
			catch
			{
			}
		});
	}

	public static void ExportTilesets(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		if (!data.IsGameMaker2())
		{
			return;
		}
		List<GameMakerBackground> items = data.Backgrounds.Where((GameMakerBackground bg) => bg.GMS2TileWidth != 0 || bg.GMS2TileHeight != 0).ToList();
		if (items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Tilesets");
		bool exportAllTilesets = filter == null || filter.Contains("Tilesets");
		List<GameMakerBackground> selectedItems = items.Where((GameMakerBackground ts) => ts != null && ts.Name?.Content != null && (exportAllTilesets || filter.Contains(ts.Name.Content))).ToList();
		if (selectedItems.Count == 0)
		{
			return;
		}
		TextureWorker worker = new TextureWorker();
		try
		{
			List<GameMakerBackground> noTextureItems = selectedItems.Where((GameMakerBackground ts) => ts.Texture == null).ToList();
			Parallel.ForEach((from ts in selectedItems
				where ts.Texture?.TexturePage != null
				group ts by ts.Texture.TexturePage).ToList(), delegate(IGrouping<GameMakerEmbeddedTexture, GameMakerBackground> group)
			{
				try
				{
					MagickImage embeddedTexture = worker.GetEmbeddedTexture(group.Key);
					foreach (GameMakerBackground current in group)
					{
						if (current != null && current.Name?.Content != null && current.Texture != null)
						{
							string text = SafeName(current.Name.Content);
							using IMagickImage<byte> image = ExportTexturePageItemFromCachedImage(embeddedTexture, current.Texture, text);
							TextureWorker.SaveImageToFile(image, Path.Combine(dir, text + ".png"));
							WriteTilesetMetadata(data, dir, current, text);
						}
					}
				}
				catch
				{
				}
			});
			Parallel.ForEach(noTextureItems, delegate(GameMakerBackground ts)
			{
				if (ts == null || ts.Name?.Content == null)
				{
					return;
				}
				try
				{
					string name = SafeName(ts.Name.Content);
					WriteTilesetMetadata(data, dir, ts, name);
				}
				catch
				{
				}
			});
		}
		finally
		{
			if (worker != null)
			{
				((IDisposable)worker).Dispose();
			}
		}
	}

	private static void WriteTilesetMetadata(GameMakerData data, string dir, GameMakerBackground ts, string name)
	{
		Dictionary<string, object> meta = new Dictionary<string, object>
		{
			["name"] = ts.Name?.Content ?? "",
			["transparent"] = ts.Transparent,
			["smooth"] = ts.Smooth,
			["preload"] = ts.Preload,
			["gms2UnknownAlways2"] = ts.GMS2UnknownAlways2,
			["gms2TileWidth"] = ts.GMS2TileWidth,
			["gms2TileHeight"] = ts.GMS2TileHeight,
			["gms2OutputBorderX"] = ts.GMS2OutputBorderX,
			["gms2OutputBorderY"] = ts.GMS2OutputBorderY,
			["gms2TileColumns"] = ts.GMS2TileColumns,
			["gms2ItemsPerTileCount"] = ts.GMS2ItemsPerTileCount,
			["gms2TileCount"] = ts.GMS2TileCount,
			["gms2ExportedSpriteIndex"] = ts.GMS2ExportedSpriteIndex,
			["gms2FrameLength"] = ts.GMS2FrameLength
		};
		if (data.IsVersionAtLeast(2024u, 14u, 1u))
		{
			meta["gms2TileSeparationX"] = ts.GMS2TileSeparationX;
			meta["gms2TileSeparationY"] = ts.GMS2TileSeparationY;
		}
		GameMakerObservableList<GameMakerBackground.TileID> gMS2TileIds = ts.GMS2TileIds;
		if (gMS2TileIds != null && gMS2TileIds.Count > 0)
		{
			List<uint> ids = new List<uint>();
			foreach (GameMakerBackground.TileID tid in ts.GMS2TileIds)
			{
				ids.Add(tid.ID);
			}
			meta["gms2TileIds"] = ids;
		}
		File.WriteAllText(Path.Combine(dir, name + ".json"), JsonSerializer.Serialize(meta, s_jsonOpts), Encoding.UTF8);
	}

	private static IMagickImage<byte> ExportTexturePageItemFromCachedImage(MagickImage embeddedImage, GameMakerTexturePageItem texPageItem, string imageName)
	{
		int exportWidth = texPageItem.BoundingWidth;
		int exportHeight = texPageItem.BoundingHeight;
		if (texPageItem.TargetWidth > exportWidth || texPageItem.TargetHeight > exportHeight)
		{
			throw new InvalidDataException(imageName + "'s texture is larger than its bounding box!");
		}
		IMagickImage<byte> image;
		lock (embeddedImage)
		{
			image = embeddedImage.CloneArea(texPageItem.SourceX, texPageItem.SourceY, texPageItem.SourceWidth, texPageItem.SourceHeight);
		}
		if (texPageItem.SourceWidth != texPageItem.TargetWidth || texPageItem.SourceHeight != texPageItem.TargetHeight)
		{
			uint resizeWidth = texPageItem.TargetWidth;
			uint resizeHeight = texPageItem.TargetHeight;
			if (image.Width != resizeWidth || image.Height != resizeHeight)
			{
				image.InterpolativeResize(resizeWidth, resizeHeight, PixelInterpolateMethod.Bilinear);
			}
		}
		image.Strip();
		return image;
	}

	public static void ExportShaders(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		if (data.Shaders == null || data.Shaders.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Shaders");
		Parallel.ForEach(data.Shaders.ToList(), delegate(GameMakerShader shader)
		{
			if (shader == null || shader.Name?.Content == null || (filter != null && !filter.Contains(shader.Name.Content)))
			{
				return;
			}
			try
			{
				string text = SafeName(shader.Name.Content);
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				File.WriteAllText(Path.Combine(text2, "Type.txt"), shader.Type.ToString(), Encoding.UTF8);
				if (shader.GLSL_ES_Fragment != null)
				{
					File.WriteAllText(Path.Combine(text2, "GLSL_ES_Fragment.txt"), shader.GLSL_ES_Fragment.Content ?? "", Encoding.UTF8);
				}
				if (shader.GLSL_ES_Vertex != null)
				{
					File.WriteAllText(Path.Combine(text2, "GLSL_ES_Vertex.txt"), shader.GLSL_ES_Vertex.Content ?? "", Encoding.UTF8);
				}
				if (shader.GLSL_Fragment != null)
				{
					File.WriteAllText(Path.Combine(text2, "GLSL_Fragment.txt"), shader.GLSL_Fragment.Content ?? "", Encoding.UTF8);
				}
				if (shader.GLSL_Vertex != null)
				{
					File.WriteAllText(Path.Combine(text2, "GLSL_Vertex.txt"), shader.GLSL_Vertex.Content ?? "", Encoding.UTF8);
				}
				if (shader.HLSL9_Fragment != null)
				{
					File.WriteAllText(Path.Combine(text2, "HLSL9_Fragment.txt"), shader.HLSL9_Fragment.Content ?? "", Encoding.UTF8);
				}
				if (shader.HLSL9_Vertex != null)
				{
					File.WriteAllText(Path.Combine(text2, "HLSL9_Vertex.txt"), shader.HLSL9_Vertex.Content ?? "", Encoding.UTF8);
				}
				GameMakerShader.GameMakerRawShaderData hLSL11_VertexData = shader.HLSL11_VertexData;
				if (hLSL11_VertexData != null && hLSL11_VertexData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "HLSL11_VertexData.bin"), shader.HLSL11_VertexData.Data);
				}
				GameMakerShader.GameMakerRawShaderData hLSL11_PixelData = shader.HLSL11_PixelData;
				if (hLSL11_PixelData != null && hLSL11_PixelData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "HLSL11_PixelData.bin"), shader.HLSL11_PixelData.Data);
				}
				GameMakerShader.GameMakerRawShaderData pSSL_VertexData = shader.PSSL_VertexData;
				if (pSSL_VertexData != null && pSSL_VertexData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "PSSL_VertexData.bin"), shader.PSSL_VertexData.Data);
				}
				GameMakerShader.GameMakerRawShaderData pSSL_PixelData = shader.PSSL_PixelData;
				if (pSSL_PixelData != null && pSSL_PixelData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "PSSL_PixelData.bin"), shader.PSSL_PixelData.Data);
				}
				GameMakerShader.GameMakerRawShaderData cg_PSVita_VertexData = shader.Cg_PSVita_VertexData;
				if (cg_PSVita_VertexData != null && cg_PSVita_VertexData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "Cg_PSVita_VertexData.bin"), shader.Cg_PSVita_VertexData.Data);
				}
				GameMakerShader.GameMakerRawShaderData cg_PSVita_PixelData = shader.Cg_PSVita_PixelData;
				if (cg_PSVita_PixelData != null && cg_PSVita_PixelData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "Cg_PSVita_PixelData.bin"), shader.Cg_PSVita_PixelData.Data);
				}
				GameMakerShader.GameMakerRawShaderData cg_PS3_VertexData = shader.Cg_PS3_VertexData;
				if (cg_PS3_VertexData != null && cg_PS3_VertexData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "Cg_PS3_VertexData.bin"), shader.Cg_PS3_VertexData.Data);
				}
				GameMakerShader.GameMakerRawShaderData cg_PS3_PixelData = shader.Cg_PS3_PixelData;
				if (cg_PS3_PixelData != null && cg_PS3_PixelData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "Cg_PS3_PixelData.bin"), shader.Cg_PS3_PixelData.Data);
				}
				GameMakerSimpleList<GameMakerShader.VertexShaderAttribute> vertexShaderAttributes = shader.VertexShaderAttributes;
				if (vertexShaderAttributes != null && vertexShaderAttributes.Count > 0)
				{
					StringBuilder stringBuilder = new StringBuilder();
					foreach (GameMakerShader.VertexShaderAttribute current in shader.VertexShaderAttributes)
					{
						if (current != null && current.Name?.Content != null)
						{
							stringBuilder.AppendLine(current.Name.Content);
						}
					}
					File.WriteAllText(Path.Combine(text2, "VertexShaderAttributes.txt"), stringBuilder.ToString(), Encoding.UTF8);
				}
				Dictionary<string, object> value = new Dictionary<string, object>
				{
					["name"] = shader.Name.Content,
					["type"] = shader.Type.ToString()
				};
				File.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(value, s_jsonOpts), Encoding.UTF8);
			}
			catch
			{
			}
		});
	}

	public static void ExportTimelines(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		List<GameMakerTimeline> items = data.Timelines?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Timelines");
		Parallel.ForEach(items, delegate(GameMakerTimeline tl)
		{
			if (tl == null || tl.Name?.Content == null || (filter != null && !filter.Contains(tl.Name.Content)))
			{
				return;
			}
			try
			{
				string text = SafeName(tl.Name.Content);
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				using FileStream utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
				{
					Indented = true
				});
				utf8JsonWriter.WriteStartObject();
				utf8JsonWriter.WriteString("name", tl.Name.Content);
				utf8JsonWriter.WriteStartArray("moments");
				foreach (GameMakerTimeline.GameMakerTimelineMoment current in tl.Moments)
				{
					utf8JsonWriter.WriteStartObject();
					utf8JsonWriter.WriteNumber("step", (int)current.Step);
					GameMakerPointerList<GameMakerGameObject.EventAction> gameMakerPointerList = current.Event;
					if (gameMakerPointerList != null && gameMakerPointerList.Count > 0)
					{
						utf8JsonWriter.WriteStartArray("actions");
						foreach (GameMakerGameObject.EventAction current2 in current.Event)
						{
							utf8JsonWriter.WriteStartObject();
							if (current2.CodeId?.Name?.Content != null)
							{
								utf8JsonWriter.WriteString("codeId", current2.CodeId.Name.Content);
							}
							else
							{
								utf8JsonWriter.WriteNull("codeId");
							}
							utf8JsonWriter.WriteNumber("libId", current2.LibID);
							utf8JsonWriter.WriteNumber("id", current2.ID);
							utf8JsonWriter.WriteNumber("kind", current2.Kind);
							utf8JsonWriter.WriteBoolean("useRelative", current2.UseRelative);
							utf8JsonWriter.WriteBoolean("isQuestion", current2.IsQuestion);
							utf8JsonWriter.WriteBoolean("useApplyTo", current2.UseApplyTo);
							utf8JsonWriter.WriteNumber("exeType", current2.ExeType);
							utf8JsonWriter.WriteString("actionName", current2.ActionName?.Content ?? "");
							utf8JsonWriter.WriteNumber("argumentCount", current2.ArgumentCount);
							utf8JsonWriter.WriteNumber("who", current2.Who);
							utf8JsonWriter.WriteBoolean("relative", current2.Relative);
							utf8JsonWriter.WriteBoolean("isNot", current2.IsNot);
							utf8JsonWriter.WriteEndObject();
						}
						utf8JsonWriter.WriteEndArray();
					}
					utf8JsonWriter.WriteEndObject();
				}
				utf8JsonWriter.WriteEndArray();
				utf8JsonWriter.WriteEndObject();
			}
			catch
			{
			}
		});
	}

	public static void ExportAnimationCurves(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		List<GameMakerAnimationCurve> items = data.AnimationCurves?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "AnimationCurves");
		Parallel.ForEach(items, delegate(GameMakerAnimationCurve curve)
		{
			if (curve == null || curve.Name?.Content == null || (filter != null && !filter.Contains(curve.Name.Content)))
			{
				return;
			}
			try
			{
				string text = SafeName(curve.Name.Content);
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				File.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(ToAnimationCurvePayload(data, curve), s_jsonOpts), Encoding.UTF8);
			}
			catch (Exception ex)
			{
				bool shouldAppend;
				LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(45, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler.AppendLiteral("[ExportAnimationCurves] Failed to export '");
					handler.AppendFormatted(curve.Name.Content);
					handler.AppendLiteral("': ");
					handler.AppendFormatted(ex.Message);
				}
				LogService.Log(ref handler);
			}
		});
	}

	public static void ExportSequences(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		List<GameMakerSequence> items = data.Sequences?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Sequences");
		ProjectContext context = ResourceAssetUtil.CreateProjectContext(data, Path.Combine(dir, ".project_context"));
		foreach (GameMakerSequence sequence in items)
		{
			if (sequence == null || sequence.Name?.Content == null || (filter != null && !filter.Contains(sequence.Name.Content)))
			{
				continue;
			}
			try
			{
				string name = SafeName(sequence.Name.Content);
				string seqDir = Path.Combine(dir, name);
				Directory.CreateDirectory(seqDir);
				SerializableProjectAssetBridge.Export(context, sequence, Path.Combine(seqDir, name + ".json"));
			}
			catch (Exception ex)
			{
				bool shouldAppend;
				LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(39, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler.AppendLiteral("[ExportSequences] Failed to export '");
					handler.AppendFormatted(sequence.Name.Content);
					handler.AppendLiteral("': ");
					handler.AppendFormatted(ex.Message);
				}
				LogService.Log(ref handler);
			}
		}
		try
		{
			Directory.Delete(Path.Combine(dir, ".project_context"), recursive: true);
		}
		catch
		{
		}
	}

	public static void ExportParticleSystems(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		List<GameMakerParticleSystem> items = data.ParticleSystems?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "ParticleSystems");
		Parallel.ForEach(items, delegate(GameMakerParticleSystem ps)
		{
			if (ps != null && ps.Name?.Content != null && (filter == null || filter.Contains(ps.Name.Content)))
			{
				string text = SafeName(ps.Name.Content);
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				File.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(ToParticleSystemPayload(ps), s_jsonOpts), Encoding.UTF8);
			}
		});
	}

	public static void ExportParticleSystemEmitters(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		List<GameMakerParticleSystemEmitter> items = data.ParticleSystemEmitters?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "ParticleSystemEmitters");
		Parallel.ForEach(items, delegate(GameMakerParticleSystemEmitter emitter)
		{
			if (emitter != null && emitter.Name?.Content != null && (filter == null || filter.Contains(emitter.Name.Content)))
			{
				string text = SafeName(emitter.Name.Content);
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				File.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(ToParticleEmitterPayload(emitter), s_jsonOpts), Encoding.UTF8);
			}
		});
	}

	private static Dictionary<string, object?> ToParticleSystemPayload(GameMakerParticleSystem ps)
	{
		return new Dictionary<string, object>
		{
			["name"] = ps.Name?.Content ?? "",
			["originX"] = ps.OriginX,
			["originY"] = ps.OriginY,
			["drawOrder"] = (int)ps.DrawOrder,
			["globalSpaceParticles"] = ps.GlobalSpaceParticles,
			["emitters"] = (from n in ps.Emitters?.Select((GameMakerResourceById<GameMakerParticleSystemEmitter, GameMakerChunkPSEM> e) => e?.Resource?.Name?.Content ?? "")
				where !string.IsNullOrEmpty(n)
				select n).ToArray() ?? Array.Empty<string>()
		};
	}

	private static Dictionary<string, object?> ToParticleEmitterPayload(GameMakerParticleSystemEmitter e)
	{
		Dictionary<string, object> payload = new Dictionary<string, object>
		{
			["name"] = e.Name?.Content ?? "",
			["sprite"] = e.Sprite?.Name?.Content ?? "",
			["spawnOnDeath"] = e.SpawnOnDeath?.Name?.Content ?? "",
			["spawnOnUpdate"] = e.SpawnOnUpdate?.Name?.Content ?? ""
		};
		PropertyInfo[] properties = typeof(GameMakerParticleSystemEmitter).GetProperties();
		foreach (PropertyInfo prop in properties)
		{
			if (!prop.CanRead || !prop.CanWrite)
			{
				continue;
			}
			bool flag;
			switch (prop.Name)
			{
			case "SizeIncrease":
			case "SpawnOnDeath":
			case "SizeMax":
			case "SizeMin":
			case "Name":
			case "Sprite":
			case "SpawnOnUpdate":
			case "SizeWiggle":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				Type type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
				if (type.IsEnum)
				{
					payload[prop.Name] = Convert.ToInt32(prop.GetValue(e));
				}
				else if (type == typeof(bool) || type == typeof(int) || type == typeof(uint) || type == typeof(float))
				{
					payload[prop.Name] = prop.GetValue(e);
				}
			}
		}
		return payload;
	}

	private static Dictionary<string, object?> ToAnimationCurvePayload(GameMakerData data, GameMakerAnimationCurve curve)
	{
		return new Dictionary<string, object>
		{
			["name"] = curve.Name?.Content ?? "",
			["graphType"] = (uint)curve.GraphType,
			["channels"] = (from channel in curve.Channels?.Where((GameMakerAnimationCurve.Channel channel) => channel != null)
				select new Dictionary<string, object>
				{
					["name"] = channel.Name?.Content ?? "",
					["curve"] = (uint)channel.Curve,
					["iterations"] = channel.Iterations,
					["points"] = (from point in channel.Points?.Where((GameMakerAnimationCurve.Channel.Point point) => point != null)
						select ToAnimationCurvePointPayload(data, point)).ToArray() ?? Array.Empty<Dictionary<string, object>>()
				}).ToArray() ?? Array.Empty<Dictionary<string, object>>()
		};
	}

	private static Dictionary<string, object?> ToAnimationCurvePointPayload(GameMakerData data, GameMakerAnimationCurve.Channel.Point point)
	{
		Dictionary<string, object> payload = new Dictionary<string, object>
		{
			["x"] = point.X,
			["value"] = point.Value
		};
		if (data.IsVersionAtLeast(2u, 3u, 1u))
		{
			payload["bezierX0"] = point.BezierX0;
			payload["bezierY0"] = point.BezierY0;
			payload["bezierX1"] = point.BezierX1;
			payload["bezierY1"] = point.BezierY1;
		}
		return payload;
	}

	public static void ExportGameObjects(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		List<GameMakerGameObject> items = data.GameObjects?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "GameObjects");
		Dictionary<GameMakerGameObject, int> objectIndices = BuildReferenceIndex(items);
		Parallel.ForEach(items, delegate(GameMakerGameObject obj)
		{
			if (obj == null || obj.Name?.Content == null || (filter != null && !filter.Contains(obj.Name.Content)))
			{
				return;
			}
			try
			{
				string value = SafeName(obj.Name.Content);
				int value3;
				int value2 = (objectIndices.TryGetValue(obj, out value3) ? value3 : (-1));
				string path = $"{value}__idx{value2:D4}";
				string text = Path.Combine(dir, path);
				Directory.CreateDirectory(text);
				using FileStream utf8Json = new FileStream(Path.Combine(text, "object.json"), FileMode.Create);
				using Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
				{
					Indented = true
				});
				utf8JsonWriter.WriteStartObject();
				utf8JsonWriter.WriteString("name", obj.Name.Content);
				utf8JsonWriter.WriteNumber("index", value2);
				utf8JsonWriter.WriteString("sprite", obj.Sprite?.Name?.Content ?? "");
				utf8JsonWriter.WriteBoolean("visible", obj.Visible);
				utf8JsonWriter.WriteBoolean("solid", obj.Solid);
				utf8JsonWriter.WriteNumber("depth", obj.Depth);
				utf8JsonWriter.WriteBoolean("persistent", obj.Persistent);
				utf8JsonWriter.WriteString("parent", obj.ParentId?.Name?.Content ?? "");
				utf8JsonWriter.WriteString("textureMask", obj.TextureMaskId?.Name?.Content ?? "");
				if (data.IsVersionAtLeast(2022u, 5u))
				{
					utf8JsonWriter.WriteBoolean("managed", obj.Managed);
				}
				utf8JsonWriter.WriteBoolean("usesPhysics", obj.UsesPhysics);
				utf8JsonWriter.WriteBoolean("isSensor", obj.IsSensor);
				utf8JsonWriter.WriteNumber("collisionShape", (int)obj.CollisionShape);
				utf8JsonWriter.WriteNumber("density", obj.Density);
				utf8JsonWriter.WriteNumber("restitution", obj.Restitution);
				utf8JsonWriter.WriteNumber("group", obj.Group);
				utf8JsonWriter.WriteNumber("linearDamping", obj.LinearDamping);
				utf8JsonWriter.WriteNumber("angularDamping", obj.AngularDamping);
				utf8JsonWriter.WriteNumber("friction", obj.Friction);
				utf8JsonWriter.WriteBoolean("awake", obj.Awake);
				utf8JsonWriter.WriteBoolean("kinematic", obj.Kinematic);
				utf8JsonWriter.WriteStartArray("events");
				for (int i = 0; i < obj.Events.Count; i++)
				{
					foreach (GameMakerGameObject.Event current in obj.Events[i])
					{
						utf8JsonWriter.WriteStartObject();
						utf8JsonWriter.WriteNumber("eventType", i);
						utf8JsonWriter.WriteNumber("eventSubtype", current.EventSubtype);
						if (i == 4 && data.GameObjects != null && current.EventSubtype < (uint)data.GameObjects.Count)
						{
							utf8JsonWriter.WriteString("collisionObjectName", data.GameObjects[(int)current.EventSubtype]?.Name?.Content ?? "");
						}
						utf8JsonWriter.WriteStartArray("actions");
						foreach (GameMakerGameObject.EventAction current2 in current.Actions)
						{
							utf8JsonWriter.WriteStartObject();
							utf8JsonWriter.WriteString("codeId", current2.CodeId?.Name?.Content ?? "");
							utf8JsonWriter.WriteNumber("libId", current2.LibID);
							utf8JsonWriter.WriteNumber("id", current2.ID);
							utf8JsonWriter.WriteNumber("kind", current2.Kind);
							utf8JsonWriter.WriteBoolean("useRelative", current2.UseRelative);
							utf8JsonWriter.WriteBoolean("isQuestion", current2.IsQuestion);
							utf8JsonWriter.WriteBoolean("useApplyTo", current2.UseApplyTo);
							utf8JsonWriter.WriteNumber("exeType", current2.ExeType);
							utf8JsonWriter.WriteString("actionName", current2.ActionName?.Content ?? "");
							utf8JsonWriter.WriteNumber("argumentCount", current2.ArgumentCount);
							utf8JsonWriter.WriteNumber("who", current2.Who);
							utf8JsonWriter.WriteBoolean("relative", current2.Relative);
							utf8JsonWriter.WriteBoolean("isNot", current2.IsNot);
							utf8JsonWriter.WriteEndObject();
						}
						utf8JsonWriter.WriteEndArray();
						utf8JsonWriter.WriteEndObject();
					}
				}
				utf8JsonWriter.WriteEndArray();
				GameMakerObservableList<GameMakerGameObject.GameMakerPhysicsVertex> physicsVertices = obj.PhysicsVertices;
				if (physicsVertices != null && physicsVertices.Count > 0)
				{
					utf8JsonWriter.WriteStartArray("physicsVertices");
					foreach (GameMakerGameObject.GameMakerPhysicsVertex current3 in obj.PhysicsVertices)
					{
						utf8JsonWriter.WriteStartObject();
						utf8JsonWriter.WriteNumber("x", current3.X);
						utf8JsonWriter.WriteNumber("y", current3.Y);
						utf8JsonWriter.WriteEndObject();
					}
					utf8JsonWriter.WriteEndArray();
				}
				utf8JsonWriter.WriteEndObject();
			}
			catch
			{
			}
		});
	}

	public static void ExportRooms(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		if (data.Rooms == null || data.Rooms.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Rooms");
		Parallel.ForEach(data.Rooms.ToList(), delegate(GameMakerRoom room)
		{
			if (room == null || room.Name?.Content == null || (filter != null && !filter.Contains(room.Name.Content)))
			{
				return;
			}
			try
			{
				ExportSingleRoom(data, room, dir);
			}
			catch (Exception ex)
			{
				bool shouldAppend;
				LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(40, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler.AppendLiteral("[ExportRooms] Failed to export room '");
					handler.AppendFormatted(room.Name.Content);
					handler.AppendLiteral("': ");
					handler.AppendFormatted(ex.Message);
				}
				LogService.Log(ref handler);
			}
		});
	}

	private static void ExportSingleRoom(GameMakerData data, GameMakerRoom room, string dir)
	{
		string name = SafeName(room.Name?.Content ?? string.Empty);
		string text = Path.Combine(dir, name);
		Directory.CreateDirectory(text);
		using FileStream s = new FileStream(Path.Combine(text, "room.json"), FileMode.Create);
		using Utf8JsonWriter w = new Utf8JsonWriter(s, new JsonWriterOptions
		{
			Indented = true
		});
		w.WriteStartObject();
		w.WriteString("name", room.Name?.Content ?? string.Empty);
		w.WriteString("caption", room.Caption?.Content ?? "");
		w.WriteNumber("width", room.Width);
		w.WriteNumber("height", room.Height);
		w.WriteNumber("speed", room.Speed);
		w.WriteBoolean("persistent", room.Persistent);
		w.WriteNumber("backgroundColor", room.BackgroundColor);
		w.WriteBoolean("drawBackgroundColor", room.DrawBackgroundColor);
		w.WriteString("creationCodeId", room.CreationCodeId?.Name?.Content ?? "");
		w.WriteNumber("flags", (int)room.Flags);
		w.WriteBoolean("world", room.World);
		w.WriteNumber("top", room.Top);
		w.WriteNumber("left", room.Left);
		w.WriteNumber("right", room.Right);
		w.WriteNumber("bottom", room.Bottom);
		w.WriteNumber("gravityX", room.GravityX);
		w.WriteNumber("gravityY", room.GravityY);
		w.WriteNumber("metersPerPixel", room.MetersPerPixel);
		w.WriteNumber("gridWidth", (float)room.GridWidth);
		w.WriteNumber("gridHeight", (float)room.GridHeight);
		w.WriteNumber("gridThicknessPx", (float)room.GridThicknessPx);
		w.WriteStartArray("backgrounds");
		foreach (GameMakerRoom.Background bg in room.Backgrounds)
		{
			w.WriteStartObject();
			w.WriteBoolean("enabled", bg.Enabled);
			w.WriteBoolean("foreground", bg.Foreground);
			w.WriteString("backgroundDefinition", bg.BackgroundDefinition?.Name?.Content ?? "");
			w.WriteNumber("x", bg.X);
			w.WriteNumber("y", bg.Y);
			w.WriteBoolean("tiledHorizontally", bg.TiledHorizontally);
			w.WriteBoolean("tiledVertically", bg.TiledVertically);
			w.WriteNumber("speedX", bg.SpeedX);
			w.WriteNumber("speedY", bg.SpeedY);
			w.WriteBoolean("stretch", bg.Stretch);
			w.WriteEndObject();
		}
		w.WriteEndArray();
		w.WriteStartArray("views");
		foreach (GameMakerRoom.View v in room.Views)
		{
			w.WriteStartObject();
			w.WriteBoolean("enabled", v.Enabled);
			w.WriteNumber("viewX", v.ViewX);
			w.WriteNumber("viewY", v.ViewY);
			w.WriteNumber("viewWidth", v.ViewWidth);
			w.WriteNumber("viewHeight", v.ViewHeight);
			w.WriteNumber("portX", v.PortX);
			w.WriteNumber("portY", v.PortY);
			w.WriteNumber("portWidth", v.PortWidth);
			w.WriteNumber("portHeight", v.PortHeight);
			w.WriteNumber("borderX", v.BorderX);
			w.WriteNumber("borderY", v.BorderY);
			w.WriteNumber("speedX", v.SpeedX);
			w.WriteNumber("speedY", v.SpeedY);
			w.WriteString("objectId", v.ObjectId?.Name?.Content ?? "");
			w.WriteEndObject();
		}
		w.WriteEndArray();
		w.WriteStartArray("gameObjects");
		foreach (GameMakerRoom.GameObject o in room.GameObjects)
		{
			w.WriteStartObject();
			w.WriteNumber("x", o.X);
			w.WriteNumber("y", o.Y);
			w.WriteString("objectDefinition", o.ObjectDefinition?.Name?.Content ?? "");
			w.WriteNumber("instanceID", o.InstanceID);
			w.WriteString("creationCode", o.CreationCode?.Name?.Content ?? "");
			w.WriteNumber("scaleX", o.ScaleX);
			w.WriteNumber("scaleY", o.ScaleY);
			w.WriteNumber("color", o.Color);
			w.WriteNumber("rotation", o.Rotation);
			w.WriteString("preCreateCode", o.PreCreateCode?.Name?.Content ?? "");
			if (data.IsVersionAtLeast(2u, 2u, 2u, 302u))
			{
				w.WriteNumber("imageSpeed", o.ImageSpeed);
				w.WriteNumber("imageIndex", o.ImageIndex);
			}
			w.WriteEndObject();
		}
		w.WriteEndArray();
		w.WriteStartArray("tiles");
		foreach (GameMakerRoom.Tile t in room.Tiles)
		{
			w.WriteStartObject();
			w.WriteNumber("x", t.X);
			w.WriteNumber("y", t.Y);
			w.WriteBoolean("spriteMode", t.spriteMode);
			if (t.spriteMode)
			{
				w.WriteString("spriteDefinition", t.SpriteDefinition?.Name?.Content ?? "");
			}
			else
			{
				w.WriteString("backgroundDefinition", t.BackgroundDefinition?.Name?.Content ?? "");
			}
			w.WriteNumber("sourceX", t.SourceX);
			w.WriteNumber("sourceY", t.SourceY);
			w.WriteNumber("width", t.Width);
			w.WriteNumber("height", t.Height);
			w.WriteNumber("tileDepth", t.TileDepth);
			w.WriteNumber("instanceID", t.InstanceID);
			w.WriteNumber("scaleX", t.ScaleX);
			w.WriteNumber("scaleY", t.ScaleY);
			w.WriteNumber("color", t.Color);
			w.WriteEndObject();
		}
		w.WriteEndArray();
		if (data.IsGameMaker2())
		{
			GameMakerPointerList<GameMakerRoom.Layer> layers = room.Layers;
			if (layers != null && layers.Count > 0)
			{
				w.WriteStartArray("layers");
				foreach (GameMakerRoom.Layer layer in room.Layers)
				{
					WriteRoomLayer(data, w, layer);
				}
				w.WriteEndArray();
			}
		}
		if (data.IsVersionAtLeast(2u, 3u))
		{
			GameMakerSimpleList<GameMakerResourceById<GameMakerSequence, GameMakerChunkSEQN>> sequences = room.Sequences;
			if (sequences != null && sequences.Count > 0)
			{
				w.WriteStartArray("sequences");
				foreach (GameMakerResourceById<GameMakerSequence, GameMakerChunkSEQN> sequence in room.Sequences)
				{
					w.WriteStringValue(sequence?.Resource?.Name?.Content ?? "");
				}
				w.WriteEndArray();
			}
		}
		if (data.IsVersionAtLeast(2024u, 13u))
		{
			GameMakerRoom.InstanceIDList instanceCreationOrderIDs = room.InstanceCreationOrderIDs;
			if (instanceCreationOrderIDs != null && instanceCreationOrderIDs.InstanceIDs?.Count > 0)
			{
				w.WriteStartArray("instanceCreationOrderIDs");
				foreach (uint id in room.InstanceCreationOrderIDs.InstanceIDs)
				{
					w.WriteNumberValue(id);
				}
				w.WriteEndArray();
			}
		}
		w.WriteEndObject();
	}

	private static void WriteRoomLayer(GameMakerData data, Utf8JsonWriter w, GameMakerRoom.Layer layer)
	{
		w.WriteStartObject();
		w.WriteString("layerName", layer.LayerName?.Content ?? "");
		w.WriteNumber("layerId", layer.LayerId);
		w.WriteNumber("layerType", (int)layer.LayerType);
		w.WriteNumber("layerDepth", layer.LayerDepth);
		w.WriteNumber("xOffset", layer.XOffset);
		w.WriteNumber("yOffset", layer.YOffset);
		w.WriteNumber("hSpeed", layer.HSpeed);
		w.WriteNumber("vSpeed", layer.VSpeed);
		w.WriteBoolean("isVisible", layer.IsVisible);
		if (data.IsVersionAtLeast(2022u, 1u))
		{
			w.WriteBoolean("effectEnabled", layer.EffectEnabled);
			w.WriteString("effectType", layer.EffectType?.Content ?? "");
		}
		if (layer.LayerType == GameMakerRoom.LayerType.Instances && layer.InstancesData != null)
		{
			w.WriteStartArray("instanceIds");
			if (layer.InstancesData.Instances != null)
			{
				foreach (GameMakerRoom.GameObject inst in layer.InstancesData.Instances)
				{
					w.WriteNumberValue(inst.InstanceID);
				}
			}
			w.WriteEndArray();
		}
		else if (layer.LayerType == GameMakerRoom.LayerType.Tiles && layer.TilesData != null)
		{
			w.WriteString("tilesBackground", layer.TilesData.Background?.Name?.Content ?? "");
			w.WriteNumber("tilesX", layer.TilesData.TilesX);
			w.WriteNumber("tilesY", layer.TilesData.TilesY);
			w.WriteStartArray("tileData");
			if (layer.TilesData.TileData != null)
			{
				uint[][] tileData = layer.TilesData.TileData;
				foreach (uint[] row in tileData)
				{
					w.WriteStartArray();
					if (row != null)
					{
						uint[] array = row;
						foreach (uint val in array)
						{
							w.WriteNumberValue(val);
						}
					}
					w.WriteEndArray();
				}
			}
			w.WriteEndArray();
		}
		else if (layer.LayerType == GameMakerRoom.LayerType.Background && layer.BackgroundData != null)
		{
			GameMakerRoom.Layer.LayerBackgroundData bd = layer.BackgroundData;
			w.WriteStartObject("backgroundData");
			w.WriteBoolean("visible", bd.Visible);
			w.WriteBoolean("foreground", bd.Foreground);
			w.WriteString("sprite", bd.Sprite?.Name?.Content ?? "");
			w.WriteBoolean("tiledHorizontally", bd.TiledHorizontally);
			w.WriteBoolean("tiledVertically", bd.TiledVertically);
			w.WriteBoolean("stretch", bd.Stretch);
			w.WriteNumber("color", bd.Color);
			w.WriteNumber("firstFrame", bd.FirstFrame);
			w.WriteNumber("animationSpeed", bd.AnimationSpeed);
			w.WriteNumber("animationSpeedType", (int)bd.AnimationSpeedType);
			w.WriteEndObject();
		}
		else if (layer.LayerType == GameMakerRoom.LayerType.Assets && layer.AssetsData != null)
		{
			GameMakerRoom.Layer.LayerAssetsData ad = layer.AssetsData;
			w.WriteStartObject("assetsData");
			w.WriteStartArray("legacyTiles");
			if (ad.LegacyTiles != null)
			{
				foreach (GameMakerRoom.Tile t in ad.LegacyTiles)
				{
					w.WriteStartObject();
					w.WriteNumber("x", t.X);
					w.WriteNumber("y", t.Y);
					w.WriteNumber("sourceX", t.SourceX);
					w.WriteNumber("sourceY", t.SourceY);
					w.WriteNumber("width", t.Width);
					w.WriteNumber("height", t.Height);
					w.WriteNumber("tileDepth", t.TileDepth);
					w.WriteNumber("instanceID", t.InstanceID);
					w.WriteNumber("scaleX", t.ScaleX);
					w.WriteNumber("scaleY", t.ScaleY);
					w.WriteNumber("color", t.Color);
					w.WriteBoolean("spriteMode", t.spriteMode);
					if (t.spriteMode)
					{
						w.WriteString("spriteDefinition", t.SpriteDefinition?.Name?.Content ?? "");
					}
					else
					{
						w.WriteString("backgroundDefinition", t.BackgroundDefinition?.Name?.Content ?? "");
					}
					w.WriteString("background", t.BackgroundDefinition?.Name?.Content ?? "");
					w.WriteEndObject();
				}
			}
			w.WriteEndArray();
			w.WriteStartArray("sprites");
			if (ad.Sprites != null)
			{
				foreach (GameMakerRoom.SpriteInstance spr in ad.Sprites)
				{
					w.WriteStartObject();
					w.WriteString("name", spr.Name?.Content ?? "");
					w.WriteString("sprite", spr.Sprite?.Name?.Content ?? "");
					w.WriteNumber("x", spr.X);
					w.WriteNumber("y", spr.Y);
					w.WriteNumber("scaleX", spr.ScaleX);
					w.WriteNumber("scaleY", spr.ScaleY);
					w.WriteNumber("color", spr.Color);
					w.WriteNumber("animationSpeed", spr.AnimationSpeed);
					w.WriteNumber("animationSpeedType", (int)spr.AnimationSpeedType);
					w.WriteNumber("frameIndex", spr.FrameIndex);
					w.WriteNumber("rotation", spr.Rotation);
					w.WriteEndObject();
				}
			}
			w.WriteEndArray();
			w.WriteEndObject();
		}
		w.WriteEndObject();
	}

	public static void ExportCodeEntries(GameMakerData data, string outputDir, int maxDegreeOfParallelism = -1)
	{
		if (data.Code != null && data.Code.Count != 0)
		{
			ExportCodeEntriesInternal(data, outputDir, null, maxDegreeOfParallelism);
		}
	}

	public static void ExportCodeEntriesSelective(GameMakerData data, string outputDir, HashSet<string> entryNames)
	{
		if (data.Code != null && data.Code.Count != 0 && entryNames.Count != 0)
		{
			ExportCodeEntriesInternal(data, outputDir, entryNames);
		}
	}

	public static Dictionary<string, (string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms)> ExportCodeEntriesToMemory(GameMakerData data, HashSet<string> entryNames)
	{
		ConcurrentDictionary<string, (string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms)> result = new ConcurrentDictionary<string, (string, string, string, Dictionary<string, (string, string)>)>();
		if (data.Code == null || data.Code.Count == 0 || entryNames.Count == 0)
		{
			return new Dictionary<string, (string, string, string, Dictionary<string, (string, string)>)>(result);
		}
		List<CodeArchiveEntryDescriptor> entryDescriptors = CodeEntryArchiveIdentity.DescribeEntries(data, entryNames);
		List<GameMakerCode> topLevel = data.Code.Where((GameMakerCode c) => c?.ParentEntry == null && c != null && c.Name?.Content != null && entryNames.Contains(c.Name.Content)).ToList();
		List<CodeArchiveEntryDescriptor> topLevelDescriptors = entryDescriptors.Where((CodeArchiveEntryDescriptor x) => x.ParentArchiveKey == null).ToList();
		(Dictionary<string, Dictionary<string, CodeArchiveEntryDescriptor>>, HashSet<string>) tuple = BuildChildDescriptorsByParent(entryDescriptors);
		Dictionary<string, Dictionary<string, CodeArchiveEntryDescriptor>> childDescriptorsByParent = tuple.Item1;
		HashSet<string> duplicateChildDescriptorParents = tuple.Item2;
		GlobalDecompileContext context = new GlobalDecompileContext(data);
		IDecompileSettings decompilerSettings = data.ToolInfo.DecompilerSettings;
		Parallel.ForEach(Enumerable.Range(0, Math.Min(topLevel.Count, topLevelDescriptors.Count)), delegate(int index)
		{
			GameMakerCode gameMakerCode = topLevel[index];
			CodeArchiveEntryDescriptor codeArchiveEntryDescriptor = topLevelDescriptors[index];
			if (gameMakerCode == null || gameMakerCode.Name?.Content == null)
			{
				return;
			}
			try
			{
				string item = null;
				string item2 = null;
				Dictionary<string, (string, string)> dictionary = null;
				try
				{
					item2 = new DecompileContext(context, gameMakerCode, decompilerSettings).DecompileToString();
				}
				catch
				{
				}
				try
				{
					item = gameMakerCode.Disassemble(data.Variables, data.CodeLocals?.For(gameMakerCode));
				}
				catch
				{
				}
				if (gameMakerCode.ChildEntries.Count > 0 && !duplicateChildDescriptorParents.Contains(codeArchiveEntryDescriptor.ArchiveKey))
				{
					dictionary = new Dictionary<string, (string, string)>();
					childDescriptorsByParent.TryGetValue(codeArchiveEntryDescriptor.ArchiveKey, out Dictionary<string, CodeArchiveEntryDescriptor> value);
					foreach (GameMakerCode current in gameMakerCode.ChildEntries)
					{
						if (current != null && current.Name?.Content != null)
						{
							try
							{
								string item3 = current.Disassemble(data.Variables, data.CodeLocals?.For(current));
								if (value != null && value.TryGetValue(current.Name.Content, out var value2))
								{
									dictionary[value2.ArchiveKey.Substring(codeArchiveEntryDescriptor.ArchiveKey.Length + 1)] = (current.Name.Content, item3);
								}
							}
							catch
							{
							}
						}
					}
				}
				result[codeArchiveEntryDescriptor.ArchiveKey] = (gameMakerCode.Name.Content, item2, item, dictionary);
			}
			catch
			{
			}
		});
		return new Dictionary<string, (string, string, string, Dictionary<string, (string, string)>)>(result);
	}

	private static void ExportCodeEntriesInternal(GameMakerData data, string outputDir, HashSet<string>? filterNames, int maxDegreeOfParallelism = -1)
	{
		string dir = EnsureDir(outputDir, "CodeEntries");
		List<CodeArchiveEntryDescriptor> entryDescriptors = CodeEntryArchiveIdentity.DescribeEntries(data, filterNames);
		List<GameMakerCode> topLevel = data.Code.Where((GameMakerCode c) => c.ParentEntry == null).ToList();
		if (filterNames != null)
		{
			topLevel = topLevel.Where((GameMakerCode c) => c != null && c.Name?.Content != null && filterNames.Contains(c.Name.Content)).ToList();
		}
		List<CodeArchiveEntryDescriptor> topLevelDescriptors = entryDescriptors.Where((CodeArchiveEntryDescriptor x) => x.ParentArchiveKey == null).ToList();
		(Dictionary<string, Dictionary<string, CodeArchiveEntryDescriptor>>, HashSet<string>) tuple = BuildChildDescriptorsByParent(entryDescriptors);
		Dictionary<string, Dictionary<string, CodeArchiveEntryDescriptor>> childDescriptorsByParent = tuple.Item1;
		HashSet<string> duplicateChildDescriptorParents = tuple.Item2;
		GlobalDecompileContext context = new GlobalDecompileContext(data);
		IDecompileSettings decompilerSettings = data.ToolInfo.DecompilerSettings;
		ParallelOptions opts = ((maxDegreeOfParallelism > 0) ? new ParallelOptions
		{
			MaxDegreeOfParallelism = maxDegreeOfParallelism
		} : new ParallelOptions());
		Parallel.ForEach(Enumerable.Range(0, Math.Min(topLevel.Count, topLevelDescriptors.Count)), opts, delegate(int index)
		{
			GameMakerCode gameMakerCode = topLevel[index];
			CodeArchiveEntryDescriptor codeArchiveEntryDescriptor = topLevelDescriptors[index];
			if (gameMakerCode == null || gameMakerCode.Name?.Content == null)
			{
				return;
			}
			try
			{
				string text = Path.Combine(dir, codeArchiveEntryDescriptor.ArchiveKey);
				Directory.CreateDirectory(text);
				try
				{
					string contents = gameMakerCode.Disassemble(data.Variables, data.CodeLocals?.For(gameMakerCode));
					File.WriteAllText(Path.Combine(text, codeArchiveEntryDescriptor.ArchiveKey + ".asm"), contents, Encoding.UTF8);
				}
				catch
				{
				}
				try
				{
					string contents2 = new DecompileContext(context, gameMakerCode, decompilerSettings).DecompileToString();
					File.WriteAllText(Path.Combine(text, codeArchiveEntryDescriptor.ArchiveKey + ".gml"), contents2, Encoding.UTF8);
				}
				catch
				{
				}
				if (duplicateChildDescriptorParents.Contains(codeArchiveEntryDescriptor.ArchiveKey))
				{
					return;
				}
				childDescriptorsByParent.TryGetValue(codeArchiveEntryDescriptor.ArchiveKey, out Dictionary<string, CodeArchiveEntryDescriptor> value);
				foreach (GameMakerCode current in gameMakerCode.ChildEntries)
				{
					if (current != null && current.Name?.Content != null)
					{
						try
						{
							string contents3 = current.Disassemble(data.Variables, data.CodeLocals?.For(current));
							if (value != null && value.TryGetValue(current.Name.Content, out var value2))
							{
								string text2 = value2.ArchiveKey.Substring(codeArchiveEntryDescriptor.ArchiveKey.Length + 1);
								File.WriteAllText(Path.Combine(text, text2 + ".asm"), contents3, Encoding.UTF8);
							}
						}
						catch
						{
						}
					}
				}
			}
			catch
			{
			}
		});
	}

	private static (Dictionary<string, Dictionary<string, CodeArchiveEntryDescriptor>> Descriptors, HashSet<string> DuplicateParents) BuildChildDescriptorsByParent(IEnumerable<CodeArchiveEntryDescriptor> descriptors)
	{
		Dictionary<string, Dictionary<string, CodeArchiveEntryDescriptor>> result = new Dictionary<string, Dictionary<string, CodeArchiveEntryDescriptor>>(StringComparer.Ordinal);
		HashSet<string> duplicates = new HashSet<string>(StringComparer.Ordinal);
		foreach (CodeArchiveEntryDescriptor descriptor in descriptors)
		{
			if (descriptor.ParentArchiveKey != null)
			{
				if (!result.TryGetValue(descriptor.ParentArchiveKey, out var children))
				{
					children = new Dictionary<string, CodeArchiveEntryDescriptor>(StringComparer.Ordinal);
					result[descriptor.ParentArchiveKey] = children;
				}
				if (!children.TryAdd(descriptor.LogicalName, descriptor))
				{
					duplicates.Add(descriptor.ParentArchiveKey);
				}
			}
		}
		return (Descriptors: result, DuplicateParents: duplicates);
	}

	public static void ExportAssetOrder(GameMakerData data, string outputDir, bool includeObjectEvents = true, bool includeVariablesFunctions = true, bool includeTextureHelpers = true, ISet<string>? assetOrderSections = null, bool includeAssetOrder = true, HashSet<string>? codeMetadataEntryNames = null)
	{
		Directory.CreateDirectory(outputDir);
		StreamWriter writer;
		if (includeAssetOrder)
		{
			writer = new StreamWriter(Path.Combine(outputDir, "asset_order.txt"));
			try
			{
				WriteSection<GameMakerSound>("sounds", data.Sounds);
				WriteSection<GameMakerSprite>("sprites", data.Sprites);
				WriteSection<GameMakerBackground>("backgrounds", data.Backgrounds);
				WriteSection<GameMakerPath>("paths", data.Paths);
				WriteSection<GameMakerScript>("scripts", data.Scripts);
				WriteSection<GameMakerFont>("fonts", data.Fonts);
				WriteSection<GameMakerGameObject>("objects", data.GameObjects);
				WriteSection<GameMakerTimeline>("timelines", data.Timelines);
				WriteSection<GameMakerRoom>("rooms", data.Rooms);
				WriteSection<GameMakerShader>("shaders", data.Shaders);
				WriteSection<GameMakerExtension>("extensions", data.Extensions);
				WriteSection<GameMakerAudioGroup>("audiogroups", data.AudioGroups);
				writer.WriteLine("@@counts@@");
				writer.WriteLine($"EmbeddedTextures={data.EmbeddedTextures.Count}");
				writer.WriteLine($"TexturePageItems={data.TexturePageItems.Count}");
			}
			finally
			{
				if (writer != null)
				{
					((IDisposable)writer).Dispose();
				}
			}
		}
		if (includeVariablesFunctions || codeMetadataEntryNames != null)
		{
			Dictionary<string, object> metadata = new Dictionary<string, object>();
			if (includeVariablesFunctions)
			{
				List<Dictionary<string, object>> varList = new List<Dictionary<string, object>>();
				foreach (GameMakerVariable v in data.Variables)
				{
					varList.Add(new Dictionary<string, object>
					{
						["n"] = v.Name?.Content ?? "",
						["t"] = (int)v.InstanceType,
						["id"] = v.VarID
					});
				}
				List<string> funcList = new List<string>();
				foreach (GameMakerFunction f in data.Functions)
				{
					funcList.Add(f.Name?.Content ?? "");
				}
				metadata["variables"] = varList;
				metadata["functions"] = funcList;
			}
			List<Dictionary<string, object>> codeEntryList = new List<Dictionary<string, object>>();
			foreach (CodeArchiveEntryDescriptor descriptor in CodeEntryArchiveIdentity.DescribeEntries(data, codeMetadataEntryNames))
			{
				Dictionary<string, object> entry = new Dictionary<string, object>
				{
					["key"] = descriptor.ArchiveKey,
					["name"] = descriptor.LogicalName,
					["occurrence"] = descriptor.Occurrence
				};
				if (!string.IsNullOrEmpty(descriptor.ParentArchiveKey))
				{
					entry["parent"] = descriptor.ParentArchiveKey;
				}
				codeEntryList.Add(entry);
			}
			metadata["codeEntries"] = codeEntryList;
			metadata["completeCodeEntries"] = codeMetadataEntryNames == null;
			string metadataFileName = ((codeMetadataEntryNames == null) ? "variables_functions.json" : "code_patch_metadata.json");
			File.WriteAllText(Path.Combine(outputDir, metadataFileName), JsonSerializer.Serialize(metadata));
		}
		if (includeObjectEvents)
		{
			Dictionary<string, int> objectNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (GameMakerGameObject gameObject in data.GameObjects)
			{
				string name = gameObject?.Name?.Content;
				if (name != null)
				{
					objectNameCounts[name] = objectNameCounts.GetValueOrDefault(name) + 1;
				}
			}
			Dictionary<string, List<Dictionary<string, object>>> objectEventsMap = new Dictionary<string, List<Dictionary<string, object>>>();
			for (int objIndex = 0; objIndex < data.GameObjects.Count; objIndex++)
			{
				GameMakerGameObject obj = data.GameObjects[objIndex];
				if (obj == null || obj.Name?.Content == null)
				{
					continue;
				}
				List<Dictionary<string, object>> events = new List<Dictionary<string, object>>();
				for (int evtType = 0; evtType < obj.Events.Count; evtType++)
				{
					foreach (GameMakerGameObject.Event evt in obj.Events[evtType])
					{
						Dictionary<string, object> evtData = new Dictionary<string, object>
						{
							["t"] = evtType,
							["s"] = evt.EventSubtype,
							["c"] = ((evt.Actions.Count <= 0) ? null : evt.Actions[0].CodeId?.Name?.Content) ?? ""
						};
						if (evtType == 4 && evt.EventSubtype < data.GameObjects.Count)
						{
							GameMakerGameObject collObj = data.GameObjects[(int)evt.EventSubtype];
							evtData["cn"] = collObj?.Name?.Content ?? "";
							if (collObj != null && collObj.Name?.Content != null)
							{
								evtData["co"] = ResourceAssetUtil.GetGameObjectOccurrence(data, (int)evt.EventSubtype);
							}
						}
						List<Dictionary<string, object>> actions = new List<Dictionary<string, object>>();
						foreach (GameMakerGameObject.EventAction action in evt.Actions)
						{
							actions.Add(new Dictionary<string, object>
							{
								["libId"] = action.LibID,
								["id"] = action.ID,
								["kind"] = action.Kind,
								["useRelative"] = action.UseRelative,
								["isQuestion"] = action.IsQuestion,
								["useApplyTo"] = action.UseApplyTo,
								["exeType"] = action.ExeType,
								["actionName"] = action.ActionName?.Content ?? "",
								["codeId"] = action.CodeId?.Name?.Content ?? "",
								["argumentCount"] = action.ArgumentCount,
								["who"] = action.Who,
								["relative"] = action.Relative,
								["isNot"] = action.IsNot
							});
						}
						evtData["actions"] = actions;
						events.Add(evtData);
					}
				}
				string key = ((objectNameCounts.GetValueOrDefault(obj.Name.Content) > 1) ? $"{obj.Name.Content}__idx{objIndex:D4}" : obj.Name.Content);
				objectEventsMap[key] = events;
			}
			File.WriteAllText(Path.Combine(outputDir, "object_events.json"), JsonSerializer.Serialize(objectEventsMap));
		}
		if (!includeTextureHelpers)
		{
			return;
		}
		ExportEmbeddedTextures(data, outputDir);
		Dictionary<GameMakerEmbeddedTexture, int> embeddedTextureIndices = BuildReferenceIndex(data.EmbeddedTextures);
		Dictionary<GameMakerTexturePageItem, int> texturePageItemIndices = BuildReferenceIndex(data.TexturePageItems);
		List<int[]> tpiList = new List<int[]>();
		foreach (GameMakerTexturePageItem tpi in data.TexturePageItems)
		{
			int textureIndex;
			int texIdx = ((tpi.TexturePage != null && embeddedTextureIndices.TryGetValue(tpi.TexturePage, out textureIndex)) ? textureIndex : (-1));
			tpiList.Add(new int[11]
			{
				texIdx, tpi.SourceX, tpi.SourceY, tpi.SourceWidth, tpi.SourceHeight, tpi.TargetX, tpi.TargetY, tpi.TargetWidth, tpi.TargetHeight, tpi.BoundingWidth,
				tpi.BoundingHeight
			});
		}
		File.WriteAllText(Path.Combine(outputDir, "texture_page_items.json"), JsonSerializer.Serialize(tpiList));
		Dictionary<string, int[]> spriteFrameMap = new Dictionary<string, int[]>();
		foreach (GameMakerSprite sprite in data.Sprites)
		{
			if (sprite?.Textures != null && sprite.Textures.Count != 0)
			{
				string key2 = sprite.Name?.Content ?? data.Sprites.IndexOf(sprite).ToString();
				int[] indices = new int[sprite.Textures.Count];
				for (int i = 0; i < sprite.Textures.Count; i++)
				{
					GameMakerTexturePageItem tpi2 = sprite.Textures[i]?.Texture;
					indices[i] = ((tpi2 != null && texturePageItemIndices.TryGetValue(tpi2, out var tpiIndex)) ? tpiIndex : (-1));
				}
				spriteFrameMap[key2] = indices;
			}
		}
		Dictionary<string, int> bgFrameMap = new Dictionary<string, int>();
		foreach (GameMakerBackground bg in data.Backgrounds)
		{
			if (bg?.Texture != null)
			{
				bgFrameMap[bg.Name?.Content ?? data.Backgrounds.IndexOf(bg).ToString()] = (texturePageItemIndices.TryGetValue(bg.Texture, out var tpiIndex2) ? tpiIndex2 : (-1));
			}
		}
		Dictionary<string, int> fontFrameMap = new Dictionary<string, int>();
		foreach (GameMakerFont font in data.Fonts)
		{
			if (font?.Texture != null)
			{
				fontFrameMap[font.Name?.Content ?? data.Fonts.IndexOf(font).ToString()] = (texturePageItemIndices.TryGetValue(font.Texture, out var tpiIndex3) ? tpiIndex3 : (-1));
			}
		}
		File.WriteAllText(Path.Combine(outputDir, "sprite_frame_map.json"), JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["sprites"] = spriteFrameMap,
			["backgrounds"] = bgFrameMap,
			["fonts"] = fontFrameMap
		}));
		static void WriteNames<T>(StreamWriter w, IList<T>? assets) where T : notnull, GameMakerNamedResource
		{
			if (assets != null)
			{
				for (int j = 0; j < assets.Count; j++)
				{
					T asset = assets[j];
					if (asset != null)
					{
						string name2 = asset.Name?.Content;
						w.WriteLine(string.IsNullOrEmpty(name2) ? j.ToString() : name2);
					}
					else
					{
						w.WriteLine("(null)");
					}
				}
			}
		}
		void WriteSection<T>(string text, IList<T>? assets) where T : notnull, GameMakerNamedResource
		{
			if (assetOrderSections == null || assetOrderSections.Contains(text))
			{
				writer.WriteLine("@@" + text + "@@");
				WriteNames<T>(writer, assets);
			}
		}
	}

	private static Dictionary<T, int> BuildReferenceIndex<T>(IList<T> items) where T : class
	{
		Dictionary<T, int> result = new Dictionary<T, int>(ReferenceEqualityComparer<T>.Instance);
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i] != null)
			{
				result.TryAdd(items[i], i);
			}
		}
		return result;
	}

	public static void ExportExtensions(GameMakerData data, string outputDir)
	{
		List<GameMakerExtension> items = data.Extensions?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		string dir = EnsureDir(outputDir, "Extensions");
		Parallel.ForEach(items, delegate(GameMakerExtension ext)
		{
			if (ext == null || ext.Name?.Content == null)
			{
				return;
			}
			try
			{
				string text = SafeName(ext.Name.Content);
				string text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				using FileStream utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
				{
					Indented = true
				});
				utf8JsonWriter.WriteStartObject();
				utf8JsonWriter.WriteString("name", ext.Name.Content);
				utf8JsonWriter.WriteString("folderName", ext.FolderName?.Content ?? "");
				if (ext.Version != null)
				{
					utf8JsonWriter.WriteString("version", ext.Version.Content ?? "");
				}
				if (ext.ClassName != null)
				{
					utf8JsonWriter.WriteString("className", ext.ClassName.Content ?? "");
				}
				GameMakerPointerList<GameMakerExtensionFile> files = ext.Files;
				if (files != null && files.Count > 0)
				{
					utf8JsonWriter.WriteStartArray("files");
					foreach (GameMakerExtensionFile current in ext.Files)
					{
						utf8JsonWriter.WriteStartObject();
						utf8JsonWriter.WriteString("filename", current.Filename?.Content ?? "");
						utf8JsonWriter.WriteNumber("kind", (int)current.Kind);
						if (current.InitScript != null)
						{
							utf8JsonWriter.WriteString("initScript", current.InitScript.Content ?? "");
						}
						if (current.CleanupScript != null)
						{
							utf8JsonWriter.WriteString("cleanupScript", current.CleanupScript.Content ?? "");
						}
						utf8JsonWriter.WriteStartArray("functions");
						if (current.Functions != null)
						{
							foreach (GameMakerExtensionFunction current2 in current.Functions)
							{
								utf8JsonWriter.WriteStartObject();
								utf8JsonWriter.WriteString("name", current2.Name?.Content ?? "");
								if (current2.ExtName != null)
								{
									utf8JsonWriter.WriteString("extName", current2.ExtName.Content ?? "");
								}
								utf8JsonWriter.WriteNumber("id", (int)current2.ID);
								utf8JsonWriter.WriteNumber("kind", (int)current2.Kind);
								utf8JsonWriter.WriteNumber("retType", (int)current2.RetType);
								utf8JsonWriter.WriteStartArray("arguments");
								if (current2.Arguments != null)
								{
									foreach (GameMakerExtensionFunctionArg current3 in current2.Arguments)
									{
										utf8JsonWriter.WriteStartObject();
										utf8JsonWriter.WriteNumber("type", (int)current3.Type);
										utf8JsonWriter.WriteEndObject();
									}
								}
								utf8JsonWriter.WriteEndArray();
								utf8JsonWriter.WriteEndObject();
							}
						}
						utf8JsonWriter.WriteEndArray();
						utf8JsonWriter.WriteEndObject();
					}
					utf8JsonWriter.WriteEndArray();
				}
				GameMakerPointerList<GameMakerExtensionOption> options = ext.Options;
				if (options != null && options.Count > 0)
				{
					utf8JsonWriter.WriteStartArray("options");
					foreach (GameMakerExtensionOption current4 in ext.Options)
					{
						utf8JsonWriter.WriteStartObject();
						utf8JsonWriter.WriteString("name", current4.Name?.Content ?? "");
						utf8JsonWriter.WriteString("value", current4.Value?.Content ?? "");
						utf8JsonWriter.WriteNumber("kind", (int)current4.Kind);
						utf8JsonWriter.WriteEndObject();
					}
					utf8JsonWriter.WriteEndArray();
				}
				utf8JsonWriter.WriteEndObject();
			}
			catch
			{
			}
		});
	}
}
