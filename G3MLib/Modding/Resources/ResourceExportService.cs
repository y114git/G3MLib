using System.Globalization;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project;
using G3MLib.DataFile.Util;
using ImageMagick;

namespace G3MLib.Modding.Resources;

public static partial class ResourceExportService
{
	private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
	{
		public static readonly ReferenceEqualityComparer<T> Instance = new();

		public bool Equals(T? x, T? y)
		{
			return x == y;
		}

		public int GetHashCode(T obj)
		{
			return RuntimeHelpers.GetHashCode(obj);
		}
	}

	private static readonly JsonSerializerOptions s_jsonOpts = new()
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
		var sb = new StringBuilder(name.Length);
		foreach (var ch in name)
		{
			sb.Append(s_invalidChars.Contains(ch) ? '_' : ch);
		}
		var safe = sb.ToString();
		if (safe.Length <= 96)
		{
			return safe;
		}
		var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..12];
		var prefixLength = 96 - hash.Length - 2;
		return safe[..prefixLength] + "__" + hash;
	}

	private static string EnsureDir(string outputDir, string typeName)
	{
		ModdingCancellation.Throw();
		var text = Path.Combine(outputDir, typeName);
		Directory.CreateDirectory(text);
		return text;
	}

	public static void ExportSelectiveExceptCode(GameMakerData data, string outputDir, string dataFilePath, Dictionary<string, HashSet<string>> changedNames)
	{
		ExportSelectiveExceptCode(data, outputDir, dataFilePath, changedNames, CancellationToken.None);
	}

	/// <summary>Cancelling throws <see cref="OperationCanceledException"/> and removes the files this call added to the output folder.</summary>
	public static void ExportSelectiveExceptCode(GameMakerData data, string outputDir, string dataFilePath, Dictionary<string, HashSet<string>> changedNames, CancellationToken cancellationToken)
	{
		ModdingCancellation.Run(outputDir, cancellationToken, () => ExportSelectiveExceptCodeCore(data, outputDir, dataFilePath, changedNames));
	}

	private static void ExportSelectiveExceptCodeCore(GameMakerData data, string outputDir, string dataFilePath, Dictionary<string, HashSet<string>> changedNames)
	{
		var totalSw = Stopwatch.StartNew();
		var sw = new Stopwatch();
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
		var handler = new LogInterpolatedStringHandler(30, 1, out var shouldAppend);
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
		var handler2 = new LogInterpolatedStringHandler(48, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler2.AppendLiteral("[Export Timing] TOTAL (selective, excl. code): ");
			handler2.AppendFormatted(totalSw.Elapsed.TotalSeconds, "F1");
			handler2.AppendLiteral("s");
		}
		LogService.Log(ref handler2);
		void Time(string name, Action action)
		{
			var filter = changedNames.GetValueOrDefault(name);
			if (filter != null && filter.Count != 0)
			{
				sw.Restart();
				action();
				var handler3 = new LogInterpolatedStringHandler(32, 3, out var shouldAppend2);
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
		ExportTypes(data, outputDir, dataFilePath, resourceTypes, CancellationToken.None);
	}

	/// <summary>Cancelling throws <see cref="OperationCanceledException"/> and removes the files this call added to the output folder.</summary>
	public static void ExportTypes(GameMakerData data, string outputDir, string dataFilePath, IEnumerable<string> resourceTypes, CancellationToken cancellationToken)
	{
		ModdingCancellation.Run(outputDir, cancellationToken, () =>
		{
			foreach (var type in resourceTypes)
			{
				ModdingCancellation.Throw();
				ExportSingle(data, outputDir, dataFilePath, type);
			}
		});
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
		ArgumentNullException.ThrowIfNull(data);
		if (data.GeneralInfo == null)
		{
			return;
		}
		using var stream = new FileStream(Path.Combine(EnsureDir(outputDir, "GeneralInfo"), "GeneralInfo.json"), FileMode.Create);
		using var w = new Utf8JsonWriter(stream, new JsonWriterOptions
		{
			Indented = true
		});
		var gi = data.GeneralInfo;
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
		var gameRunFromGms2Ide = (gi.Info & GameMakerGeneralInfo.InfoFlags.GameRunFromGMS2IDE) != 0;
		w.WriteBoolean("licenseExclusions", gameRunFromGms2Ide);
		w.WriteBoolean("gameRunFromGMS2IDE", gameRunFromGms2Ide);
		w.WriteEndObject();
		w.WriteNumber("licenseCRC32", gi.LicenseCRC32);
		w.WritePropertyName("licenseMD5");
		w.WriteStartArray();
		if (gi.LicenseMD5 != null)
		{
			var licenseMD = gi.LicenseMD5;
			foreach (var b in licenseMD)
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
		foreach (var roomRef in gi.RoomOrder)
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
				foreach (var uid in gi.GMS2RandomUID)
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
				var licenseMD = gi.GMS2GameGUID;
				foreach (var b2 in licenseMD)
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
		ArgumentNullException.ThrowIfNull(data);
		if (data.Options != null)
		{
			var path = EnsureDir(outputDir, "Options");
			var options = data.Options;
			ExportText.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
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
				["constants"] = options.Constants?.Select(c => new Dictionary<string, object>
				{
					["name"] = c?.Name?.Content ?? "",
					["value"] = c?.Value?.Content ?? ""
				}).ToArray() ?? []
			}, s_jsonOpts), path: Path.Combine(path, "options.json"));
		}
	}

	public static void ExportGlobalScripts(GameMakerData data, string outputDir)
	{
		ArgumentNullException.ThrowIfNull(data);
		var path = EnsureDir(outputDir, "GlobalScripts");
		ExportText.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["globalInitScripts"] = data.GlobalInitScripts?.Select(s => s?.Code?.Name?.Content ?? "").ToArray() ?? [],
			["gameEndScripts"] = data.GameEndScripts?.Select(s => s?.Code?.Name?.Content ?? "").ToArray() ?? []
		}, s_jsonOpts), path: Path.Combine(path, "global_scripts.json"));
	}

	public static void ExportScripts(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.Scripts == null)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Scripts");
		List<(GameMakerScript, string)> entries = BuildUniqueExportNames((from script in data.Scripts
																		  where script?.Name?.Content != null
																		  select (script, script.Name.Content)).ToList());
		if (filter != null)
		{
			entries = [.. entries.Where(((GameMakerScript Item, string ExportName) entry) => filter.Contains(entry.ExportName) || filter.Contains(entry.Item.Name?.Content ?? string.Empty))];
		}
		CancellableParallel.ForEach(entries, delegate ((GameMakerScript Item, string ExportName) entry)
		{
			var item = entry.Item;
			var item2 = entry.ExportName;
			BatchRun.Guard(item.Name.Content, () =>
			{
				var text = Path.Combine(dir, item2);
				Directory.CreateDirectory(text);
				ExportText.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
				{
					["name"] = item.Name.Content,
					["code"] = item.Code?.Name?.Content ?? "",
					["isConstructor"] = item.IsConstructor
				}, s_jsonOpts), path: Path.Combine(text, item2 + ".json"));
			});
		});
	}

	private static List<(T Item, string ExportName)> BuildUniqueExportNames<T>(List<(T Item, string Name)> items)
	{
		var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var result = new List<(T, string)>(items.Count);
		foreach (var (Item, Name) in items)
		{
			var item = Item;
			var safeName = SafeName(Name);
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
		ArgumentNullException.ThrowIfNull(data);
		if (data.EmbeddedImages == null)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "EmbeddedImages");
		var texturePageItemIndices = (data.TexturePageItems == null) ? null : BuildReferenceIndex(data.TexturePageItems);
		CancellableParallel.ForEach([.. data.EmbeddedImages], delegate (GameMakerEmbeddedImage image)
		{
			if (image == null || image.Name?.Content == null || filter != null && !filter.Contains(image.Name.Content))
			{
				return;
			}
			BatchRun.Guard(image.Name.Content, () =>
			{
				var text = SafeName(image.Name.Content);
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				var obj = new Dictionary<string, object> { ["name"] = image.Name.Content };
				int num;
				if (image.TextureEntry != null)
				{
					var dictionary = texturePageItemIndices;
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
				var value2 = obj;
				ExportText.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(value2, s_jsonOpts));
			});
		});
	}

	public static void ExportFeatureFlags(GameMakerData data, string outputDir)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.FeatureFlags?.List != null)
		{
			var path = EnsureDir(outputDir, "FeatureFlags");
			ExportText.WriteAllText(contents: JsonSerializer.Serialize(data.FeatureFlags.List.Select(f => f?.Content ?? "").ToArray(), s_jsonOpts), path: Path.Combine(path, "feature_flags.json"));
		}
	}

	public static void ExportLanguage(GameMakerData data, string outputDir)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.Language == null)
		{
			return;
		}
		var path = EnsureDir(outputDir, "Language");
		ExportText.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["unknown1"] = data.Language.Unknown1,
			["entryIds"] = data.Language.EntryIDs?.Select(e => e?.Content ?? "").ToArray() ?? [],
			["languages"] = (from language in data.Language.Languages?.Where(l => l != null)
							 select new Dictionary<string, object>
							 {
								 ["name"] = language.Name?.Content ?? "",
								 ["region"] = language.Region?.Content ?? "",
								 ["entries"] = language.Entries?.Select(e => e?.Content ?? "").ToArray() ?? []
							 }).ToArray() ?? []
		}, s_jsonOpts), path: Path.Combine(path, "language.json"));
	}

	public static void ExportTags(GameMakerData data, string outputDir)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.Tags == null)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Tags");
		List<Dictionary<string, object>> assetTags = [];
		if (data.Tags.AssetTags != null)
		{
			foreach (var item in data.Tags.AssetTags.OrderBy(kvp => kvp.Key))
			{
				item.Deconstruct(out var key, out var value);
				var assetId = key;
				var tags = value;
				(string, int, string) decoded = DecodeAssetTagId(data, assetId);
				assetTags.Add(new Dictionary<string, object>
				{
					["assetId"] = assetId,
					["assetType"] = decoded.Item1,
					["assetIndex"] = decoded.Item2,
					["assetName"] = decoded.Item3,
					["tags"] = tags?.Select(t => t?.Content ?? "").ToArray() ?? []
				});
			}
		}
		var payload = new Dictionary<string, object>
		{
			["tags"] = data.Tags.Tags?.Select(t => t?.Content ?? "").ToArray() ?? [],
			["assetTags"] = assetTags
		};
		ExportText.WriteAllText(Path.Combine(dir, "tags.json"), JsonSerializer.Serialize(payload, s_jsonOpts));
	}

	public static void ExportFilterEffects(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		var items = data.FilterEffects?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "FilterEffects");
		CancellableParallel.ForEach(items, delegate (GameMakerFilterEffect effect)
		{
			if (effect == null || effect.Name?.Content == null || filter != null && !filter.Contains(effect.Name.Content))
			{
				return;
			}
			try
			{
				var text = SafeName(effect.Name.Content);
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				ExportText.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
				{
					["name"] = effect.Name.Content,
					["value"] = effect.Value?.Content ?? ""
				}, s_jsonOpts), path: Path.Combine(text2, text + ".json"), encoding: Encoding.UTF8);
				BatchRun.Item(effect.Name.Content);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(effect.Name.Content, ex);
				var handler = new LogInterpolatedStringHandler(43, 2, out var shouldAppend);
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
		var type = (ResourceType)(assetId >> 24);
		var rawIndex = assetId & 0xFFFFFF;
		var index = (type == ResourceType.Script) ? (rawIndex - 100000) : rawIndex;
		var name = ResourceAssetUtil.GetAssetNameByTagType(data, type, index) ?? "";
		return (Type: type.ToString(), Index: index, Name: name);
	}

	public static void ExportAudioGroups(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		var items = data.AudioGroups?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "AudioGroups");
		CancellableParallel.ForEach(items, delegate (GameMakerAudioGroup ag)
		{
			if (ag == null || ag.Name?.Content == null || filter != null && !filter.Contains(ag.Name.Content))
			{
				return;
			}
			try
			{
				var text = SafeName(ag.Name.Content);
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				using var utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using var utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
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
				BatchRun.Item(ag.Name.Content);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(ag.Name.Content, ex);
			}
		});
	}

	public static void ExportTextureGroupInfo(GameMakerData data, string outputDir)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.TextureGroupInfo == null || data.TextureGroupInfo.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "TextureGroupInfo");
		CancellableParallel.ForEach([.. data.TextureGroupInfo], delegate (GameMakerTextureGroupInfo tg)
		{
			if (tg == null || tg.Name?.Content == null)
			{
				return;
			}
			try
			{
				var text = SafeName(tg.Name.Content);
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				using var utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using var utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
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
					foreach (var current in tg.TexturePages)
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
					foreach (var current2 in tg.Sprites)
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
						foreach (var current3 in tg.SpineSprites)
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
					foreach (var current4 in tg.Fonts)
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
					foreach (var current5 in tg.Tilesets)
					{
						if (current5.Resource?.Name?.Content != null)
						{
							utf8JsonWriter.WriteStringValue(current5.Resource.Name.Content);
						}
					}
				}
				utf8JsonWriter.WriteEndArray();
				utf8JsonWriter.WriteEndObject();
				BatchRun.Item(tg.Name.Content);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(tg.Name.Content, ex);
			}
		});
	}

	public static void ExportEmbeddedTextures(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.EmbeddedTextures == null || data.EmbeddedTextures.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "EmbeddedTextures");
		CancellableParallel.For(0, data.EmbeddedTextures.Count, delegate (int i)
		{
			var text = $"texture_{i:D4}";
			if (filter != null && !filter.Contains(text))
			{
				return;
			}
			var gameMakerEmbeddedTexture = data.EmbeddedTextures[i];
			try
			{
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				if (gameMakerEmbeddedTexture.TextureData?.Image != null)
				{
					var flag = false;
					try
					{
						var bytes = gameMakerEmbeddedTexture.TextureData.Image.ToSpan(data.IsVersionAtLeast(2022u, 5u)).ToArray();
						File.WriteAllBytes(Path.Combine(text2, text + ".bin"), bytes);
						flag = true;
					}
					catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
					{
						LogService.Warning("[ExportEmbeddedTextures] Binary export failed for " + text + ", keeping PNG payload: " + ex.Message);
					}
					if (!flag)
					{
						using var magickImage = gameMakerEmbeddedTexture.TextureData.Image.GetMagickImage();
						magickImage.Strip();
						var data2 = GMImage.FromMagickImage(magickImage).ConvertToPng().GetData();
						File.WriteAllBytes(Path.Combine(text2, text + ".png"), data2);
					}
				}
				var value = new Dictionary<string, object>
				{
					["index"] = i,
					["name"] = gameMakerEmbeddedTexture.Name?.Content ?? "",
					["scaled"] = gameMakerEmbeddedTexture.Scaled,
					["generatedMips"] = gameMakerEmbeddedTexture.GeneratedMips,
					["format"] = gameMakerEmbeddedTexture.TextureData?.Image?.Format.ToString() ?? "Png"
				};
				ExportText.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(value, s_jsonOpts), Encoding.UTF8);
				BatchRun.Item(text);
			}
			catch (Exception ex2) when (!ModdingCancellation.IsCancellation(ex2))
			{
				BatchRun.Failed(text, ex2);
				LogService.Warning("[ExportEmbeddedTextures] Failed to export " + text + ": " + ex2.Message);
			}
		});
	}

	public static void ExportTexturePageItems(GameMakerData data, string outputDir)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.TexturePageItems == null)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "TexturePageItems");
		var embeddedTextureIndices = BuildReferenceIndex(data.EmbeddedTextures);
		var texturePageItems = new List<Dictionary<string, object>>(data.TexturePageItems.Count);
		for (var i = 0; i < data.TexturePageItems.Count; i++)
		{
			var tpi = data.TexturePageItems[i];
			if (tpi == null)
			{
				texturePageItems.Add(new Dictionary<string, object>
				{
					["index"] = i,
					["isNull"] = true
				});
				continue;
			}
			var texIdx = (tpi.TexturePage != null && embeddedTextureIndices.TryGetValue(tpi.TexturePage, out var textureIndex)) ? textureIndex : (-1);
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
		ExportText.WriteAllText(Path.Combine(dir, "texture_page_items.json"), JsonSerializer.Serialize(texturePageItems, s_jsonOpts));
	}

	public static void ExportSprites(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.Sprites == null || data.Sprites.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Sprites");
		(GameMakerSprite, int)[] sprites = [.. from item in data.Sprites.Select((sprite, index) => (Sprite: sprite, Index: index))
											where item.Sprite?.Name?.Content != null && (filter == null || filter.Contains(item.Sprite.Name.Content))
											select item];
		if (sprites.Length == 0)
		{
			return;
		}
		var texturePageIndexes = new Dictionary<GameMakerEmbeddedTexture, int>(ReferenceEqualityComparer.Instance);
		for (var i = 0; i < (data.EmbeddedTextures?.Count ?? 0); i++)
		{
			var texture = data.EmbeddedTextures[i];
			if (texture != null)
			{
				texturePageIndexes[texture] = i;
			}
		}
		var worker = new TextureWorker();
		try
		{
			CancellableParallel.ForEach(sprites, delegate ((GameMakerSprite Sprite, int Index) item)
			{
				var (gameMakerSprite, _) = item;
				try
				{
					var item2 = item.Index;
					var text = SafeName(gameMakerSprite.Name?.Content ?? string.Empty);
					var text2 = string.IsNullOrEmpty(text) ? $"__unnamed_sprite__idx{item2}" : text;
					var text3 = Path.Combine(dir, text2);
					Directory.CreateDirectory(text3);
					var dictionary = new Dictionary<string, object>
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
					List<Dictionary<string, object>> list = [];
					for (var j = 0; j < gameMakerSprite.Textures.Count; j++)
					{
						var gameMakerTexturePageItem = gameMakerSprite.Textures[j]?.Texture;
						if (gameMakerTexturePageItem != null)
						{
							var num = (gameMakerTexturePageItem.TexturePage != null && texturePageIndexes.TryGetValue(gameMakerTexturePageItem.TexturePage, out var value)) ? value : (-1);
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
					var collisionMasks = gameMakerSprite.CollisionMasks;
					if (collisionMasks != null && collisionMasks.Count > 0)
					{
						List<Dictionary<string, object>> list2 = [];
						foreach (var current in gameMakerSprite.CollisionMasks)
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
						var dictionary2 = new Dictionary<string, object>
						{
							["left"] = gameMakerSprite.V3NineSlice.Left,
							["top"] = gameMakerSprite.V3NineSlice.Top,
							["right"] = gameMakerSprite.V3NineSlice.Right,
							["bottom"] = gameMakerSprite.V3NineSlice.Bottom,
							["enabled"] = gameMakerSprite.V3NineSlice.Enabled
						};
						if (gameMakerSprite.V3NineSlice.TileModes != null)
						{
							dictionary2["tileModes"] = gameMakerSprite.V3NineSlice.TileModes.Select(t => (int)t).ToArray();
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
					ExportText.WriteAllText(Path.Combine(text3, text2 + ".json"), JsonSerializer.Serialize(dictionary, s_jsonOpts), Encoding.UTF8);
					string? frameError = null;
					for (var num2 = 0; num2 < gameMakerSprite.Textures.Count; num2++)
					{
						var gameMakerTexturePageItem2 = gameMakerSprite.Textures[num2]?.Texture;
						if (gameMakerTexturePageItem2 != null)
						{
							try
							{
								worker.ExportAsPNG(gameMakerTexturePageItem2, Path.Combine(text3, $"{text}_{num2}.png"));
							}
							catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
							{
								frameError ??= $"Frame {num2}: {ex.Message}";
							}
						}
					}
					BatchRun.Item(gameMakerSprite.Name.Content, frameError == null ? ResultKind.Written : ResultKind.Warning, frameError);
				}
				catch (Exception ex2) when (!ModdingCancellation.IsCancellation(ex2))
				{
					BatchRun.Failed(gameMakerSprite.Name.Content, ex2);
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
		ArgumentNullException.ThrowIfNull(data);
		var items = data.IsGameMaker2() ? data.Backgrounds.Where(bg => bg != null && bg.GMS2TileWidth == 0 && bg.GMS2TileHeight == 0).ToList() : [.. data.Backgrounds];
		if (items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Backgrounds");
		var worker = new TextureWorker();
		try
		{
			CancellableParallel.ForEach(items, delegate (GameMakerBackground bg)
			{
				if (bg == null || bg.Name?.Content == null || filter != null && !filter.Contains(bg.Name.Content))
				{
					return;
				}
				try
				{
					var text = SafeName(bg.Name.Content);
					var text2 = Path.Combine(dir, text);
					Directory.CreateDirectory(text2);
					if (bg.Texture != null)
					{
						worker.ExportAsPNG(bg.Texture, Path.Combine(text2, text + ".png"));
					}
					var dictionary = new Dictionary<string, object>
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
					ExportText.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(dictionary, s_jsonOpts), Encoding.UTF8);
					BatchRun.Item(bg.Name.Content);
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					BatchRun.Failed(bg.Name.Content, ex);
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
		ArgumentNullException.ThrowIfNull(data);
		if (data.Fonts == null || data.Fonts.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Fonts");
		var worker = new TextureWorker();
		try
		{
			CancellableParallel.ForEach([.. data.Fonts], delegate (GameMakerFont font)
			{
				if (font == null || font.Name?.Content == null || filter != null && !filter.Contains(font.Name.Content))
				{
					return;
				}
				try
				{
					var path = SafeName(font.Name.Content);
					var text = Path.Combine(dir, path);
					Directory.CreateDirectory(text);
					if (font.Texture != null)
					{
						worker.ExportAsPNG(font.Texture, Path.Combine(text, "texture.png"));
					}
					using var utf8Json = new FileStream(Path.Combine(text, "font.json"), FileMode.Create);
					using var utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
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
					foreach (var current in font.Glyphs)
					{
						utf8JsonWriter.WriteStartObject();
						utf8JsonWriter.WriteNumber("character", current.Character);
						utf8JsonWriter.WriteNumber("sourceX", current.SourceX);
						utf8JsonWriter.WriteNumber("sourceY", current.SourceY);
						utf8JsonWriter.WriteNumber("sourceWidth", current.SourceWidth);
						utf8JsonWriter.WriteNumber("sourceHeight", current.SourceHeight);
						utf8JsonWriter.WriteNumber("shift", current.Shift);
						utf8JsonWriter.WriteNumber("offset", current.Offset);
						var kerning = current.Kerning;
						if (kerning != null && kerning.Count > 0)
						{
							utf8JsonWriter.WritePropertyName("kerning");
							utf8JsonWriter.WriteStartArray();
							foreach (var current2 in current.Kerning)
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
					BatchRun.Item(font.Name.Content);
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					BatchRun.Failed(font.Name.Content, ex);
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
		ArgumentNullException.ThrowIfNull(data);
		if (data.Sounds == null || data.Sounds.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Sounds");
		var dataDir = Path.GetDirectoryName(dataFilePath) ?? "";
		using var audioReader = new AudioGroupReader(data, dataFilePath);
		CancellableParallel.ForEach([.. data.Sounds], delegate (GameMakerSound gameMakerSound)
		{
			if (gameMakerSound == null || gameMakerSound.Name?.Content == null || filter != null && !filter.Contains(gameMakerSound.Name.Content))
			{
				return;
			}
			try
			{
				var text = SafeName(gameMakerSound.Name.Content);
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				string? audioWarning = null;
				var audio = audioReader.GetAudio(gameMakerSound, out _);
				if (audio is { Length: > 0 } audioBytes)
				{
					var text3 = gameMakerSound.Type?.Content ?? ".ogg";
					if (!text3.StartsWith('.'))
					{
						text3 = "." + text3;
					}
					File.WriteAllBytes(Path.Combine(text2, text + text3), audioBytes.ToArray());
				}
				else if (gameMakerSound.GroupID > 0 && gameMakerSound.AudioID >= 0 && data.AudioGroups != null && gameMakerSound.GroupID < data.AudioGroups.Count)
				{
					audioWarning = "Audio not found in audio group " + gameMakerSound.GroupID;
				}
				using var utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using var utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
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
				if (data.IsVersionAtLeast(2024u, 6u))
				{
					utf8JsonWriter.WriteNumber("audioLength", gameMakerSound.AudioLength);
				}
				if (gameMakerSound.AudioGroup?.Name?.Content != null)
				{
					utf8JsonWriter.WriteString("audioGroupName", gameMakerSound.AudioGroup.Name.Content);
				}
				utf8JsonWriter.WriteEndObject();
				BatchRun.Item(gameMakerSound.Name.Content, audioWarning == null ? ResultKind.Written : ResultKind.Warning, audioWarning);
			}
			catch (Exception ex2) when (!ModdingCancellation.IsCancellation(ex2))
			{
				BatchRun.Failed(gameMakerSound.Name.Content, ex2);
			}
		});
	}

	public static void ExportEmbeddedAudio(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.EmbeddedAudio == null || data.EmbeddedAudio.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "EmbeddedAudio");
		CancellableParallel.For(0, data.EmbeddedAudio.Count, delegate (int i)
		{
			var text = $"audio_{i:D4}";
			if (filter != null && !filter.Contains(text))
			{
				return;
			}
			try
			{
				var gameMakerEmbeddedAudio = data.EmbeddedAudio[i];
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				File.WriteAllBytes(Path.Combine(text2, text + ".bin"), gameMakerEmbeddedAudio?.Data ?? []);
				ExportText.WriteAllText(contents: JsonSerializer.Serialize(new Dictionary<string, object>
				{
					["index"] = i,
					["bytes"] = (gameMakerEmbeddedAudio?.Data?.Length).GetValueOrDefault()
				}, s_jsonOpts), path: Path.Combine(text2, text + ".json"), encoding: Encoding.UTF8);
				BatchRun.Item(text);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(text, ex);
			}
		});
	}

	public static void ExportPaths(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		var items = data.Paths?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Paths");
		CancellableParallel.ForEach(items, delegate (GameMakerPath path)
		{
			if (path == null || path.Name?.Content == null || filter != null && !filter.Contains(path.Name.Content))
			{
				return;
			}
			try
			{
				var text = SafeName(path.Name.Content);
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				using var utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using var utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
				{
					Indented = true
				});
				utf8JsonWriter.WriteStartObject();
				utf8JsonWriter.WriteString("name", path.Name.Content);
				utf8JsonWriter.WriteBoolean("isSmooth", path.IsSmooth);
				utf8JsonWriter.WriteBoolean("isClosed", path.IsClosed);
				utf8JsonWriter.WriteNumber("precision", (int)path.Precision);
				utf8JsonWriter.WriteStartArray("points");
				foreach (var current in path.Points)
				{
					utf8JsonWriter.WriteStartObject();
					utf8JsonWriter.WriteNumber("x", current.X);
					utf8JsonWriter.WriteNumber("y", current.Y);
					utf8JsonWriter.WriteNumber("speed", current.Speed);
					utf8JsonWriter.WriteEndObject();
				}
				utf8JsonWriter.WriteEndArray();
				utf8JsonWriter.WriteEndObject();
				BatchRun.Item(path.Name.Content);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(path.Name.Content, ex);
			}
		});
	}

	public static void ExportTilesets(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (!data.IsGameMaker2())
		{
			return;
		}
		List<GameMakerBackground> items = [.. data.Backgrounds.Where(bg => bg != null && (bg.GMS2TileWidth != 0 || bg.GMS2TileHeight != 0))];
		if (items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Tilesets");
		var exportAllTilesets = filter == null || filter.Contains("Tilesets");
		List<GameMakerBackground> selectedItems = [.. items.Where(ts => ts != null && ts.Name?.Content != null && (exportAllTilesets || filter.Contains(ts.Name.Content)))];
		if (selectedItems.Count == 0)
		{
			return;
		}
		var worker = new TextureWorker();
		try
		{
			List<GameMakerBackground> noTextureItems = [.. selectedItems.Where(ts => ts.Texture == null)];
			List<IGrouping<GameMakerEmbeddedTexture, GameMakerBackground>> pageGroups = [.. from ts in selectedItems
																						 where ts.Texture?.TexturePage != null
																						 group ts by ts.Texture.TexturePage];
			BatchRun.Expect(selectedItems.Count - noTextureItems.Count - pageGroups.Count);
			CancellableParallel.ForEach(pageGroups, delegate (IGrouping<GameMakerEmbeddedTexture, GameMakerBackground> group)
			{
				var failedName = group.First().Name?.Content ?? "";
				try
				{
					var embeddedTexture = worker.GetEmbeddedTexture(group.Key);
					foreach (var current in group)
					{
						if (current != null && current.Name?.Content != null && current.Texture != null)
						{
							failedName = current.Name.Content;
							var text = SafeName(current.Name.Content);
							using var image = ExportTexturePageItemFromCachedImage(embeddedTexture, current.Texture, text);
							TextureWorker.SaveImageToFile(image, Path.Combine(dir, text + ".png"));
							WriteTilesetMetadata(data, dir, current, text);
							BatchRun.Item(current.Name.Content);
						}
					}
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					BatchRun.Failed(failedName, ex);
				}
			});
			CancellableParallel.ForEach(noTextureItems, delegate (GameMakerBackground ts)
			{
				if (ts == null || ts.Name?.Content == null)
				{
					return;
				}
				try
				{
					var name = SafeName(ts.Name.Content);
					WriteTilesetMetadata(data, dir, ts, name);
					BatchRun.Item(ts.Name.Content);
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					BatchRun.Failed(ts.Name.Content, ex);
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
		var meta = new Dictionary<string, object>
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
		var gMS2TileIds = ts.GMS2TileIds;
		if (gMS2TileIds != null && gMS2TileIds.Count > 0)
		{
			List<uint> ids = [];
			foreach (var tid in ts.GMS2TileIds)
			{
				ids.Add(tid.ID);
			}
			meta["gms2TileIds"] = ids;
		}
		ExportText.WriteAllText(Path.Combine(dir, name + ".json"), JsonSerializer.Serialize(meta, s_jsonOpts), Encoding.UTF8);
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
		ArgumentNullException.ThrowIfNull(data);
		if (data.Shaders == null || data.Shaders.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Shaders");
		CancellableParallel.ForEach([.. data.Shaders], delegate (GameMakerShader shader)
		{
			if (shader == null || shader.Name?.Content == null || filter != null && !filter.Contains(shader.Name.Content))
			{
				return;
			}
			try
			{
				var text = SafeName(shader.Name.Content);
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				ExportText.WriteAllText(Path.Combine(text2, "Type.txt"), shader.Type.ToString(), Encoding.UTF8);
				if (shader.GLSL_ES_Fragment != null)
				{
					ExportText.WriteAllText(Path.Combine(text2, "GLSL_ES_Fragment.txt"), shader.GLSL_ES_Fragment.Content ?? "", Encoding.UTF8);
				}
				if (shader.GLSL_ES_Vertex != null)
				{
					ExportText.WriteAllText(Path.Combine(text2, "GLSL_ES_Vertex.txt"), shader.GLSL_ES_Vertex.Content ?? "", Encoding.UTF8);
				}
				if (shader.GLSL_Fragment != null)
				{
					ExportText.WriteAllText(Path.Combine(text2, "GLSL_Fragment.txt"), shader.GLSL_Fragment.Content ?? "", Encoding.UTF8);
				}
				if (shader.GLSL_Vertex != null)
				{
					ExportText.WriteAllText(Path.Combine(text2, "GLSL_Vertex.txt"), shader.GLSL_Vertex.Content ?? "", Encoding.UTF8);
				}
				if (shader.HLSL9_Fragment != null)
				{
					ExportText.WriteAllText(Path.Combine(text2, "HLSL9_Fragment.txt"), shader.HLSL9_Fragment.Content ?? "", Encoding.UTF8);
				}
				if (shader.HLSL9_Vertex != null)
				{
					ExportText.WriteAllText(Path.Combine(text2, "HLSL9_Vertex.txt"), shader.HLSL9_Vertex.Content ?? "", Encoding.UTF8);
				}
				var hLSL11_VertexData = shader.HLSL11_VertexData;
				if (hLSL11_VertexData != null && hLSL11_VertexData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "HLSL11_VertexData.bin"), shader.HLSL11_VertexData.Data);
				}
				var hLSL11_PixelData = shader.HLSL11_PixelData;
				if (hLSL11_PixelData != null && hLSL11_PixelData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "HLSL11_PixelData.bin"), shader.HLSL11_PixelData.Data);
				}
				var pSSL_VertexData = shader.PSSL_VertexData;
				if (pSSL_VertexData != null && pSSL_VertexData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "PSSL_VertexData.bin"), shader.PSSL_VertexData.Data);
				}
				var pSSL_PixelData = shader.PSSL_PixelData;
				if (pSSL_PixelData != null && pSSL_PixelData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "PSSL_PixelData.bin"), shader.PSSL_PixelData.Data);
				}
				var cg_PSVita_VertexData = shader.Cg_PSVita_VertexData;
				if (cg_PSVita_VertexData != null && cg_PSVita_VertexData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "Cg_PSVita_VertexData.bin"), shader.Cg_PSVita_VertexData.Data);
				}
				var cg_PSVita_PixelData = shader.Cg_PSVita_PixelData;
				if (cg_PSVita_PixelData != null && cg_PSVita_PixelData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "Cg_PSVita_PixelData.bin"), shader.Cg_PSVita_PixelData.Data);
				}
				var cg_PS3_VertexData = shader.Cg_PS3_VertexData;
				if (cg_PS3_VertexData != null && cg_PS3_VertexData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "Cg_PS3_VertexData.bin"), shader.Cg_PS3_VertexData.Data);
				}
				var cg_PS3_PixelData = shader.Cg_PS3_PixelData;
				if (cg_PS3_PixelData != null && cg_PS3_PixelData.Data?.Length > 0)
				{
					File.WriteAllBytes(Path.Combine(text2, "Cg_PS3_PixelData.bin"), shader.Cg_PS3_PixelData.Data);
				}
				var vertexShaderAttributes = shader.VertexShaderAttributes;
				if (vertexShaderAttributes != null && vertexShaderAttributes.Count > 0)
				{
					var stringBuilder = new StringBuilder();
					foreach (var current in shader.VertexShaderAttributes)
					{
						if (current != null && current.Name?.Content != null)
						{
							stringBuilder.AppendLine(current.Name.Content);
						}
					}
					ExportText.WriteAllText(Path.Combine(text2, "VertexShaderAttributes.txt"), stringBuilder.ToString(), Encoding.UTF8);
				}
				var value = new Dictionary<string, object>
				{
					["name"] = shader.Name.Content,
					["type"] = shader.Type.ToString()
				};
				ExportText.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(value, s_jsonOpts), Encoding.UTF8);
				BatchRun.Item(shader.Name.Content);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(shader.Name.Content, ex);
			}
		});
	}

	public static void ExportTimelines(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		var items = data.Timelines?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Timelines");
		CancellableParallel.ForEach(items, delegate (GameMakerTimeline tl)
		{
			if (tl == null || tl.Name?.Content == null || filter != null && !filter.Contains(tl.Name.Content))
			{
				return;
			}
			try
			{
				var text = SafeName(tl.Name.Content);
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				using var utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using var utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
				{
					Indented = true
				});
				utf8JsonWriter.WriteStartObject();
				utf8JsonWriter.WriteString("name", tl.Name.Content);
				utf8JsonWriter.WriteStartArray("moments");
				foreach (var current in tl.Moments)
				{
					utf8JsonWriter.WriteStartObject();
					utf8JsonWriter.WriteNumber("step", (int)current.Step);
					var gameMakerPointerList = current.Event;
					if (gameMakerPointerList != null && gameMakerPointerList.Count > 0)
					{
						utf8JsonWriter.WriteStartArray("actions");
						foreach (var current2 in current.Event)
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
				BatchRun.Item(tl.Name.Content);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(tl.Name.Content, ex);
			}
		});
	}

	public static void ExportAnimationCurves(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		var items = data.AnimationCurves?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "AnimationCurves");
		CancellableParallel.ForEach(items, delegate (GameMakerAnimationCurve curve)
		{
			if (curve == null || curve.Name?.Content == null || filter != null && !filter.Contains(curve.Name.Content))
			{
				return;
			}
			try
			{
				var text = SafeName(curve.Name.Content);
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				ExportText.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(ToAnimationCurvePayload(data, curve), s_jsonOpts), Encoding.UTF8);
				BatchRun.Item(curve.Name.Content);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(curve.Name.Content, ex);
				var handler = new LogInterpolatedStringHandler(45, 2, out var shouldAppend);
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
		ArgumentNullException.ThrowIfNull(data);
		var items = data.Sequences?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Sequences");
		var context = ResourceAssetUtil.CreateProjectContext(data, Path.Combine(dir, ".project_context"));
		foreach (var sequence in items)
		{
			if (sequence == null || sequence.Name?.Content == null || filter != null && !filter.Contains(sequence.Name.Content))
			{
				continue;
			}
			try
			{
				var name = SafeName(sequence.Name.Content);
				var seqDir = Path.Combine(dir, name);
				Directory.CreateDirectory(seqDir);
				SerializableProjectAssetBridge.Export(context, sequence, Path.Combine(seqDir, name + ".json"));
				BatchRun.Item(sequence.Name.Content);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(sequence.Name.Content, ex);
				var handler = new LogInterpolatedStringHandler(39, 2, out var shouldAppend);
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
		catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
		{
			LogService.Warning("[ExportSequences] Could not remove the project context folder: " + ex.Message);
		}
	}

	public static void ExportParticleSystems(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		var items = data.ParticleSystems?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "ParticleSystems");
		var emitterKeys = EmitterKeys(data);
		CancellableParallel.ForEach(items, delegate (GameMakerParticleSystem ps)
		{
			if (ps != null && ps.Name?.Content != null && (filter == null || filter.Contains(ps.Name.Content)))
			{
				BatchRun.Guard(ps.Name.Content, () =>
				{
					var text = SafeName(ps.Name.Content);
					var text2 = Path.Combine(dir, text);
					Directory.CreateDirectory(text2);
					ExportText.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(ToParticleSystemPayload(ps, emitterKeys), s_jsonOpts), Encoding.UTF8);
				});
			}
		});
	}

	public static void ExportParticleSystemEmitters(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		var items = data.ParticleSystemEmitters?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "ParticleSystemEmitters");
		var emitterKeys = EmitterKeys(data);
		CancellableParallel.ForEach(items, delegate (GameMakerParticleSystemEmitter emitter)
		{
			if (emitter != null && emitter.Name?.Content != null && (filter == null || filter.Contains(emitter.Name.Content)))
			{
				BatchRun.Guard(emitter.Name.Content, () =>
				{
					var text = SafeName(emitterKeys[emitter]);
					var text2 = Path.Combine(dir, text);
					Directory.CreateDirectory(text2);
					ExportText.WriteAllText(Path.Combine(text2, text + ".json"), JsonSerializer.Serialize(ToParticleEmitterPayload(emitter, emitterKeys), s_jsonOpts), Encoding.UTF8);
				});
			}
		});
	}

	// Emitter names are not unique in some games. A repeated name is keyed "name__idxNNNN" (list position) so each
	// emitter gets its own folder and references find the right one; the import reads the key back.
	private static Dictionary<GameMakerParticleSystemEmitter, string> EmitterKeys(GameMakerData data)
	{
		var keys = new Dictionary<GameMakerParticleSystemEmitter, string>(ReferenceEqualityComparer<GameMakerParticleSystemEmitter>.Instance);
		var emitters = data.ParticleSystemEmitters;
		if (emitters == null)
		{
			return keys;
		}
		Dictionary<string, int> counts = [];
		foreach (var e in emitters)
		{
			var name = e?.Name?.Content;
			if (name != null)
			{
				counts[name] = counts.GetValueOrDefault(name) + 1;
			}
		}
		for (var i = 0; i < emitters.Count; i++)
		{
			var name = emitters[i]?.Name?.Content;
			if (name != null)
			{
				keys[emitters[i]] = (counts[name] > 1) ? $"{name}__idx{i:D4}" : name;
			}
		}
		return keys;
	}

	private static string EmitterKey(Dictionary<GameMakerParticleSystemEmitter, string> keys, GameMakerParticleSystemEmitter e)
	{
		return keys.TryGetValue(e, out var key) ? key : (e.Name?.Content ?? "");
	}

	private static Dictionary<string, object?> ToParticleSystemPayload(GameMakerParticleSystem ps, Dictionary<GameMakerParticleSystemEmitter, string> emitterKeys)
	{
		return new Dictionary<string, object>
		{
			["name"] = ps.Name?.Content ?? "",
			["originX"] = ps.OriginX,
			["originY"] = ps.OriginY,
			["drawOrder"] = (int)ps.DrawOrder,
			["globalSpaceParticles"] = ps.GlobalSpaceParticles,
			["emitters"] = (from n in ps.Emitters?.Select(e => (e?.Resource == null) ? "" : EmitterKey(emitterKeys, e.Resource))
							where !string.IsNullOrEmpty(n)
							select n).ToArray() ?? []
		};
	}

	private static Dictionary<string, object?> ToParticleEmitterPayload(GameMakerParticleSystemEmitter e, Dictionary<GameMakerParticleSystemEmitter, string> emitterKeys)
	{
		var payload = new Dictionary<string, object>
		{
			["name"] = e.Name?.Content ?? "",
			["sprite"] = e.Sprite?.Name?.Content ?? "",
			["spawnOnDeath"] = (e.SpawnOnDeath == null) ? "" : EmitterKey(emitterKeys, e.SpawnOnDeath),
			["spawnOnUpdate"] = (e.SpawnOnUpdate == null) ? "" : EmitterKey(emitterKeys, e.SpawnOnUpdate)
		};
		if (emitterKeys.TryGetValue(e, out var ownKey) && ownKey != e.Name?.Content)
		{
			payload["key"] = ownKey;
		}
		var properties = typeof(GameMakerParticleSystemEmitter).GetProperties();
		foreach (var prop in properties)
		{
			if (!prop.CanRead || !prop.CanWrite)
			{
				continue;
			}

			var flag = prop.Name switch
			{
				"SizeIncrease" or "SpawnOnDeath" or "SizeMax" or "SizeMin" or "Name" or "Sprite" or "SpawnOnUpdate" or "SizeWiggle" => true,
				_ => false,
			};
			if (!flag)
			{
				var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
				if (type.IsEnum)
				{
					payload[prop.Name] = Convert.ToInt32(prop.GetValue(e), CultureInfo.InvariantCulture);
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
			["channels"] = (from channel in curve.Channels?.Where(channel => channel != null)
							select new Dictionary<string, object>
							{
								["name"] = channel.Name?.Content ?? "",
								["curve"] = (uint)channel.Curve,
								["iterations"] = channel.Iterations,
								["points"] = (from point in channel.Points?.Where(point => point != null)
											  select ToAnimationCurvePointPayload(data, point)).ToArray() ?? []
							}).ToArray() ?? []
		};
	}

	private static Dictionary<string, object?> ToAnimationCurvePointPayload(GameMakerData data, GameMakerAnimationCurve.Channel.Point point)
	{
		var payload = new Dictionary<string, object>
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
		ArgumentNullException.ThrowIfNull(data);
		var items = data.GameObjects?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "GameObjects");
		var objectIndices = BuildReferenceIndex(items);
		CancellableParallel.ForEach(items, delegate (GameMakerGameObject obj)
		{
			if (obj == null || obj.Name?.Content == null || filter != null && !filter.Contains(obj.Name.Content))
			{
				return;
			}
			try
			{
				var value = SafeName(obj.Name.Content);
				var value2 = objectIndices.TryGetValue(obj, out var value3) ? value3 : (-1);
				var path = $"{value}__idx{value2:D4}";
				var text = Path.Combine(dir, path);
				Directory.CreateDirectory(text);
				using var utf8Json = new FileStream(Path.Combine(text, "object.json"), FileMode.Create);
				using var utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
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
				for (var i = 0; i < obj.Events.Count; i++)
				{
					foreach (var current in obj.Events[i])
					{
						utf8JsonWriter.WriteStartObject();
						utf8JsonWriter.WriteNumber("eventType", i);
						utf8JsonWriter.WriteNumber("eventSubtype", current.EventSubtype);
						if (i == 4 && data.GameObjects != null && current.EventSubtype < (uint)data.GameObjects.Count)
						{
							utf8JsonWriter.WriteString("collisionObjectName", data.GameObjects[(int)current.EventSubtype]?.Name?.Content ?? "");
						}
						utf8JsonWriter.WriteStartArray("actions");
						foreach (var current2 in current.Actions)
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
				var physicsVertices = obj.PhysicsVertices;
				if (physicsVertices != null && physicsVertices.Count > 0)
				{
					utf8JsonWriter.WriteStartArray("physicsVertices");
					foreach (var current3 in obj.PhysicsVertices)
					{
						utf8JsonWriter.WriteStartObject();
						utf8JsonWriter.WriteNumber("x", current3.X);
						utf8JsonWriter.WriteNumber("y", current3.Y);
						utf8JsonWriter.WriteEndObject();
					}
					utf8JsonWriter.WriteEndArray();
				}
				utf8JsonWriter.WriteEndObject();
				BatchRun.Item(obj.Name.Content);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(obj.Name.Content, ex);
			}
		});
	}

	public static void ExportRooms(GameMakerData data, string outputDir, HashSet<string>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.Rooms == null || data.Rooms.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Rooms");
		CancellableParallel.ForEach([.. data.Rooms], delegate (GameMakerRoom room)
		{
			if (room == null || room.Name?.Content == null || filter != null && !filter.Contains(room.Name.Content))
			{
				return;
			}
			try
			{
				ExportSingleRoom(data, room, dir);
				BatchRun.Item(room.Name.Content);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(room.Name.Content, ex);
				var handler = new LogInterpolatedStringHandler(40, 2, out var shouldAppend);
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
		var name = SafeName(room.Name?.Content ?? string.Empty);
		var text = Path.Combine(dir, name);
		Directory.CreateDirectory(text);
		using var s = new FileStream(Path.Combine(text, "room.json"), FileMode.Create);
		using var w = new Utf8JsonWriter(s, new JsonWriterOptions
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
		foreach (var bg in room.Backgrounds)
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
		foreach (var v in room.Views)
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
		foreach (var o in room.GameObjects)
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
		foreach (var t in room.Tiles)
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
			var layers = room.Layers;
			if (layers != null && layers.Count > 0)
			{
				w.WriteStartArray("layers");
				foreach (var layer in room.Layers)
				{
					WriteRoomLayer(data, w, layer);
				}
				w.WriteEndArray();
			}
		}
		if (data.IsVersionAtLeast(2u, 3u))
		{
			var sequences = room.Sequences;
			if (sequences != null && sequences.Count > 0)
			{
				w.WriteStartArray("sequences");
				foreach (var sequence in room.Sequences)
				{
					w.WriteStringValue(sequence?.Resource?.Name?.Content ?? "");
				}
				w.WriteEndArray();
			}
		}
		if (data.IsVersionAtLeast(2024u, 13u))
		{
			var instanceCreationOrderIDs = room.InstanceCreationOrderIDs;
			if (instanceCreationOrderIDs != null && instanceCreationOrderIDs.InstanceIDs?.Count > 0)
			{
				w.WriteStartArray("instanceCreationOrderIDs");
				foreach (var id in room.InstanceCreationOrderIDs.InstanceIDs)
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
				foreach (var inst in layer.InstancesData.Instances)
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
				var tileData = layer.TilesData.TileData;
				foreach (var row in tileData)
				{
					w.WriteStartArray();
					if (row != null)
					{
						var array = row;
						foreach (var val in array)
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
			var bd = layer.BackgroundData;
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
			var ad = layer.AssetsData;
			w.WriteStartObject("assetsData");
			w.WriteStartArray("legacyTiles");
			if (ad.LegacyTiles != null)
			{
				foreach (var t in ad.LegacyTiles)
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
				foreach (var spr in ad.Sprites)
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
		ArgumentNullException.ThrowIfNull(data);
		if (data.Code != null && data.Code.Count != 0)
		{
			ExportCodeEntriesInternal(data, outputDir, null, maxDegreeOfParallelism);
		}
	}

	public static void ExportCodeEntries(GameMakerData data, string outputDir, CancellationToken cancellationToken, int maxDegreeOfParallelism = -1)
	{
		ModdingCancellation.Run(outputDir, cancellationToken, () => ExportCodeEntries(data, outputDir, maxDegreeOfParallelism));
	}

	public static void ExportCodeEntriesSelective(GameMakerData data, string outputDir, HashSet<string> entryNames, CancellationToken cancellationToken)
	{
		ModdingCancellation.Run(outputDir, cancellationToken, () => ExportCodeEntriesSelective(data, outputDir, entryNames));
	}

	public static void ExportCodeEntriesSelective(GameMakerData data, string outputDir, HashSet<string> entryNames)
	{
		ArgumentNullException.ThrowIfNull(data);
		ArgumentNullException.ThrowIfNull(entryNames);
		if (data.Code != null && data.Code.Count != 0 && entryNames.Count != 0)
		{
			ExportCodeEntriesInternal(data, outputDir, entryNames);
		}
	}

	public static Dictionary<string, (string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms)> ExportCodeEntriesToMemory(GameMakerData data, HashSet<string> entryNames)
	{
		ArgumentNullException.ThrowIfNull(entryNames);
		ArgumentNullException.ThrowIfNull(data);
		ConcurrentDictionary<string, (string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms)> result = new ConcurrentDictionary<string, (string, string, string, Dictionary<string, (string, string)>)>();
		if (data.Code == null || data.Code.Count == 0 || entryNames.Count == 0)
		{
			return new Dictionary<string, (string, string, string, Dictionary<string, (string, string)>)>(result);
		}
		var entryDescriptors = CodeEntryArchiveIdentity.DescribeEntries(data, entryNames);
		List<GameMakerCode> topLevel = [.. data.Code.Where(c => c?.ParentEntry == null && c != null && c.Name?.Content != null && entryNames.Contains(c.Name.Content))];
		List<CodeArchiveEntryDescriptor> topLevelDescriptors = [.. entryDescriptors.Where(x => x.ParentArchiveKey == null)];
		(Dictionary<string, Dictionary<string, CodeArchiveEntryDescriptor>>, HashSet<string>) tuple = BuildChildDescriptorsByParent(entryDescriptors);
		var childDescriptorsByParent = tuple.Item1;
		var duplicateChildDescriptorParents = tuple.Item2;
		var context = new GlobalDecompileContext(data);
		var decompilerSettings = data.ToolInfo.DecompilerSettings;
		CancellableParallel.ForEach(Enumerable.Range(0, Math.Min(topLevel.Count, topLevelDescriptors.Count)), delegate (int index)
		{
			var gameMakerCode = topLevel[index];
			var codeArchiveEntryDescriptor = topLevelDescriptors[index];
			if (gameMakerCode == null || gameMakerCode.Name?.Content == null)
			{
				return;
			}
			try
			{
				string? item = null;
				string? item2 = null;
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
					dictionary = [];
					childDescriptorsByParent.TryGetValue(codeArchiveEntryDescriptor.ArchiveKey, out var value);
					foreach (var current in gameMakerCode.ChildEntries)
					{
						if (current != null && current.Name?.Content != null)
						{
							try
							{
								var item3 = current.Disassemble(data.Variables, data.CodeLocals?.For(current));
								if (value != null && value.TryGetValue(current.Name.Content, out var value2))
								{
									dictionary[value2.ArchiveKey[(codeArchiveEntryDescriptor.ArchiveKey.Length + 1)..]] = (current.Name.Content, item3);
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
		var dir = EnsureDir(outputDir, "CodeEntries");
		var entryDescriptors = CodeEntryArchiveIdentity.DescribeEntries(data, filterNames);
		List<GameMakerCode> topLevel = [.. data.Code.Where(c => c.ParentEntry == null)];
		if (filterNames != null)
		{
			topLevel = [.. topLevel.Where(c => c != null && c.Name?.Content != null && filterNames.Contains(c.Name.Content))];
		}
		List<CodeArchiveEntryDescriptor> topLevelDescriptors = [.. entryDescriptors.Where(x => x.ParentArchiveKey == null)];
		(Dictionary<string, Dictionary<string, CodeArchiveEntryDescriptor>>, HashSet<string>) tuple = BuildChildDescriptorsByParent(entryDescriptors);
		var childDescriptorsByParent = tuple.Item1;
		var duplicateChildDescriptorParents = tuple.Item2;
		var context = new GlobalDecompileContext(data);
		var decompilerSettings = data.ToolInfo.DecompilerSettings;
		var opts = (maxDegreeOfParallelism > 0) ? new ParallelOptions
		{
			MaxDegreeOfParallelism = maxDegreeOfParallelism
		} : new ParallelOptions();
		CancellableParallel.ForEach(Enumerable.Range(0, Math.Min(topLevel.Count, topLevelDescriptors.Count)), opts, delegate (int index)
		{
			var gameMakerCode = topLevel[index];
			var codeArchiveEntryDescriptor = topLevelDescriptors[index];
			if (gameMakerCode == null || gameMakerCode.Name?.Content == null)
			{
				return;
			}
			try
			{
				string? warning = null;
				var text = Path.Combine(dir, codeArchiveEntryDescriptor.ArchiveKey);
				Directory.CreateDirectory(text);
				try
				{
					var contents = gameMakerCode.Disassemble(data.Variables, data.CodeLocals?.For(gameMakerCode));
					ExportText.WriteAllText(Path.Combine(text, codeArchiveEntryDescriptor.ArchiveKey + ".asm"), contents, Encoding.UTF8);
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					warning ??= "Disassembly failed: " + ex.Message;
				}
				try
				{
					var contents2 = new DecompileContext(context, gameMakerCode, decompilerSettings).DecompileToString();
					ExportText.WriteAllText(Path.Combine(text, codeArchiveEntryDescriptor.ArchiveKey + ".gml"), contents2, Encoding.UTF8);
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					warning ??= "Decompile failed: " + ex.Message;
				}
				if (duplicateChildDescriptorParents.Contains(codeArchiveEntryDescriptor.ArchiveKey))
				{
					BatchRun.Item(gameMakerCode.Name.Content, warning == null ? ResultKind.Written : ResultKind.Warning, warning);
					return;
				}
				childDescriptorsByParent.TryGetValue(codeArchiveEntryDescriptor.ArchiveKey, out var value);
				foreach (var current in gameMakerCode.ChildEntries)
				{
					if (current != null && current.Name?.Content != null)
					{
						try
						{
							var contents3 = current.Disassemble(data.Variables, data.CodeLocals?.For(current));
							if (value != null && value.TryGetValue(current.Name.Content, out var value2))
							{
								var text2 = value2.ArchiveKey[(codeArchiveEntryDescriptor.ArchiveKey.Length + 1)..];
								ExportText.WriteAllText(Path.Combine(text, text2 + ".asm"), contents3, Encoding.UTF8);
							}
						}
						catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
						{
							warning ??= "Child entry failed: " + ex.Message;
						}
					}
				}
				BatchRun.Item(gameMakerCode.Name.Content, warning == null ? ResultKind.Written : ResultKind.Warning, warning);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(gameMakerCode.Name.Content, ex);
			}
		});
	}

	private static (Dictionary<string, Dictionary<string, CodeArchiveEntryDescriptor>> Descriptors, HashSet<string> DuplicateParents) BuildChildDescriptorsByParent(IEnumerable<CodeArchiveEntryDescriptor> descriptors)
	{
		var result = new Dictionary<string, Dictionary<string, CodeArchiveEntryDescriptor>>(StringComparer.Ordinal);
		var duplicates = new HashSet<string>(StringComparer.Ordinal);
		foreach (var descriptor in descriptors)
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
		ArgumentNullException.ThrowIfNull(data);
		Directory.CreateDirectory(outputDir);
		StreamWriter writer;
		if (includeAssetOrder)
		{
			writer = new StreamWriter(Path.Combine(outputDir, "asset_order.txt"));
			try
			{
				WriteSection("sounds", data.Sounds);
				WriteSection("sprites", data.Sprites);
				WriteSection("backgrounds", data.Backgrounds);
				WriteSection("paths", data.Paths);
				WriteSection("scripts", data.Scripts);
				WriteSection("fonts", data.Fonts);
				WriteSection("objects", data.GameObjects);
				WriteSection("timelines", data.Timelines);
				WriteSection("rooms", data.Rooms);
				WriteSection("shaders", data.Shaders);
				WriteSection("extensions", data.Extensions);
				WriteSection("audiogroups", data.AudioGroups);
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
			Dictionary<string, object> metadata = [];
			if (includeVariablesFunctions)
			{
				List<Dictionary<string, object>> varList = [];
				foreach (var v in data.Variables)
				{
					varList.Add(new Dictionary<string, object>
					{
						["n"] = v.Name?.Content ?? "",
						["t"] = (int)v.InstanceType,
						["id"] = v.VarID
					});
				}
				List<string> funcList = [];
				foreach (var f in data.Functions)
				{
					funcList.Add(f.Name?.Content ?? "");
				}
				metadata["variables"] = varList;
				metadata["functions"] = funcList;
			}
			List<Dictionary<string, object>> codeEntryList = [];
			foreach (var descriptor in CodeEntryArchiveIdentity.DescribeEntries(data, codeMetadataEntryNames))
			{
				var entry = new Dictionary<string, object>
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
			var metadataFileName = (codeMetadataEntryNames == null) ? "variables_functions.json" : "code_patch_metadata.json";
			ExportText.WriteAllText(Path.Combine(outputDir, metadataFileName), JsonSerializer.Serialize(metadata));
		}
		if (includeObjectEvents)
		{
			var objectNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (var gameObject in data.GameObjects)
			{
				var name = gameObject?.Name?.Content;
				if (name != null)
				{
					objectNameCounts[name] = objectNameCounts.GetValueOrDefault(name) + 1;
				}
			}
			Dictionary<string, List<Dictionary<string, object>>> objectEventsMap = [];
			for (var objIndex = 0; objIndex < data.GameObjects.Count; objIndex++)
			{
				var obj = data.GameObjects[objIndex];
				if (obj == null || obj.Name?.Content == null)
				{
					continue;
				}
				List<Dictionary<string, object>> events = [];
				for (var evtType = 0; evtType < obj.Events.Count; evtType++)
				{
					foreach (var evt in obj.Events[evtType])
					{
						var evtData = new Dictionary<string, object>
						{
							["t"] = evtType,
							["s"] = evt.EventSubtype,
							["c"] = ((evt.Actions.Count <= 0) ? null : evt.Actions[0].CodeId?.Name?.Content) ?? ""
						};
						if (evtType == 4 && evt.EventSubtype < data.GameObjects.Count)
						{
							var collObj = data.GameObjects[(int)evt.EventSubtype];
							evtData["cn"] = collObj?.Name?.Content ?? "";
							if (collObj != null && collObj.Name?.Content != null)
							{
								evtData["co"] = ResourceAssetUtil.GetGameObjectOccurrence(data, (int)evt.EventSubtype);
							}
						}
						List<Dictionary<string, object>> actions = [];
						foreach (var action in evt.Actions)
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
				var key = (objectNameCounts.GetValueOrDefault(obj.Name.Content) > 1) ? $"{obj.Name.Content}__idx{objIndex:D4}" : obj.Name.Content;
				objectEventsMap[key] = events;
			}
			ExportText.WriteAllText(Path.Combine(outputDir, "object_events.json"), JsonSerializer.Serialize(objectEventsMap));
		}
		if (!includeTextureHelpers)
		{
			return;
		}
		ExportEmbeddedTextures(data, outputDir);
		var embeddedTextureIndices = BuildReferenceIndex(data.EmbeddedTextures);
		var texturePageItemIndices = BuildReferenceIndex(data.TexturePageItems);
		List<int[]> tpiList = [];
		foreach (var tpi in data.TexturePageItems)
		{
			var texIdx = (tpi.TexturePage != null && embeddedTextureIndices.TryGetValue(tpi.TexturePage, out var textureIndex)) ? textureIndex : (-1);
			tpiList.Add(
			[
				texIdx, tpi.SourceX, tpi.SourceY, tpi.SourceWidth, tpi.SourceHeight, tpi.TargetX, tpi.TargetY, tpi.TargetWidth, tpi.TargetHeight, tpi.BoundingWidth,
				tpi.BoundingHeight
			]);
		}
		ExportText.WriteAllText(Path.Combine(outputDir, "texture_page_items.json"), JsonSerializer.Serialize(tpiList));
		Dictionary<string, int[]> spriteFrameMap = [];
		foreach (var sprite in data.Sprites)
		{
			if (sprite?.Textures != null && sprite.Textures.Count != 0)
			{
				var key2 = sprite.Name?.Content ?? data.Sprites.IndexOf(sprite).ToString(CultureInfo.InvariantCulture);
				var indices = new int[sprite.Textures.Count];
				for (var i = 0; i < sprite.Textures.Count; i++)
				{
					var tpi2 = sprite.Textures[i]?.Texture;
					indices[i] = (tpi2 != null && texturePageItemIndices.TryGetValue(tpi2, out var tpiIndex)) ? tpiIndex : (-1);
				}
				spriteFrameMap[key2] = indices;
			}
		}
		Dictionary<string, int> bgFrameMap = [];
		foreach (var bg in data.Backgrounds)
		{
			if (bg?.Texture != null)
			{
				bgFrameMap[bg.Name?.Content ?? data.Backgrounds.IndexOf(bg).ToString(CultureInfo.InvariantCulture)] = texturePageItemIndices.TryGetValue(bg.Texture, out var tpiIndex2) ? tpiIndex2 : (-1);
			}
		}
		Dictionary<string, int> fontFrameMap = [];
		foreach (var font in data.Fonts)
		{
			if (font?.Texture != null)
			{
				fontFrameMap[font.Name?.Content ?? data.Fonts.IndexOf(font).ToString(CultureInfo.InvariantCulture)] = texturePageItemIndices.TryGetValue(font.Texture, out var tpiIndex3) ? tpiIndex3 : (-1);
			}
		}
		ExportText.WriteAllText(Path.Combine(outputDir, "sprite_frame_map.json"), JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["sprites"] = spriteFrameMap,
			["backgrounds"] = bgFrameMap,
			["fonts"] = fontFrameMap
		}));
		static void WriteNames<T>(StreamWriter w, IList<T>? assets) where T : notnull, GameMakerNamedResource
		{
			if (assets != null)
			{
				for (var j = 0; j < assets.Count; j++)
				{
					var asset = assets[j];
					if (asset != null)
					{
						var name2 = asset.Name?.Content;
						w.WriteLine(string.IsNullOrEmpty(name2) ? j.ToString(CultureInfo.InvariantCulture) : name2);
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
		var result = new Dictionary<T, int>(ReferenceEqualityComparer<T>.Instance);
		for (var i = 0; i < items.Count; i++)
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
		ArgumentNullException.ThrowIfNull(data);
		var items = data.Extensions?.ToList();
		if (items == null || items.Count == 0)
		{
			return;
		}
		var dir = EnsureDir(outputDir, "Extensions");
		CancellableParallel.ForEach(items, delegate (GameMakerExtension ext)
		{
			if (ext == null || ext.Name?.Content == null)
			{
				return;
			}
			try
			{
				var text = SafeName(ext.Name.Content);
				var text2 = Path.Combine(dir, text);
				Directory.CreateDirectory(text2);
				using var utf8Json = new FileStream(Path.Combine(text2, text + ".json"), FileMode.Create);
				using var utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
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
				var files = ext.Files;
				if (files != null && files.Count > 0)
				{
					utf8JsonWriter.WriteStartArray("files");
					foreach (var current in ext.Files)
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
							foreach (var current2 in current.Functions)
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
									foreach (var current3 in current2.Arguments)
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
				var options = ext.Options;
				if (options != null && options.Count > 0)
				{
					utf8JsonWriter.WriteStartArray("options");
					foreach (var current4 in ext.Options)
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
				BatchRun.Item(ext.Name.Content);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(ext.Name.Content, ex);
			}
		});
	}
}
