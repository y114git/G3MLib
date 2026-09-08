using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Modding.Caching;

public static class G3MCacheService
{
	private const int Schema = G3MCacheManifest.CurrentSchema;

	private const string ManifestEntryName = "g3mcache.json";

	private const string AnalysisEntryName = "data_analysis.json";

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = false
	};

	public static G3MDataAnalysisCache? TryReadDataCache(string sourcePath, G3MCacheOptions? options)
	{
		bool shouldAppend;
		try
		{
			G3MDataAnalysisCache? analysis = TryReadAnalysis(sourcePath, options);
			if (analysis?.DataInfo == null || analysis.ResourceHashes.Count == 0)
			{
				return null;
			}
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(33, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[Cache] Using analysis cache for ");
				handler.AppendFormatted(Path.GetFileName(sourcePath));
			}
			LogService.Log(ref handler);
			return analysis;
		}
		catch (Exception ex)
		{
			LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(36, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler2.AppendLiteral("[Cache] Ignored invalid cache for ");
				handler2.AppendFormatted(Path.GetFileName(sourcePath));
				handler2.AppendLiteral(": ");
				handler2.AppendFormatted(ex.Message);
			}
			LogService.Log(ref handler2);
			return null;
		}
	}

	public static G3MDataInfoSnapshot? TryReadDataInfoSnapshot(string sourcePath, G3MCacheOptions? options)
	{
		bool shouldAppend;
		try
		{
			G3MDataAnalysisCache? analysis = TryReadAnalysis(sourcePath, options);
			if (analysis?.InfoSnapshot == null)
			{
				return null;
			}
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(29, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[Cache] Using info cache for ");
				handler.AppendFormatted(Path.GetFileName(sourcePath));
			}
			LogService.Log(ref handler);
			return analysis.InfoSnapshot;
		}
		catch (Exception ex)
		{
			LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(41, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler2.AppendLiteral("[Cache] Ignored invalid info cache for ");
				handler2.AppendFormatted(Path.GetFileName(sourcePath));
				handler2.AppendLiteral(": ");
				handler2.AppendFormatted(ex.Message);
			}
			LogService.Log(ref handler2);
			return null;
		}
	}

	public static DataFileInfo? TryReadDataFileInfo(string sourcePath, G3MCacheOptions? options)
	{
		try
		{
			return TryReadAnalysis(sourcePath, options)?.DataInfo;
		}
		catch
		{
			return null;
		}
	}

	private static G3MDataAnalysisCache? TryReadAnalysis(string sourcePath, G3MCacheOptions? options)
	{
		if (options == null || !options.CanRead)
		{
			return null;
		}
		string cachePath = GetCachePath(options.ReadDirectory, sourcePath);
		if (!File.Exists(cachePath))
		{
			return null;
		}
		using ZipArchive zip = ZipFile.OpenRead(cachePath);
		if (!IsValidForSource(ReadJson<G3MCacheManifest>(zip, ManifestEntryName), sourcePath))
		{
			return null;
		}
		return ReadJson<G3MDataAnalysisCache>(zip, AnalysisEntryName);
	}

	public static async Task WriteDataInfoCacheAsync(string sourcePath, G3MDataInfoSnapshot infoSnapshot, G3MCacheOptions? options)
	{
		if (options != null && options.CanWrite)
		{
			G3MDataAnalysisCache existingAnalysis = TryReadDataCache(sourcePath, options);
			if (existingAnalysis != null)
			{
				await WriteDataCacheAsync(sourcePath, existingAnalysis.DataInfo, existingAnalysis.ResourceHashes, existingAnalysis.ResourceNameCounts, ToReadOnlyOrderNames(existingAnalysis.OrderSensitiveNames), options, infoSnapshot);
				return;
			}
			DataFileInfo dataFileInfo = new DataFileInfo
			{
				Filename = infoSnapshot.File,
				Size = infoSnapshot.Size
			};
			DataFileInfo dataFileInfo2 = dataFileInfo;
			dataFileInfo2.Md5 = await HashService.ComputeFileHashAsync(sourcePath);
			dataFileInfo.BytecodeVersion = infoSnapshot.BytecodeVersion;
			dataFileInfo.GmsVersion = infoSnapshot.Version;
			dataFileInfo.GeneralInfo = infoSnapshot.GeneralInfo;
			await WriteDataCacheAsync(sourcePath, dataFileInfo, new Dictionary<string, Dictionary<string, string>>(), new Dictionary<string, Dictionary<string, int>>(), new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase), options, infoSnapshot);
		}
	}

	public static async Task WriteDataCacheAsync(string sourcePath, DataFileInfo dataInfo, Dictionary<string, Dictionary<string, string>> resourceHashes, Dictionary<string, Dictionary<string, int>> resourceNameCounts, Dictionary<string, IReadOnlyList<string>> orderSensitiveNames, G3MCacheOptions? options, G3MDataInfoSnapshot? infoSnapshot = null)
	{
		if (options == null || !options.CanWrite)
		{
			return;
		}
		string tempPath = null;
		bool shouldAppend;
		try
		{
			Directory.CreateDirectory(options.WriteDirectory);
			string cachePath = GetCachePath(options.WriteDirectory, sourcePath);
			if (infoSnapshot == null)
			{
				infoSnapshot = TryReadDataInfoSnapshot(sourcePath, options);
			}
			tempPath = $"{cachePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
			FileInfo info = new FileInfo(sourcePath);
			G3MCacheManifest manifest = new G3MCacheManifest
			{
				Schema = Schema,
				CreatedAt = DateTime.UtcNow.ToString("O"),
				Tool = new G3MLib.Modding.Patching.ToolInfo
				{
					Name = ModdingMetadata.ToolName,
					Version = ModdingMetadata.ToolVersion
				},
				SourceFile = Path.GetFileName(sourcePath),
				SourceSize = info.Length,
				SourceLastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
				SourceMd5 = dataInfo.Md5
			};
			G3MDataAnalysisCache analysis = new G3MDataAnalysisCache
			{
				DataInfo = dataInfo,
				ResourceHashes = resourceHashes,
				ResourceNameCounts = resourceNameCounts,
				OrderSensitiveNames = orderSensitiveNames.ToDictionary<KeyValuePair<string, IReadOnlyList<string>>, string, List<string>>((KeyValuePair<string, IReadOnlyList<string>> kvp) => kvp.Key, (KeyValuePair<string, IReadOnlyList<string>> kvp) => kvp.Value.ToList(), StringComparer.OrdinalIgnoreCase),
				InfoSnapshot = infoSnapshot
			};
			using (ZipArchive zip = await ZipFile.OpenAsync(tempPath, ZipArchiveMode.Create))
			{
				WriteJson(zip, "g3mcache.json", manifest);
				WriteJson(zip, "data_analysis.json", analysis);
			}
			File.Move(tempPath, cachePath, overwrite: true);
			await Task.CompletedTask;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(33, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[Cache] Wrote analysis cache for ");
				handler.AppendFormatted(Path.GetFileName(sourcePath));
			}
			LogService.Log(ref handler);
		}
		catch (Exception ex)
		{
			LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(36, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler2.AppendLiteral("[Cache] Could not write cache for ");
				handler2.AppendFormatted(Path.GetFileName(sourcePath));
				handler2.AppendLiteral(": ");
				handler2.AppendFormatted(ex.Message);
			}
			LogService.Log(ref handler2);
		}
		finally
		{
			if (tempPath != null)
			{
				try
				{
					File.Delete(tempPath);
				}
				catch
				{
				}
			}
		}
	}

	public static Dictionary<string, IReadOnlyList<string>> ToReadOnlyOrderNames(Dictionary<string, List<string>> names)
	{
		return names.ToDictionary<KeyValuePair<string, List<string>>, string, IReadOnlyList<string>>((KeyValuePair<string, List<string>> kvp) => kvp.Key, (KeyValuePair<string, List<string>> kvp) => kvp.Value, StringComparer.OrdinalIgnoreCase);
	}

	public static G3MDataInfoSnapshot BuildInfoSnapshot(string sourcePath, GameMakerData data)
	{
		GameMakerGeneralInfo generalInfo = data.GeneralInfo;
		Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["Sprites"] = data.Sprites?.Count ?? 0,
			["Sounds"] = data.Sounds?.Count ?? 0,
			["Code"] = data.Code?.Count ?? 0,
			["GameObjects"] = data.GameObjects?.Count ?? 0,
			["Rooms"] = data.Rooms?.Count ?? 0,
			["Backgrounds"] = data.Backgrounds?.Count ?? 0,
			["Fonts"] = data.Fonts?.Count ?? 0,
			["Scripts"] = data.Scripts?.Count ?? 0,
			["Shaders"] = data.Shaders?.Count ?? 0,
			["Paths"] = data.Paths?.Count ?? 0,
			["Timelines"] = data.Timelines?.Count ?? 0,
			["Extensions"] = data.Extensions?.Count ?? 0,
			["Variables"] = data.Variables?.Count ?? 0,
			["Functions"] = data.Functions?.Count ?? 0,
			["Strings"] = data.Strings?.Count ?? 0,
			["AudioGroups"] = data.AudioGroups?.Count ?? 0,
			["EmbeddedTextures"] = data.EmbeddedTextures?.Count ?? 0,
			["TexturePageItems"] = data.TexturePageItems?.Count ?? 0,
			["TextureGroupInfo"] = data.TextureGroupInfo?.Count ?? 0,
			["Tilesets"] = data.Backgrounds?.Count ?? 0
		};
		int topLevelCode = data.Code?.Count((GameMakerCode c) => c?.ParentEntry == null) ?? 0;
		G3MDataInfoSnapshot obj = new G3MDataInfoSnapshot
		{
			File = Path.GetFileName(sourcePath),
			Size = new FileInfo(sourcePath).Length,
			Game = (generalInfo?.DisplayName?.Content ?? "Unknown"),
			BytecodeVersion = (generalInfo?.BytecodeVersion ?? 0),
			Version = GeneralInfoUtil.GetVersionDisplay(generalInfo),
			GeneralInfo = GeneralInfoUtil.ExtractGeneralInfo(data),
			ResourceCounts = counts,
			VariablesByInstanceType = ((from g in data.Variables?.GroupBy((GameMakerVariable v) => v.InstanceType.ToString())
				orderby g.Count() descending
				select g).ToDictionary<IGrouping<string, GameMakerVariable>, string, int>((IGrouping<string, GameMakerVariable> g) => g.Key, (IGrouping<string, GameMakerVariable> g) => g.Count(), StringComparer.Ordinal) ?? new Dictionary<string, int>()),
			FirstFunction = ((data.Functions == null || data.Functions.Count <= 0) ? null : data.Functions[0]?.Name?.Content)
		};
		object lastFunction;
		if (data.Functions == null || data.Functions.Count <= 0)
		{
			lastFunction = null;
		}
		else
		{
			IList<GameMakerFunction> functions = data.Functions;
			lastFunction = functions[functions.Count - 1]?.Name?.Content;
		}
		obj.LastFunction = (string?)lastFunction;
		obj.TopLevelCodeCount = topLevelCode;
		obj.ChildCodeCount = (data.Code?.Count ?? 0) - topLevelCode;
		obj.AudioGroups = data.AudioGroups?.Select((GameMakerAudioGroup g) => g?.Name?.Content ?? "?").ToList() ?? new List<string>();
		obj.Extensions = data.Extensions?.Select((GameMakerExtension e) => e?.Name?.Content ?? "?").ToList() ?? new List<string>();
		obj.RoomOrderPreview = (from r in generalInfo?.RoomOrder?.Take(10)
			select r?.Resource?.Name?.Content ?? "?").ToList() ?? new List<string>();
		obj.RoomOrderCount = (generalInfo?.RoomOrder?.Count).GetValueOrDefault();
		return obj;
	}

	private static bool IsValidForSource(G3MCacheManifest? manifest, string sourcePath)
	{
		if (manifest == null || manifest.Schema != Schema || !string.Equals(manifest.SourceKind, "data", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		FileInfo info = new FileInfo(sourcePath);
		if (info.Length != manifest.SourceSize)
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(manifest.SourceMd5))
		{
			return string.Equals(HashService.ComputeFileHash(sourcePath), manifest.SourceMd5, StringComparison.OrdinalIgnoreCase);
		}
		return info.LastWriteTimeUtc.Ticks == manifest.SourceLastWriteUtcTicks;
	}

	private static string GetCachePath(string directory, string sourcePath)
	{
		FileInfo info = new FileInfo(sourcePath);
		string identity = $"{Path.GetFullPath(sourcePath).ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
		string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
		string safeName = Path.GetFileNameWithoutExtension(sourcePath);
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char c in invalidFileNameChars)
		{
			safeName = safeName.Replace(c, '_');
		}
		return Path.Combine(directory, safeName + "_" + hash + ".g3mcache");
	}

	private static T? ReadJson<T>(ZipArchive zip, string entryName)
	{
		ZipArchiveEntry entry = zip.GetEntry(entryName);
		if (entry == null)
		{
			return default(T);
		}
		using Stream stream = entry.Open();
		return JsonSerializer.Deserialize<T>(stream, JsonOptions);
	}

	private static void WriteJson<T>(ZipArchive zip, string entryName, T value)
	{
		using Stream stream = zip.CreateEntry(entryName, CompressionLevel.Fastest).Open();
		JsonSerializer.Serialize(stream, value, JsonOptions);
	}
}
