using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;

namespace G3MLib.Modding.Patching;

public class PatchService
{
	private sealed class PatchHelperSnapshot
	{
		public required string HelpersDir { get; init; }

		public string[]? AssetOrderLines { get; init; }

		public byte[]? ObjectEventsBytes { get; init; }

		public byte[]? TexturePageItemsBytes { get; init; }

		public byte[]? SpriteFrameMapBytes { get; init; }
	}

	private sealed class PatchApplyPreparation
	{
		public required bool NeedsFinalAssetReorder { get; init; }

		public required bool NeedsHeavyFinalize { get; init; }

		public required bool NeedsCodeRemapPreparation { get; init; }

		public required PatchHelperSnapshot Helpers { get; init; }

		public string? VariablesFunctionsContent { get; init; }

		public required Dictionary<string, string> OriginalGmlForCodeRemap { get; init; }

		public required HashSet<string> PatchCodeEntryNames { get; init; }

		public required Dictionary<int, byte[]> UntouchedEmbeddedTextureSnapshot { get; init; }

		public string? StagedEmbeddedTexturesDir { get; init; }

		public Dictionary<string, List<string>> AssetNamesAtCodeImportStart { get; set; } = new Dictionary<string, List<string>>();

		public string? CodeMetadataForFinalCleanup { get; set; }

		public bool GmlCompilationRan { get; set; }
	}

	internal static class ScriptCodeResolver
	{
		internal static GameMakerCode? Resolve(GameMakerData data, string? scriptName, string? explicitCodeName = null)
		{
			if (data.Code == null)
			{
				return null;
			}
			if (!string.IsNullOrWhiteSpace(explicitCodeName))
			{
				GameMakerCode byExplicit = FindCodeByName(data, explicitCodeName);
				if (byExplicit != null)
				{
					return byExplicit;
				}
			}
			if (string.IsNullOrWhiteSpace(scriptName))
			{
				return null;
			}
			GameMakerCode byExact = FindCodeByName(data, scriptName);
			if (byExact != null)
			{
				return byExact;
			}
			bool alreadyScriptPrefixed = scriptName.StartsWith("gml_Script_", StringComparison.Ordinal);
			bool alreadyGlobalPrefixed = scriptName.StartsWith("gml_GlobalScript_", StringComparison.Ordinal);
			if (scriptName.StartsWith("gml_Script_gml_GlobalScript_", StringComparison.Ordinal))
			{
				GameMakerCode byCanonicalScript = FindCodeByName(data, "gml_Script_" + scriptName.Substring("gml_Script_gml_GlobalScript_".Length));
				if (byCanonicalScript != null)
				{
					return byCanonicalScript;
				}
			}
			if (!alreadyScriptPrefixed)
			{
				GameMakerCode byScriptPrefix = FindCodeByName(data, "gml_Script_" + scriptName);
				if (byScriptPrefix != null)
				{
					return byScriptPrefix;
				}
			}
			if (!alreadyGlobalPrefixed)
			{
				GameMakerCode byGlobalPrefix = FindCodeByName(data, "gml_GlobalScript_" + scriptName);
				if (byGlobalPrefix != null)
				{
					return byGlobalPrefix;
				}
			}
			if (alreadyScriptPrefixed)
			{
				return FindCodeByName(data, "gml_GlobalScript_" + scriptName.Substring("gml_Script_".Length));
			}
			if (alreadyGlobalPrefixed)
			{
				return FindCodeByName(data, "gml_Script_" + scriptName.Substring("gml_GlobalScript_".Length));
			}
			return null;
		}

		private static GameMakerCode? FindCodeByName(GameMakerData data, string? codeName)
		{
			if (string.IsNullOrWhiteSpace(codeName) || data.Code == null)
			{
				return null;
			}
			GameMakerCode topLevel = data.Code.ByName(codeName);
			if (topLevel != null)
			{
				return topLevel;
			}
			foreach (GameMakerCode item in data.Code)
			{
				GameMakerCode found = FindCodeByNameRecursive(item, codeName);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}

		private static GameMakerCode? FindCodeByNameRecursive(GameMakerCode? code, string codeName)
		{
			if (code?.Name?.Content == codeName)
			{
				return code;
			}
			if (code?.ChildEntries == null)
			{
				return null;
			}
			foreach (GameMakerCode childEntry in code.ChildEntries)
			{
				GameMakerCode found = FindCodeByNameRecursive(childEntry, codeName);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}
	}

	private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
	{
		public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

		public new bool Equals(object? x, object? y)
		{
			return x == y;
		}

		public int GetHashCode(object obj)
		{
			return RuntimeHelpers.GetHashCode(obj);
		}
	}

	private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private static readonly Lock s_codeImportLock = new Lock();

	private const int DataFileBufferSize = 1048576;

	private static readonly string[] s_codeTopologyWatchKeys = new string[5] { "gml_GlobalScript_scr_flag_get/gml_Script_scr_flag_get", "gml_GlobalScript_scr_flag_get/gml_Script_scr_flag_name_get", "gml_GlobalScript_scr_flag_get/gml_Script_scr_getflag", "gml_GlobalScript_scr_rhythmgame_init/gml_Script_scr_rhythmgame_init", "gml_Object_obj_watercooler_enemy_Step_0" };

	private static FileStream OpenDataReadStream(string path)
	{
		return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan);
	}

	private static FileStream OpenDataWriteStream(string path)
	{
		return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1048576);
	}

	internal static string? ValidateSavedDataFile(string path)
	{
		try
		{
			using FileStream stream = OpenDataReadStream(path);
			using GameMakerData savedData = GameMakerIO.Read(stream);
			DataIntegrityResult integrity = DataIntegrityService.ValidateOnly(savedData);
			return integrity.Success ? null : string.Join("; ", integrity.Errors.Take(20));
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	private static void DeleteInvalidOutput(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception ex)
		{
			LogService.Warning("[PatchService] Could not remove invalid output '" + path + "': " + ex.Message);
		}
	}

	private static List<string> BuildResourceTypesToProcess(PatchFileSystem pfs, G3MPatchManifest? manifest)
	{
		string[] importOrder = ResourceTypeRegistry.ImportOrder;
		HashSet<string> existingFolders = pfs.GetResourceTypes();
		List<string> resourceTypesToProcess = new List<string>(importOrder.Length);
		string[] array = importOrder;
		foreach (string rt in array)
		{
			if (existingFolders.Contains(rt))
			{
				resourceTypesToProcess.Add(rt);
			}
		}
		foreach (string rt2 in (from kvp in manifest?.Resources?.Where((KeyValuePair<string, ResourceTypeChanges> kvp) => (kvp.Value.Deleted?.Count ?? 0) > 0)
			select kvp.Key).ToHashSet<string>(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>())
		{
			if (!resourceTypesToProcess.Contains<string>(rt2, StringComparer.OrdinalIgnoreCase))
			{
				resourceTypesToProcess.Add(rt2);
			}
		}
		if (resourceTypesToProcess.Remove("GeneralInfo"))
		{
			resourceTypesToProcess.Insert(0, "GeneralInfo");
		}
		return resourceTypesToProcess;
	}

	private static bool HasScriptOrderOnlyAssetPayload(PatchFileSystem pfs)
	{
		string assetOrderPath = Path.Combine(pfs.HelpersPrefix, "asset_order.txt");
		if (pfs.FileExists(assetOrderPath))
		{
			return pfs.ReadAllLines(assetOrderPath).Any((string line) => string.Equals(line, "@@scripts@@", StringComparison.OrdinalIgnoreCase));
		}
		return false;
	}

	private static (Dictionary<string, int> Weights, int TotalNonCodeWeight, bool HasCodeEntries, int NonCodeRangeStart, int NonCodeRange) CreateImportProgressState(IReadOnlyList<string> resourceTypes)
	{
		Dictionary<string, int> weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["AudioGroups"] = 1,
			["TextureGroupInfo"] = 1,
			["Sprites"] = 5,
			["Fonts"] = 1,
			["Sounds"] = 1,
			["Paths"] = 1,
			["Shaders"] = 1,
			["GameObjects"] = 2,
			["Rooms"] = 4,
			["Tilesets"] = 1,
			["GeneralInfo"] = 1
		};
		int totalNonCodeWeight = resourceTypes.Where((string resourceType) => resourceType != "CodeEntries").Sum((string resourceType) => weights.GetValueOrDefault(resourceType, 1));
		bool hasCodeEntries = resourceTypes.Contains("CodeEntries");
		int nonCodeRangeEnd = (hasCodeEntries ? 40 : 90);
		return (Weights: weights, TotalNonCodeWeight: totalNonCodeWeight, HasCodeEntries: hasCodeEntries, NonCodeRangeStart: 5, NonCodeRange: nonCodeRangeEnd - 5);
	}

	private static PatchHelperSnapshot CapturePatchHelpers(PatchFileSystem pfs, bool includeAssetOrder, bool includeObjectEvents, bool includeTextureMappingHelpers)
	{
		string helpersDir = pfs.HelpersPrefix;
		string capturedAssetOrderPath = Path.Combine(helpersDir, "asset_order.txt");
		string capturedObjectEventsPath = Path.Combine(helpersDir, "object_events.json");
		string capturedTexturePageItemsPath = Path.Combine(helpersDir, "texture_page_items.json");
		string capturedSpriteFrameMapPath = Path.Combine(helpersDir, "sprite_frame_map.json");
		return new PatchHelperSnapshot
		{
			HelpersDir = helpersDir,
			AssetOrderLines = ((includeAssetOrder && pfs.FileExists(capturedAssetOrderPath)) ? pfs.ReadAllLines(capturedAssetOrderPath) : null),
			ObjectEventsBytes = ((includeObjectEvents && pfs.FileExists(capturedObjectEventsPath)) ? pfs.ReadAllBytes(capturedObjectEventsPath) : null),
			TexturePageItemsBytes = ((includeTextureMappingHelpers && pfs.FileExists(capturedTexturePageItemsPath)) ? pfs.ReadAllBytes(capturedTexturePageItemsPath) : null),
			SpriteFrameMapBytes = ((includeTextureMappingHelpers && pfs.FileExists(capturedSpriteFrameMapPath)) ? pfs.ReadAllBytes(capturedSpriteFrameMapPath) : null)
		};
	}

	private static PatchApplyPreparation PreparePatchApply(GameMakerData data, PatchFileSystem pfs, IReadOnlyList<string> resourceTypes, PatchApplyPlan applyPlan, bool hasCodeEntries, bool scriptOrderOnly)
	{
		bool requiresFinalAssetReorder = applyPlan.RequiresAssetReorder || scriptOrderOnly;
		bool touchesTextureWorld = RequiresTextureMappingHelpers(resourceTypes) || resourceTypes.Contains<string>("EmbeddedTextures", StringComparer.OrdinalIgnoreCase) || resourceTypes.Contains<string>("TexturePageItems", StringComparer.OrdinalIgnoreCase);
		bool needsCodeRemapPreparation = hasCodeEntries && RequiresCodeRemapPreparation(resourceTypes);
		bool needsTexturePreservation = requiresFinalAssetReorder && touchesTextureWorld;
		PatchHelperSnapshot helpers = CapturePatchHelpers(pfs, hasCodeEntries || requiresFinalAssetReorder, hasCodeEntries || requiresFinalAssetReorder, needsTexturePreservation);
		string variablesFunctionsPath = Path.Combine(helpers.HelpersDir, "variables_functions.json");
		string variablesFunctionsContent = ((hasCodeEntries && pfs.FileExists(variablesFunctionsPath)) ? pfs.ReadAllText(variablesFunctionsPath) : null);
		return new PatchApplyPreparation
		{
			NeedsFinalAssetReorder = requiresFinalAssetReorder,
			NeedsHeavyFinalize = applyPlan.RequiresHeavyFinalize,
			NeedsCodeRemapPreparation = needsCodeRemapPreparation,
			Helpers = helpers,
			VariablesFunctionsContent = variablesFunctionsContent,
			OriginalGmlForCodeRemap = (needsCodeRemapPreparation ? CaptureOriginalCodeGmlForAssetRemap(data, resourceTypes) : new Dictionary<string, string>()),
			PatchCodeEntryNames = (needsCodeRemapPreparation ? CapturePatchCodeEntryNames(pfs) : new HashSet<string>()),
			UntouchedEmbeddedTextureSnapshot = (needsTexturePreservation ? CaptureUntouchedEmbeddedTextures(data, pfs) : new Dictionary<int, byte[]>()),
			StagedEmbeddedTexturesDir = (needsTexturePreservation ? StageEmbeddedTexturesForAssetOrder(data, pfs) : null),
			CodeMetadataForFinalCleanup = variablesFunctionsContent
		};
	}

	private static (int AppliedCount, int FailedCount) ImportMinimalResources(GameMakerData data, PatchFileSystem pfs, IReadOnlyList<string> resourceTypes, string dataPath, string outputPath)
	{
		int appliedCount = 0;
		int failedCount = 0;
		foreach (string resourceType in resourceTypes)
		{
			if (!pfs.DirectoryExists(resourceType))
			{
				continue;
			}
			try
			{
				ResourceImportService.SetDataPaths(dataPath, outputPath);
				if (!ResourceImportService.Import(resourceType, data, resourceType))
				{
					failedCount++;
				}
				else
				{
					appliedCount++;
				}
			}
			catch (Exception ex)
			{
				LogService.Warning("ERROR applying " + resourceType + ": " + ex.Message);
				failedCount++;
			}
			finally
			{
				ResourceImportService.SetDataPaths(null, null);
			}
		}
		return (appliedCount, failedCount);
	}

	private static (int AppliedCount, int FailedCount) ImportPatchResources(GameMakerData data, PatchFileSystem pfs, G3MPatchManifest? manifest, IReadOnlyList<string> resourceTypes, PatchApplyPreparation preparation, bool hasCodeEntries, string dataPath, string outputPath, Stopwatch nonCodeImportSw, Stopwatch codeImportSw, Stopwatch phaseSw, Stopwatch preCodeReorderSw, Stopwatch postSpriteReorderSw, bool logDetailedCodeImport)
	{
		(Dictionary<string, int> Weights, int TotalNonCodeWeight, bool HasCodeEntries, int NonCodeRangeStart, int NonCodeRange) tuple = CreateImportProgressState(resourceTypes);
		Dictionary<string, int> resourceWeights = tuple.Weights;
		int totalNonCodeWeight = tuple.TotalNonCodeWeight;
		int nonCodeRangeStart = tuple.NonCodeRangeStart;
		int nonCodeRange = tuple.NonCodeRange;
		int nonCodeProgress = 0;
		int appliedCount = 0;
		int failedCount = 0;
		nonCodeImportSw.Start();
		foreach (string resourceType in resourceTypes)
		{
			if (ApplyDeletedResources(data, manifest, resourceType) > 0)
			{
				appliedCount++;
			}
			bool shouldAppend;
			if (resourceType == "CodeEntries")
			{
				if (PrepareCodeAssetOrder(data, pfs, resourceTypes, preparation.Helpers.HelpersDir, preparation.Helpers.AssetOrderLines, preparation.Helpers.ObjectEventsBytes, preCodeReorderSw))
				{
					appliedCount++;
				}
				if (preparation.NeedsCodeRemapPreparation)
				{
					preparation.AssetNamesAtCodeImportStart = CaptureAssetNamesForCodeRemap(data);
				}
				if (logDetailedCodeImport)
				{
					LogService.Progress(45, 100);
				}
				nonCodeImportSw.Stop();
				LogService.Log("[PatchService] Applying CodeEntries via hybrid import (in-memory)...");
				phaseSw.Restart();
				codeImportSw.Start();
				(bool, bool, string) codeImportResult = ImportCodeEntries(data, pfs, preparation.Helpers.HelpersDir, preparation.VariablesFunctionsContent);
				if (preparation.CodeMetadataForFinalCleanup == null)
				{
					string text = (preparation.CodeMetadataForFinalCleanup = codeImportResult.Item3);
				}
				preparation.GmlCompilationRan = codeImportResult.Item2;
				if (codeImportResult.Item1)
				{
					appliedCount++;
				}
				else
				{
					failedCount++;
				}
				codeImportSw.Stop();
				if (logDetailedCodeImport)
				{
					LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(46, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler.AppendLiteral("[PatchService] Released PFS file data, RAM: ");
						handler.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
						handler.AppendLiteral("MB");
					}
					LogService.Log(ref handler);
					LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(39, 2, out shouldAppend);
					if (shouldAppend)
					{
						handler2.AppendLiteral("[Timing] Import CodeEntries: ");
						handler2.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
						handler2.AppendLiteral("s, RAM: ");
						handler2.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
						handler2.AppendLiteral("MB");
					}
					LogService.Log(ref handler2);
				}
				LogService.Progress(90, 100);
				nonCodeImportSw.Start();
				continue;
			}
			if (hasCodeEntries && resourceType == "Scripts")
			{
				LogService.Log("[PatchService] Deferring Scripts import until after CodeEntries...");
				nonCodeProgress += resourceWeights.GetValueOrDefault(resourceType, 1);
				LogService.Progress(nonCodeRangeStart + nonCodeRange * nonCodeProgress / Math.Max(totalNonCodeWeight, 1), 100);
				continue;
			}
			if (!pfs.DirectoryExists(resourceType))
			{
				nonCodeProgress += resourceWeights.GetValueOrDefault(resourceType, 1);
				LogService.Progress(nonCodeRangeStart + nonCodeRange * nonCodeProgress / Math.Max(totalNonCodeWeight, 1), 100);
				continue;
			}
			LogInterpolatedStringHandler handler3 = new LogInterpolatedStringHandler(27, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler3.AppendLiteral("[PatchService] Applying ");
				handler3.AppendFormatted(resourceType);
				handler3.AppendLiteral("...");
			}
			LogService.Log(ref handler3);
			phaseSw.Restart();
			try
			{
				ResourceImportService.SetDataPaths(dataPath, outputPath);
				if (resourceType == "Sprites")
				{
					ResourceImportService.SetProgressRange(6, 24);
				}
				if (!ResourceImportService.Import(resourceType, data, resourceType))
				{
					LogService.Warning("No native importer for " + resourceType + ", skipping");
					failedCount++;
				}
				else
				{
					appliedCount++;
				}
			}
			catch (Exception ex)
			{
				LogService.Warning("ERROR applying " + resourceType + ": " + ex.Message);
				failedCount++;
			}
			finally
			{
				ResourceImportService.SetProgressRange();
				ResourceImportService.SetDataPaths(null, null);
			}
			if (logDetailedCodeImport)
			{
				LogInterpolatedStringHandler handler4 = new LogInterpolatedStringHandler(28, 3, out shouldAppend);
				if (shouldAppend)
				{
					handler4.AppendLiteral("[Timing] Import ");
					handler4.AppendFormatted(resourceType);
					handler4.AppendLiteral(": ");
					handler4.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
					handler4.AppendLiteral("s, RAM: ");
					handler4.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler4.AppendLiteral("MB");
				}
				LogService.Log(ref handler4);
			}
			if (resourceType == "Sprites")
			{
				ReorderAfterSpriteImport(data, pfs, resourceTypes, preparation.Helpers.HelpersDir, postSpriteReorderSw);
			}
			nonCodeProgress += resourceWeights.GetValueOrDefault(resourceType, 1);
			LogService.Progress(nonCodeRangeStart + nonCodeRange * nonCodeProgress / Math.Max(totalNonCodeWeight, 1), 100);
		}
		nonCodeImportSw.Stop();
		return (AppliedCount: appliedCount, FailedCount: failedCount);
	}

	private static bool ApplyFinalAssetOrder(bool required, GameMakerData data, PatchFileSystem pfs, IReadOnlyList<string> resourceTypes, PatchHelperSnapshot helpers, string? stagedEmbeddedTexturesDir, bool scriptOrderOnly, Stopwatch reorderStopwatch)
	{
		if (!required)
		{
			return false;
		}
		string reorderDir = null;
		try
		{
			if (helpers.AssetOrderLines == null)
			{
				return false;
			}
			reorderDir = Path.Combine(Path.GetTempPath(), $"g3mtool_final_reorder_{Guid.NewGuid():N}");
			Directory.CreateDirectory(reorderDir);
			HashSet<string> sections = BuildRelevantAssetOrderSections(resourceTypes, includeScriptsForCodeEntries: true);
			sections.Add("counts");
			string[] filteredLines = SelectAssetOrderSections(helpers.AssetOrderLines, sections, "TexturePageItems", "EmbeddedTextures");
			if (helpers.TexturePageItemsBytes == null && helpers.SpriteFrameMapBytes == null && stagedEmbeddedTexturesDir == null && AssetOrderAlreadyMatches(data, filteredLines))
			{
				LogService.Log("[PatchService] Final asset order already matches target; skipping final reorder.");
				return false;
			}
			File.WriteAllLines(Path.Combine(reorderDir, "asset_order.txt"), filteredLines);
			if (helpers.ObjectEventsBytes != null)
			{
				File.WriteAllBytes(Path.Combine(reorderDir, "object_events.json"), helpers.ObjectEventsBytes);
			}
			if (helpers.TexturePageItemsBytes != null)
			{
				File.WriteAllBytes(Path.Combine(reorderDir, "texture_page_items.json"), helpers.TexturePageItemsBytes);
			}
			if (helpers.SpriteFrameMapBytes != null)
			{
				File.WriteAllBytes(Path.Combine(reorderDir, "sprite_frame_map.json"), RemoveUnsafeRelinksFromSpriteFrameMap(helpers.SpriteFrameMapBytes, removeFontRelinks: false));
			}
			if (stagedEmbeddedTexturesDir != null)
			{
				CopyStagedEmbeddedTexturesForAssetOrder(stagedEmbeddedTexturesDir, reorderDir);
			}
			LogService.Log("[PatchService] Enforcing final asset order after all imports...");
			ResourceImportService.SetPatchFileSystem(null);
			reorderStopwatch.Start();
			ResourceImportService.ImportAssetOrder(data, reorderDir);
			reorderStopwatch.Stop();
			return scriptOrderOnly;
		}
		catch (Exception ex)
		{
			if (reorderStopwatch.IsRunning)
			{
				reorderStopwatch.Stop();
			}
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(44, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Final asset reorder warning: ");
				handler.AppendFormatted(ex.Message);
			}
			LogService.Log(ref handler);
			return false;
		}
		finally
		{
			ResourceImportService.SetPatchFileSystem(pfs);
			if (reorderDir != null)
			{
				try
				{
					Directory.Delete(reorderDir, recursive: true);
				}
				catch
				{
				}
			}
			if (stagedEmbeddedTexturesDir != null)
			{
				try
				{
					Directory.Delete(stagedEmbeddedTexturesDir, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private static void ReorderAfterSpriteImport(GameMakerData data, PatchFileSystem pfs, IReadOnlyList<string> resourceTypes, string helpersDir, Stopwatch reorderStopwatch)
	{
		string assetOrderPath = Path.Combine(helpersDir, "asset_order.txt");
		if (!pfs.FileExists(assetOrderPath))
		{
			return;
		}
		string reorderDir = Path.Combine(Path.GetTempPath(), $"g3mtool_reorder_{Guid.NewGuid():N}");
		Directory.CreateDirectory(reorderDir);
		string[] filteredLines = SelectAssetOrderSections(pfs.ReadAllLines(assetOrderPath), BuildRelevantAssetOrderSections(resourceTypes, includeScriptsForCodeEntries: false), "TexturePageItems", "EmbeddedTextures");
		File.WriteAllLines(Path.Combine(reorderDir, "asset_order.txt"), filteredLines);
		LogService.Log("[PatchService] Re-ordering assets after sprite import...");
		ResourceImportService.SetPatchFileSystem(null);
		try
		{
			reorderStopwatch.Start();
			ResourceImportService.ImportAssetOrder(data, reorderDir);
			reorderStopwatch.Stop();
		}
		catch (Exception ex)
		{
			if (reorderStopwatch.IsRunning)
			{
				reorderStopwatch.Stop();
			}
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(44, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Post-sprite reorder warning: ");
				handler.AppendFormatted(ex.Message);
			}
			LogService.Log(ref handler);
		}
		finally
		{
			ResourceImportService.SetPatchFileSystem(pfs);
			try
			{
				Directory.Delete(reorderDir, recursive: true);
			}
			catch
			{
			}
		}
	}

	private static bool PrepareCodeAssetOrder(GameMakerData data, PatchFileSystem pfs, IReadOnlyList<string> resourceTypes, string helpersDir, string[]? capturedAssetOrderLines, byte[]? objectEvents, Stopwatch reorderStopwatch)
	{
		string assetOrderPath = Path.Combine(helpersDir, "asset_order.txt");
		if (!pfs.FileExists(assetOrderPath))
		{
			return false;
		}
		string[] orderLines = capturedAssetOrderLines ?? pfs.ReadAllLines(assetOrderPath);
		Dictionary<string, int> targetObjectCounts = new Dictionary<string, int>();
		bool inObjects = false;
		string[] array = orderLines;
		int i;
		for (i = 0; i < array.Length; i++)
		{
			string line = array[i];
			if (line.StartsWith("@@"))
			{
				inObjects = line == "@@objects@@";
			}
			else if (inObjects && !string.IsNullOrWhiteSpace(line))
			{
				string name = line.Trim();
				if (!(name == "(null)") && !int.TryParse(name, out var _))
				{
					targetObjectCounts[name] = targetObjectCounts.GetValueOrDefault(name) + 1;
				}
			}
		}
		Dictionary<string, int> existingObjectCounts = new Dictionary<string, int>();
		foreach (GameMakerGameObject gameObject in data.GameObjects)
		{
			string name2 = gameObject?.Name?.Content;
			if (name2 != null)
			{
				existingObjectCounts[name2] = existingObjectCounts.GetValueOrDefault(name2) + 1;
			}
		}
		int createdObjects = 0;
		foreach (KeyValuePair<string, int> item in targetObjectCounts)
		{
			item.Deconstruct(out var key, out i);
			string name3 = key;
			int targetCount = i;
			for (int count = existingObjectCounts.GetValueOrDefault(name3); count < targetCount; count++)
			{
				data.GameObjects.Add(new GameMakerGameObject
				{
					Name = data.Strings.MakeString(name3)
				});
				createdObjects++;
			}
		}
		bool shouldAppend;
		if (createdObjects > 0)
		{
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(61, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Created ");
				handler.AppendFormatted(createdObjects);
				handler.AppendLiteral(" missing GameObjects from TARGET order");
			}
			LogService.Log(ref handler);
		}
		string reorderDir = Path.Combine(Path.GetTempPath(), $"g3mtool_aoreorder_{Guid.NewGuid():N}");
		try
		{
			LogService.Log("[PatchService] Reordering assets to match TARGET order...");
			reorderStopwatch.Start();
			Directory.CreateDirectory(reorderDir);
			HashSet<string> sections = BuildRelevantAssetOrderSections(resourceTypes, includeScriptsForCodeEntries: true);
			sections.Add("counts");
			File.WriteAllLines(Path.Combine(reorderDir, "asset_order.txt"), SelectAssetOrderSections(orderLines, sections, "TexturePageItems", "EmbeddedTextures"));
			if (objectEvents != null)
			{
				File.WriteAllBytes(Path.Combine(reorderDir, "object_events.json"), objectEvents);
			}
			ResourceImportService.SetPatchFileSystem(null);
			ResourceImportService.ImportAssetOrder(data, reorderDir);
			reorderStopwatch.Stop();
			shouldAppend = true;
			return shouldAppend;
		}
		catch (Exception ex)
		{
			if (reorderStopwatch.IsRunning)
			{
				reorderStopwatch.Stop();
			}
			bool shouldAppend2;
			LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(38, 1, out shouldAppend2);
			if (shouldAppend2)
			{
				handler2.AppendLiteral("[PatchService] Asset reorder warning: ");
				handler2.AppendFormatted(ex.Message);
			}
			LogService.Log(ref handler2);
			shouldAppend = false;
			return shouldAppend;
		}
		finally
		{
			ResourceImportService.SetPatchFileSystem(pfs);
			try
			{
				Directory.Delete(reorderDir, recursive: true);
			}
			catch
			{
			}
		}
	}

	private static (bool Success, bool GmlCompilationRan, string? CodeMetadata) ImportCodeEntries(GameMakerData data, PatchFileSystem pfs, string helpersDir, string? capturedVariablesFunctionsContent)
	{
		string codeMetadata = ReadCodeMetadataContent(pfs, helpersDir);
		if (codeMetadata != null && pfs.FileExists(Path.Combine(helpersDir, "variables_functions.json")))
		{
			codeMetadata = capturedVariablesFunctionsContent ?? codeMetadata;
		}
		string objectEvents = null;
		string objectEventsPath = Path.Combine(helpersDir, "object_events.json");
		if (pfs.FileExists(objectEventsPath))
		{
			objectEvents = pfs.ReadAllText(objectEventsPath);
		}
		string globalScripts = null;
		if (pfs.FileExists("GlobalScripts/global_scripts.json"))
		{
			globalScripts = pfs.ReadAllText("GlobalScripts/global_scripts.json");
		}
		bool hasScripts = pfs.DirectoryExists("Scripts");
		try
		{
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(49, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Using ");
				handler.AppendFormatted(pfs.GmlEntries.Count);
				handler.AppendLiteral(" GML + ");
				handler.AppendFormatted(pfs.AsmEntries.Count);
				handler.AppendLiteral(" ASM entries from PFS");
			}
			LogService.Log(ref handler);
			ImportCodeEntriesDirect(data, pfs.GmlEntries, pfs.AsmEntries, pfs.AsmEntryPaths, pfs.CodeEntryLogicalNames, helpersDir, out var gmlCompilationRan, codeMetadata, objectEvents);
			if (hasScripts)
			{
				ResourceImportService.Import("Scripts", data, "Scripts");
				LogService.Log("[PatchService] Re-applied Scripts after CodeEntries");
			}
			if (globalScripts != null)
			{
				ApplyDeferredGlobalScripts(data, globalScripts);
				LogService.Log("[PatchService] Re-applied GlobalScripts after CodeEntries");
			}
			return (Success: true, GmlCompilationRan: gmlCompilationRan, CodeMetadata: codeMetadata);
		}
		catch (Exception ex)
		{
			LogService.Warning("ERROR applying CodeEntries: " + ex.Message);
			return (Success: false, GmlCompilationRan: false, CodeMetadata: codeMetadata);
		}
		finally
		{
			ResourceImportService.SetPatchFileSystem(null);
			pfs.ReleaseFileData();
			pfs.ReleaseCodeEntries();
			GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
		}
	}

	public static async Task<PatchCreateResult> CreatePatchAsync(string originalPath, string modifiedPath, string outputPath, Dictionary<string, Dictionary<string, string>>? precomputedOriginalHashes = null, DataFileInfo? precomputedOriginalInfo = null, Dictionary<string, Dictionary<string, int>>? precomputedOriginalNameCounts = null, Dictionary<string, IReadOnlyList<string>>? precomputedOriginalOrderedNames = null, GameMakerData? precomputedModifiedData = null, bool includeXdeltaFallback = false, G3MCacheOptions? cacheOptions = null)
	{
		if (!File.Exists(originalPath))
		{
			return new PatchCreateResult
			{
				Success = false,
				Error = "Original file not found: " + originalPath
			};
		}
		if (!File.Exists(modifiedPath))
		{
			return new PatchCreateResult
			{
				Success = false,
				Error = "Modified file not found: " + modifiedPath
			};
		}
		string tempXdeltaResult = null;
		string tempExactPatch = null;
		string exactPatchSourcePath = null;
		if (Path.GetExtension(modifiedPath).Equals(".xdelta", StringComparison.OrdinalIgnoreCase))
		{
			LogService.Log("[PatchService] Detected xdelta file, applying to original first...");
			XDeltaService xDeltaService = new XDeltaService();
			tempXdeltaResult = Path.Combine(Path.GetTempPath(), $"g3mtool_xdelta_{Guid.NewGuid():N}.win");
			XDeltaResult xdeltaResult = await xDeltaService.ApplyPatchAsync(originalPath, modifiedPath, tempXdeltaResult);
			if (!xdeltaResult.Success)
			{
				if (File.Exists(tempXdeltaResult))
				{
					File.Delete(tempXdeltaResult);
				}
				return new PatchCreateResult
				{
					Success = false,
					Error = "Failed to apply xdelta: " + xdeltaResult.Error
				};
			}
			LogService.Log("[PatchService] xdelta applied successfully, using result as modified file");
			string originalDir = Path.GetDirectoryName(originalPath);
			string tempDir = Path.GetDirectoryName(tempXdeltaResult);
			if (!string.IsNullOrEmpty(originalDir) && !string.IsNullOrEmpty(tempDir))
			{
				string[] files = Directory.GetFiles(originalDir, "audiogroup*.dat");
				foreach (string audioGroup in files)
				{
					string destPath = Path.Combine(tempDir, Path.GetFileName(audioGroup));
					if (!File.Exists(destPath))
					{
						File.Copy(audioGroup, destPath);
					}
				}
			}
			modifiedPath = tempXdeltaResult;
		}
		try
		{
			Stopwatch totalSw = Stopwatch.StartNew();
			Stopwatch phaseSw = new Stopwatch();
			string tempDir2 = Path.Combine(Path.GetTempPath(), $"g3mtool_{Guid.NewGuid():N}");
			string modifiedExportDir = Path.Combine(tempDir2, "modified");
			Directory.CreateDirectory(modifiedExportDir);
			LogService.SetOperation("Creating patch");
			LogService.Progress(0, 100);
			int originalCodeEntryCount = -1;
			bool originalCodeEntryCountRecovered = false;
			Dictionary<string, Dictionary<string, string>> originalHashes;
			DataFileInfo originalInfo;
			Dictionary<string, Dictionary<string, int>> originalNameCounts;
			Dictionary<string, IReadOnlyList<string>> originalOrderedNames;
			bool shouldAppend;
			string key;
			if (precomputedOriginalHashes != null && precomputedOriginalInfo != null)
			{
				originalHashes = precomputedOriginalHashes;
				originalInfo = precomputedOriginalInfo;
				originalNameCounts = precomputedOriginalNameCounts ?? ReadResourceNameCounts(originalPath);
				originalOrderedNames = precomputedOriginalOrderedNames ?? ReadOrderSensitiveResourceNames(originalPath);
				LogService.Log("[PatchService] Using precomputed original hashes (shared)");
			}
			else
			{
				G3MDataAnalysisCache cachedOriginal = G3MCacheService.TryReadDataCache(originalPath, cacheOptions);
				if (cachedOriginal != null)
				{
					originalInfo = cachedOriginal.DataInfo;
					originalHashes = cachedOriginal.ResourceHashes;
					originalNameCounts = cachedOriginal.ResourceNameCounts;
					originalOrderedNames = G3MCacheService.ToReadOnlyOrderNames(cachedOriginal.OrderSensitiveNames);
					LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(25, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler.AppendLiteral("[PatchService] Original: ");
						handler.AppendFormatted(originalInfo.GeneralInfo?.DisplayName ?? "Unknown");
					}
					LogService.Log(ref handler);
					LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(41, 2, out shouldAppend);
					if (shouldAppend)
					{
						handler2.AppendLiteral("[Timing] Original cache: ");
						handler2.AppendFormatted(phaseSw.Elapsed.TotalMilliseconds, "F0");
						handler2.AppendLiteral("ms, peak RAM: ");
						handler2.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
						handler2.AppendLiteral("MB");
					}
					LogService.Log(ref handler2);
				}
				else
				{
					phaseSw.Restart();
					LogService.Log("[PatchService] Loading original data file...");
					using FileStream stream = OpenDataReadStream(originalPath);
					using GameMakerData originalData = GameMakerIO.Read(stream);
					originalCodeEntryCount = originalData.Code?.Count ?? 0;
					TimeSpan originalLoadTime = phaseSw.Elapsed;
					LogInterpolatedStringHandler handler3 = new LogInterpolatedStringHandler(25, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler3.AppendLiteral("[PatchService] Original: ");
						handler3.AppendFormatted(originalData.GeneralInfo?.DisplayName?.Content ?? "Unknown");
					}
					LogService.Log(ref handler3);
					DataFileInfo dataFileInfo = new DataFileInfo
					{
						Filename = Path.GetFileName(originalPath),
						Size = new FileInfo(originalPath).Length
					};
					DataFileInfo dataFileInfo2 = dataFileInfo;
					key = (dataFileInfo2.Md5 = await HashService.ComputeFileHashAsync(originalPath));
					dataFileInfo.BytecodeVersion = originalData.GeneralInfo?.BytecodeVersion ?? 0;
					dataFileInfo.GmsVersion = GeneralInfoUtil.GetVersionDisplay(originalData.GeneralInfo);
					dataFileInfo.GeneralInfo = GeneralInfoUtil.ExtractGeneralInfo(originalData);
					originalInfo = dataFileInfo;
					phaseSw.Restart();
					LogService.Log("[PatchService] Hashing original resources in memory...");
					originalHashes = ResourceHashService.HashAll(originalData, originalPath);
					originalNameCounts = GetResourceNameCounts(originalData);
					originalOrderedNames = GetOrderSensitiveResourceNames(originalData);
					await G3MCacheService.WriteDataCacheAsync(originalPath, originalInfo, originalHashes, originalNameCounts, originalOrderedNames, cacheOptions);
					LogInterpolatedStringHandler handler4 = new LogInterpolatedStringHandler(48, 3, out shouldAppend);
					if (shouldAppend)
					{
						handler4.AppendLiteral("[Timing] Original load: ");
						handler4.AppendFormatted(originalLoadTime.TotalSeconds, "F1");
						handler4.AppendLiteral("s, hash: ");
						handler4.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
						handler4.AppendLiteral("s, peak RAM: ");
						handler4.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
						handler4.AppendLiteral("MB");
					}
					LogService.Log(ref handler4);
				}
				phaseSw.Restart();
				GC.Collect();
				LogInterpolatedStringHandler handler5 = new LogInterpolatedStringHandler(48, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler5.AppendLiteral("[Timing] GC after original: ");
					handler5.AppendFormatted(phaseSw.Elapsed.TotalMilliseconds, "F0");
					handler5.AppendLiteral("ms, RAM after GC: ");
					handler5.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler5.AppendLiteral("MB");
				}
				LogService.Log(ref handler5);
			}
			if (originalCodeEntryCount < 0 && originalHashes.TryGetValue("CodeEntries", out Dictionary<string, string> originalCodeHashes))
			{
				originalCodeEntryCount = originalCodeHashes.Count;
				originalCodeEntryCountRecovered = true;
			}
			LogService.Progress(20, 100);
			phaseSw.Restart();
			LogService.Log((precomputedModifiedData != null) ? "[PatchService] Using preloaded modified data..." : "[PatchService] Loading modified data file...");
			G3MCacheOptions modifiedCacheOptions = ((tempXdeltaResult == null) ? cacheOptions : null);
			HashSet<string> helperForcedResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			DataFileInfo modifiedInfo;
			Dictionary<string, Dictionary<string, string>> modifiedHashes;
			string key2;
			Dictionary<string, HashSet<string>> manifestChangedNamesPerType;
			Dictionary<string, (string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms)> codeEntriesInMemory;
			using (FileStream stream = ((precomputedModifiedData == null) ? OpenDataReadStream(modifiedPath) : null))
			{
				using GameMakerData originalData = ((stream == null) ? null : GameMakerIO.Read(stream));
				GameMakerData modifiedData = precomputedModifiedData ?? originalData;
				int modifiedCodeEntryCount = modifiedData.Code?.Count ?? 0;
				TimeSpan originalLoadTime = phaseSw.Elapsed;
				LogInterpolatedStringHandler handler6 = new LogInterpolatedStringHandler(25, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler6.AppendLiteral("[PatchService] Modified: ");
					handler6.AppendFormatted(modifiedData.GeneralInfo?.DisplayName?.Content ?? "Unknown");
				}
				LogService.Log(ref handler6);
				G3MDataAnalysisCache cachedModified = G3MCacheService.TryReadDataCache(modifiedPath, modifiedCacheOptions);
				if (cachedModified != null)
				{
					modifiedInfo = cachedModified.DataInfo;
					modifiedHashes = cachedModified.ResourceHashes;
				}
				else
				{
					DataFileInfo dataFileInfo2 = new DataFileInfo
					{
						Filename = Path.GetFileName(modifiedPath),
						Size = new FileInfo(modifiedPath).Length
					};
					DataFileInfo dataFileInfo = dataFileInfo2;
					key = (dataFileInfo.Md5 = await HashService.ComputeFileHashAsync(modifiedPath));
					dataFileInfo2.BytecodeVersion = modifiedData.GeneralInfo?.BytecodeVersion ?? 0;
					dataFileInfo2.GmsVersion = GeneralInfoUtil.GetVersionDisplay(modifiedData.GeneralInfo);
					dataFileInfo2.GeneralInfo = GeneralInfoUtil.ExtractGeneralInfo(modifiedData);
					modifiedInfo = dataFileInfo2;
					phaseSw.Restart();
					LogService.Log("[PatchService] Hashing modified resources in memory...");
					modifiedHashes = ResourceHashService.HashAll(modifiedData, modifiedPath);
					await G3MCacheService.WriteDataCacheAsync(modifiedPath, modifiedInfo, modifiedHashes, GetResourceNameCounts(modifiedData), GetOrderSensitiveResourceNames(modifiedData), modifiedCacheOptions);
				}
				LogCreateCompatibilityWarnings(originalInfo, modifiedInfo);
				LogService.Progress(40, 100);
				TimeSpan hashTime = phaseSw.Elapsed;
				phaseSw.Restart();
				LogService.Log("[PatchService] Comparing hashes to build selective export filter...");
				Dictionary<string, HashSet<string>> changedNamesPerType = new Dictionary<string, HashSet<string>>();
				string[] files = ResourceTypeRegistry.AllTypes;
				foreach (string resourceType in files)
				{
					Dictionary<string, string> origTypeHashes = originalHashes.GetValueOrDefault(resourceType) ?? new Dictionary<string, string>();
					Dictionary<string, string>? obj = modifiedHashes.GetValueOrDefault(resourceType) ?? new Dictionary<string, string>();
					HashSet<string> changedNames = new HashSet<string>();
					foreach (KeyValuePair<string, string> item in obj)
					{
						item.Deconstruct(out key, out key2);
						string name = key;
						string hash = key2;
						if (!origTypeHashes.TryGetValue(name, out var origHash) || origHash != hash)
						{
							changedNames.Add(name);
						}
					}
					if (changedNames.Count > 0)
					{
						changedNamesPerType[resourceType] = changedNames;
					}
				}
				manifestChangedNamesPerType = CloneChangedNameMap(changedNamesPerType);
				AddDuplicateCountChanges(originalNameCounts, modifiedData, changedNamesPerType);
				PromoteOrderSensitiveExportsIfNeeded(originalOrderedNames, modifiedData, modifiedHashes, changedNamesPerType, helperForcedResourceTypes);
				PromoteVersionSensitiveExportsIfNeeded(originalInfo.GeneralInfo, modifiedInfo.GeneralInfo, modifiedHashes, changedNamesPerType);
				LogInterpolatedStringHandler handler7 = new LogInterpolatedStringHandler(41, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler7.AppendLiteral("[Timing] Hash comparison (pre-export): ");
					handler7.AppendFormatted(phaseSw.Elapsed.TotalMilliseconds, "F0");
					handler7.AppendLiteral("ms");
				}
				LogService.Log(ref handler7);
				ExpandChangedCodeEntriesForTopology(modifiedData, changedNamesPerType);
				phaseSw.Restart();
				LogService.Log("[PatchService] Exporting modified resources (selective)...");
				ResourceExportService.ExportSelectiveExceptCode(modifiedData, modifiedExportDir, modifiedPath, changedNamesPerType);
				HashSet<string> changedCodeNames = changedNamesPerType.GetValueOrDefault("CodeEntries") ?? new HashSet<string>();
				Stopwatch codeExportSw = Stopwatch.StartNew();
				codeEntriesInMemory = ResourceExportService.ExportCodeEntriesToMemory(modifiedData, changedCodeNames);
				codeExportSw.Stop();
				LogInterpolatedStringHandler handler8 = new LogInterpolatedStringHandler(43, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler8.AppendLiteral("[Export Timing] CodeEntries (in-memory ");
					handler8.AppendFormatted(changedCodeNames.Count);
					handler8.AppendLiteral("): ");
					handler8.AppendFormatted(codeExportSw.Elapsed.TotalSeconds, "F1");
					handler8.AppendLiteral("s");
				}
				LogService.Log(ref handler8);
				TimeSpan exportTime = phaseSw.Elapsed;
				phaseSw.Restart();
				string helpersExportDir = Path.Combine(tempDir2, "Helpers");
				ExportPatchHelpers(modifiedData, helpersExportDir, originalHashes, modifiedHashes, changedNamesPerType, helperForcedResourceTypes, originalCodeEntryCount, originalCodeEntryCountRecovered, modifiedCodeEntryCount, codeEntriesInMemory);
				LogInterpolatedStringHandler handler9 = new LogInterpolatedStringHandler(71, 5, out shouldAppend);
				if (shouldAppend)
				{
					handler9.AppendLiteral("[Timing] Modified load: ");
					handler9.AppendFormatted(originalLoadTime.TotalSeconds, "F1");
					handler9.AppendLiteral("s, hash: ");
					handler9.AppendFormatted(hashTime.TotalSeconds, "F1");
					handler9.AppendLiteral("s, export: ");
					handler9.AppendFormatted(exportTime.TotalSeconds, "F1");
					handler9.AppendLiteral("s, helpers: ");
					handler9.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
					handler9.AppendLiteral("s, peak RAM: ");
					handler9.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler9.AppendLiteral("MB");
				}
				LogService.Log(ref handler9);
			}
			if (precomputedModifiedData == null)
			{
				phaseSw.Restart();
				GC.Collect();
				LogInterpolatedStringHandler handler10 = new LogInterpolatedStringHandler(48, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler10.AppendLiteral("[Timing] GC after modified: ");
					handler10.AppendFormatted(phaseSw.Elapsed.TotalMilliseconds, "F0");
					handler10.AppendLiteral("ms, RAM after GC: ");
					handler10.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler10.AppendLiteral("MB");
				}
				LogService.Log(ref handler10);
			}
			LogService.Progress(60, 100);
			G3MPatchManifest manifest = new G3MPatchManifest
			{
				CreatedAt = DateTime.UtcNow.ToString("o"),
				Tool = new ToolInfo
				{
					Name = ModdingMetadata.ToolName,
					Version = ModdingMetadata.ToolVersion
				},
				Original = originalInfo,
				Modified = modifiedInfo,
				Resources = new Dictionary<string, ResourceTypeChanges>(),
				Statistics = new PatchStatistics()
			};
			try
			{
				phaseSw.Restart();
				LogService.Log("[PatchService] Comparing resources by in-memory hash...");
				string[] files = ResourceTypeRegistry.AllTypes;
				foreach (string resourceType2 in files)
				{
					Dictionary<string, string>? originalHashes2 = originalHashes.GetValueOrDefault(resourceType2) ?? new Dictionary<string, string>();
					Dictionary<string, string> modTypeHashes = modifiedHashes.GetValueOrDefault(resourceType2) ?? new Dictionary<string, string>();
					ResourceTypeChanges changes = BuildResourceTypeChangesFromKnownDifferences(originalHashes2, modTypeHashes, modifiedExportDir, resourceType2, manifestChangedNamesPerType.GetValueOrDefault(resourceType2));
					if (resourceType2 == "CodeEntries")
					{
						PopulateCodeEntryFiles(changes, codeEntriesInMemory);
					}
					if (changes.HasChanges)
					{
						manifest.Resources[resourceType2] = changes;
					}
				}
				LogService.Progress(65, 100);
				LogInterpolatedStringHandler handler11 = new LogInterpolatedStringHandler(27, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler11.AppendLiteral("[Timing] Hash comparison: ");
					handler11.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
					handler11.AppendLiteral("s");
				}
				LogService.Log(ref handler11);
				ResourceTypeChanges value;
				foreach (KeyValuePair<string, ResourceTypeChanges> resource in manifest.Resources)
				{
					resource.Deconstruct(out key2, out value);
					ResourceTypeChanges changes2 = value;
					manifest.Statistics.TotalChanged += changes2.Changed?.Count ?? 0;
					manifest.Statistics.TotalNew += changes2.New?.Count ?? 0;
					manifest.Statistics.TotalDeleted += changes2.Deleted?.Count ?? 0;
					IEnumerable<ResourceChange> changed = changes2.Changed;
					foreach (ResourceChange c in changed ?? Enumerable.Empty<ResourceChange>())
					{
						manifest.Statistics.TotalChangedFiles += c.Files?.Count ?? 0;
					}
					changed = changes2.New;
					foreach (ResourceChange n in changed ?? Enumerable.Empty<ResourceChange>())
					{
						manifest.Statistics.TotalNewFiles += n.Files?.Count ?? 0;
					}
				}
				manifest.ApplyPlan = BuildPatchApplyPlan(manifest.Resources);
				string outputDir = Path.GetDirectoryName(outputPath);
				if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
				{
					Directory.CreateDirectory(outputDir);
				}
				phaseSw.Restart();
				LogService.Log("[PatchService] Creating .g3mpatch file...");
				using (FileStream stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, FileOptions.SequentialScan))
				{
					using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create);
					using (StreamWriter writer = new StreamWriter(await archive.CreateEntry("g3mpatch.json", ArchiveCompressionUtil.GetLevel("g3mpatch.json")).OpenAsync()))
					{
						string json = JsonSerializer.Serialize(manifest, s_jsonOptions);
						await writer.WriteAsync(json);
					}
					foreach (KeyValuePair<string, ResourceTypeChanges> resource2 in manifest.Resources)
					{
						resource2.Deconstruct(out key2, out value);
						string resourceType3 = key2;
						ResourceTypeChanges changes3 = value;
						if (resourceType3 == "CodeEntries")
						{
							continue;
						}
						string modifiedResDir = Path.Combine(modifiedExportDir, resourceType3);
						IEnumerable<ResourceChange> changed = changes3.Changed;
						foreach (ResourceChange changed2 in changed ?? Enumerable.Empty<ResourceChange>())
						{
							await AddResourceToArchiveAsync(archive, modifiedResDir, resourceType3, changed2.Name);
						}
						changed = changes3.New;
						foreach (ResourceChange newRes in changed ?? Enumerable.Empty<ResourceChange>())
						{
							await AddResourceToArchiveAsync(archive, modifiedResDir, resourceType3, newRes.Name);
						}
					}
					if (codeEntriesInMemory.Count > 0)
					{
						foreach (KeyValuePair<string, (string, string, string, Dictionary<string, (string, string)>)> item2 in codeEntriesInMemory)
						{
							item2.Deconstruct(out key2, out var value2);
							string resourceType3 = key2;
							(string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms) entry = value2;
							if (entry.gml != null)
							{
								string gmlPath = $"CodeEntries/{resourceType3}/{resourceType3}.gml";
								using StreamWriter writer = new StreamWriter(await archive.CreateEntry(gmlPath, ArchiveCompressionUtil.GetLevel(gmlPath)).OpenAsync(), Encoding.UTF8);
								await writer.WriteAsync(entry.gml);
							}
							if (entry.asm != null)
							{
								string asmPath = $"CodeEntries/{resourceType3}/{resourceType3}.asm";
								using StreamWriter writer = new StreamWriter(await archive.CreateEntry(asmPath, ArchiveCompressionUtil.GetLevel(asmPath)).OpenAsync(), Encoding.UTF8);
								await writer.WriteAsync(entry.asm);
							}
							if (entry.childAsms == null)
							{
								continue;
							}
							foreach (KeyValuePair<string, (string, string)> item3 in entry.childAsms)
							{
								item3.Deconstruct(out key2, out var value3);
								string childLeafKey = key2;
								(string LogicalName, string Asm) childEntry = value3;
								string childPath = $"CodeEntries/{resourceType3}/{childLeafKey}.asm";
								using StreamWriter writer = new StreamWriter(await archive.CreateEntry(childPath, ArchiveCompressionUtil.GetLevel(childPath)).OpenAsync(), Encoding.UTF8);
								await writer.WriteAsync(childEntry.Asm);
							}
						}
						LogInterpolatedStringHandler handler12 = new LogInterpolatedStringHandler(54, 1, out shouldAppend);
						if (shouldAppend)
						{
							handler12.AppendLiteral("[PatchService] CodeEntries: ");
							handler12.AppendFormatted(codeEntriesInMemory.Count);
							handler12.AppendLiteral(" written directly to patch");
						}
						LogService.Log(ref handler12);
					}
					string helpersDir = Path.Combine(tempDir2, "Helpers");
					if (Directory.Exists(helpersDir))
					{
						string[] files2 = Directory.GetFiles(helpersDir, "*", SearchOption.AllDirectories);
						foreach (string resourceType3 in files2)
						{
							string relative = Path.GetRelativePath(helpersDir, resourceType3).Replace('\\', '/');
							if (relative.StartsWith("EmbeddedTextures/", StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}
							string helperEntryPath = "Helpers/" + relative;
							using Stream entryStream = await archive.CreateEntry(helperEntryPath, ArchiveCompressionUtil.GetLevel(helperEntryPath)).OpenAsync();
							using FileStream fileStream = File.OpenRead(resourceType3);
							await fileStream.CopyToAsync(entryStream);
						}
						LogService.Log("[PatchService] Helpers data added to patch");
					}
					if (includeXdeltaFallback && exactPatchSourcePath == null)
					{
						XDeltaService xDeltaService2 = new XDeltaService();
						tempExactPatch = Path.Combine(Path.GetTempPath(), $"g3mtool_xdelta_fallback_{Guid.NewGuid():N}.xdelta");
						XDeltaResult exactResult = await xDeltaService2.CreatePatchAsync(originalPath, modifiedPath, tempExactPatch);
						if (exactResult.Success && File.Exists(tempExactPatch))
						{
							exactPatchSourcePath = tempExactPatch;
							LogService.Log("[PatchService] Embedded xdelta fallback");
						}
						else
						{
							LogService.Warning("[PatchService] Xdelta fallback was not created: " + exactResult.Error);
						}
					}
					if (includeXdeltaFallback && exactPatchSourcePath != null && File.Exists(exactPatchSourcePath))
					{
						using Stream entryStream = await archive.CreateEntry("Xdelta/" + Path.GetFileName(exactPatchSourcePath), CompressionLevel.Fastest).OpenAsync();
						using FileStream fileStream = File.OpenRead(exactPatchSourcePath);
						await fileStream.CopyToAsync(entryStream);
					}
				}
				LogInterpolatedStringHandler handler13 = new LogInterpolatedStringHandler(23, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler13.AppendLiteral("[Timing] Patch write: ");
					handler13.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
					handler13.AppendLiteral("s");
				}
				LogService.Log(ref handler13);
				LogService.Progress(100, 100);
				LogService.ProgressComplete();
				totalSw.Stop();
				LogInterpolatedStringHandler handler14 = new LogInterpolatedStringHandler(46, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler14.AppendLiteral("[Timing] === PATCH CREATE TOTAL: ");
					handler14.AppendFormatted(totalSw.Elapsed.TotalSeconds, "F1");
					handler14.AppendLiteral("s === RAM: ");
					handler14.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler14.AppendLiteral("MB");
				}
				LogService.Log(ref handler14);
				return new PatchCreateResult
				{
					Success = true,
					Statistics = manifest.Statistics,
					Message = FormatPatchStats(manifest.Statistics)
				};
			}
			finally
			{
				try
				{
					Directory.Delete(tempDir2, recursive: true);
				}
				catch
				{
				}
				if (tempXdeltaResult != null)
				{
					try
					{
						File.Delete(tempXdeltaResult);
					}
					catch
					{
					}
				}
				if (tempExactPatch != null)
				{
					try
					{
						File.Delete(tempExactPatch);
					}
					catch
					{
					}
				}
			}
		}
		catch (Exception ex)
		{
			if (tempXdeltaResult != null)
			{
				try
				{
					File.Delete(tempXdeltaResult);
				}
				catch
				{
				}
			}
			if (tempExactPatch != null)
			{
				try
				{
					File.Delete(tempExactPatch);
				}
				catch
				{
				}
			}
			return new PatchCreateResult
			{
				Success = false,
				Error = "Failed to create patch: " + ex.Message
			};
		}
	}

	private static Dictionary<string, Dictionary<string, int>> ReadResourceNameCounts(string dataPath)
	{
		using FileStream stream = OpenDataReadStream(dataPath);
		using GameMakerData data = GameMakerIO.Read(stream);
		return GetResourceNameCounts(data);
	}

	private static Dictionary<string, Dictionary<string, int>> GetResourceNameCounts(GameMakerData data)
	{
		return new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
		{
			["GameObjects"] = CountResourceNames(data.GameObjects),
			["Sprites"] = CountResourceNames(data.Sprites),
			["Sounds"] = CountResourceNames(data.Sounds),
			["Rooms"] = CountResourceNames(data.Rooms),
			["Scripts"] = CountResourceNames(data.Scripts),
			["Fonts"] = CountResourceNames(data.Fonts),
			["Backgrounds"] = CountResourceNames(data.Backgrounds),
			["Paths"] = CountResourceNames(data.Paths),
			["Timelines"] = CountResourceNames(data.Timelines),
			["Shaders"] = CountResourceNames(data.Shaders),
			["Extensions"] = CountResourceNames(data.Extensions),
			["AudioGroups"] = CountResourceNames(data.AudioGroups)
		};
	}

	private static Dictionary<string, int> CountResourceNames<T>(IList<T>? list) where T : GameMakerNamedResource
	{
		Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
		if (list == null)
		{
			return counts;
		}
		foreach (T item2 in list)
		{
			string name = item2?.Name?.Content;
			if (!string.IsNullOrEmpty(name))
			{
				counts[name] = counts.GetValueOrDefault(name) + 1;
			}
		}
		return counts;
	}

	private static void AddDuplicateCountChanges(Dictionary<string, Dictionary<string, int>> originalNameCounts, GameMakerData modifiedData, Dictionary<string, HashSet<string>> changedNamesPerType)
	{
		AddDuplicateCountChangesForType("GameObjects", originalNameCounts, modifiedData.GameObjects, changedNamesPerType);
		AddDuplicateCountChangesForType("Sprites", originalNameCounts, modifiedData.Sprites, changedNamesPerType);
		AddDuplicateCountChangesForType("Sounds", originalNameCounts, modifiedData.Sounds, changedNamesPerType);
		AddDuplicateCountChangesForType("Rooms", originalNameCounts, modifiedData.Rooms, changedNamesPerType);
		AddDuplicateCountChangesForType("Scripts", originalNameCounts, modifiedData.Scripts, changedNamesPerType);
		AddDuplicateCountChangesForType("Fonts", originalNameCounts, modifiedData.Fonts, changedNamesPerType);
		AddDuplicateCountChangesForType("Backgrounds", originalNameCounts, modifiedData.Backgrounds, changedNamesPerType);
		AddDuplicateCountChangesForType("Paths", originalNameCounts, modifiedData.Paths, changedNamesPerType);
		AddDuplicateCountChangesForType("Timelines", originalNameCounts, modifiedData.Timelines, changedNamesPerType);
		AddDuplicateCountChangesForType("Shaders", originalNameCounts, modifiedData.Shaders, changedNamesPerType);
		AddDuplicateCountChangesForType("Extensions", originalNameCounts, modifiedData.Extensions, changedNamesPerType);
		AddDuplicateCountChangesForType("AudioGroups", originalNameCounts, modifiedData.AudioGroups, changedNamesPerType);
	}

	private static Dictionary<string, HashSet<string>> CloneChangedNameMap(Dictionary<string, HashSet<string>> source)
	{
		return source.ToDictionary<KeyValuePair<string, HashSet<string>>, string, HashSet<string>>((KeyValuePair<string, HashSet<string>> pair) => pair.Key, (KeyValuePair<string, HashSet<string>> pair) => new HashSet<string>(pair.Value, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
	}

	private static void ExportPatchHelpers(GameMakerData modifiedData, string helpersExportDir, Dictionary<string, Dictionary<string, string>> originalHashes, Dictionary<string, Dictionary<string, string>> modifiedHashes, Dictionary<string, HashSet<string>> changedNamesPerType, HashSet<string> helperForcedResourceTypes, int originalCodeEntryCount, bool originalCodeEntryCountRecovered, int modifiedCodeEntryCount, Dictionary<string, (string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms)> codeEntriesInMemory)
	{
		HashSet<string> helperRelevantResourceTypes = GetHelperRelevantResourceTypes(originalHashes, modifiedHashes, changedNamesPerType);
		foreach (string forcedType in helperForcedResourceTypes)
		{
			helperRelevantResourceTypes.Add(forcedType);
		}
		if (!RequiresPatchHelpers(helperRelevantResourceTypes, helperForcedResourceTypes))
		{
			return;
		}
		HashSet<string> changedCodeNames = changedNamesPerType.GetValueOrDefault("CodeEntries") ?? new HashSet<string>();
		if (CanUseCompactCodeMetadata(originalHashes, modifiedHashes, helperRelevantResourceTypes, helperForcedResourceTypes, originalCodeEntryCount, originalCodeEntryCountRecovered, modifiedCodeEntryCount, changedCodeNames, codeEntriesInMemory))
		{
			bool includeAssetOrder = helperRelevantResourceTypes.Any((string type) => !type.Equals("CodeEntries", StringComparison.OrdinalIgnoreCase));
			ResourceExportService.ExportAssetOrder(modifiedData, helpersExportDir, RequiresObjectEventsHelper(changedNamesPerType), includeVariablesFunctions: false, RequiresTextureMappingHelpers(helperRelevantResourceTypes), includeAssetOrder ? BuildRelevantAssetOrderSections(helperRelevantResourceTypes, includeScriptsForCodeEntries: true) : null, includeAssetOrder, changedCodeNames);
			LogService.Log("[PatchService] Using compact code metadata for index-stable ASM code changes.");
		}
		else
		{
			bool includeVariablesFunctions = RequiresVariableFunctionsHelper(helperRelevantResourceTypes);
			bool includeTextureHelpers = RequiresTextureMappingHelpers(helperRelevantResourceTypes);
			HashSet<string> assetOrderSections = BuildRelevantAssetOrderSections(helperRelevantResourceTypes, changedCodeNames.Count > 0);
			ResourceExportService.ExportAssetOrder(modifiedData, helpersExportDir, RequiresObjectEventsHelper(changedNamesPerType), includeVariablesFunctions, includeTextureHelpers, assetOrderSections);
		}
	}

	private static bool CanUseCompactCodeMetadata(Dictionary<string, Dictionary<string, string>> originalHashes, Dictionary<string, Dictionary<string, string>> modifiedHashes, HashSet<string> helperRelevantResourceTypes, HashSet<string> helperForcedResourceTypes, int originalCodeEntryCount, bool originalCodeEntryCountRecovered, int modifiedCodeEntryCount, HashSet<string> changedCodeNames, Dictionary<string, (string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms)> codeEntriesInMemory)
	{
		if (helperForcedResourceTypes.Count != 0 || !helperRelevantResourceTypes.Contains("CodeEntries") || originalCodeEntryCount < 0 || (!originalCodeEntryCountRecovered && originalCodeEntryCount != modifiedCodeEntryCount) || changedCodeNames.Count == 0 || codeEntriesInMemory.Count == 0 || codeEntriesInMemory.Values.Any<(string, string, string, Dictionary<string, (string, string)>)>(((string LogicalName, string gml, string asm, Dictionary<string, (string LogicalName, string Asm)> childAsms) entry) => entry.asm == null))
		{
			return false;
		}
		string[] allTypes = ResourceTypeRegistry.AllTypes;
		foreach (string resourceType in allTypes)
		{
			IEnumerable<string> enumerable = originalHashes.GetValueOrDefault(resourceType)?.Keys;
			IEnumerable<string> source = enumerable ?? Enumerable.Empty<string>();
			enumerable = modifiedHashes.GetValueOrDefault(resourceType)?.Keys;
			IEnumerable<string> modifiedNames = enumerable ?? Enumerable.Empty<string>();
			if (!source.ToHashSet<string>(StringComparer.Ordinal).SetEquals(modifiedNames))
			{
				return false;
			}
		}
		return true;
	}

	private static void ExpandChangedCodeEntriesForTopology(GameMakerData modifiedData, Dictionary<string, HashSet<string>> changedNamesPerType)
	{
		if (!changedNamesPerType.TryGetValue("CodeEntries", out HashSet<string> changedCode) || changedCode.Count == 0)
		{
			return;
		}
		int addedParents = 0;
		int addedChildren = 0;
		bool changed;
		do
		{
			changed = false;
			foreach (GameMakerCode code in modifiedData.Code ?? new List<GameMakerCode>())
			{
				string codeName = code?.Name?.Content;
				if (code == null || string.IsNullOrEmpty(codeName))
				{
					continue;
				}
				string parentName = code.ParentEntry?.Name?.Content;
				if (!string.IsNullOrEmpty(parentName) && changedCode.Contains(codeName) && changedCode.Add(parentName))
				{
					addedParents++;
					changed = true;
				}
				if (!changedCode.Contains(codeName))
				{
					continue;
				}
				foreach (GameMakerCode item in code.ChildEntries ?? new List<GameMakerCode>())
				{
					string childName = item?.Name?.Content;
					if (!string.IsNullOrEmpty(childName) && changedCode.Add(childName))
					{
						addedChildren++;
						changed = true;
					}
				}
			}
		}
		while (changed);
		if (addedParents > 0 || addedChildren > 0)
		{
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(73, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Expanded CodeEntries topology: +");
				handler.AppendFormatted(addedParents);
				handler.AppendLiteral(" parent, +");
				handler.AppendFormatted(addedChildren);
				handler.AppendLiteral(" child entrie(s)");
			}
			LogService.Log(ref handler);
		}
	}

	private static void AddDuplicateCountChangesForType<T>(string resourceType, Dictionary<string, Dictionary<string, int>> originalNameCounts, IList<T>? modified, Dictionary<string, HashSet<string>> changedNamesPerType) where T : GameMakerNamedResource
	{
		if (modified == null)
		{
			return;
		}
		originalNameCounts.TryGetValue(resourceType, out Dictionary<string, int> originalCounts);
		if (originalCounts == null)
		{
			originalCounts = new Dictionary<string, int>();
		}
		Dictionary<string, int> modifiedCounts = CountResourceNames(modified);
		if (modifiedCounts.Any((KeyValuePair<string, int> pair) => originalCounts.GetValueOrDefault(pair.Key) != pair.Value) && resourceType.Equals("GameObjects", StringComparison.OrdinalIgnoreCase))
		{
			HashSet<string> hashSet = new HashSet<string>();
			foreach (string key in modifiedCounts.Keys)
			{
				hashSet.Add(key);
			}
			changedNamesPerType[resourceType] = hashSet;
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(66, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] ");
				handler.AppendFormatted(resourceType);
				handler.AppendLiteral(": duplicate/name count changed, exporting full type");
			}
			LogService.Log(ref handler);
			return;
		}
		foreach (var (name, count) in modifiedCounts)
		{
			if (originalCounts.GetValueOrDefault(name) != count)
			{
				if (!changedNamesPerType.TryGetValue(resourceType, out HashSet<string> changedNames))
				{
					changedNames = (changedNamesPerType[resourceType] = new HashSet<string>());
				}
				changedNames.Add(name);
			}
		}
	}

	private static void PromoteVersionSensitiveExportsIfNeeded(GeneralInfoData? originalInfo, GeneralInfoData? modifiedInfo, Dictionary<string, Dictionary<string, string>> modifiedHashes, Dictionary<string, HashSet<string>> changedNamesPerType)
	{
		if (originalInfo != null && modifiedInfo != null && (originalInfo.Major != modifiedInfo.Major || originalInfo.Minor != modifiedInfo.Minor || originalInfo.Release != modifiedInfo.Release || originalInfo.Build != modifiedInfo.Build))
		{
			PromoteResourceTypeToFullExport(changedNamesPerType, modifiedHashes, "Rooms");
			PromoteResourceTypeToFullExport(changedNamesPerType, modifiedHashes, "TextureGroupInfo");
		}
	}

	private static void PromoteOrderSensitiveExportsIfNeeded(Dictionary<string, IReadOnlyList<string>> originalOrderedNames, GameMakerData modifiedData, Dictionary<string, Dictionary<string, string>> modifiedHashes, Dictionary<string, HashSet<string>> changedNamesPerType, HashSet<string> helperForcedResourceTypes)
	{
		PromoteOrderSensitiveExportIfNeeded("Rooms", originalOrderedNames, modifiedData.Rooms?.Select((GameMakerRoom r) => r?.Name?.Content ?? "").ToList(), modifiedHashes, changedNamesPerType, helperForcedResourceTypes, forceHelpers: true);
		PromoteOrderSensitiveExportIfNeeded("Scripts", originalOrderedNames, modifiedData.Scripts?.Select((GameMakerScript s) => s?.Name?.Content ?? "").ToList(), modifiedHashes, changedNamesPerType, helperForcedResourceTypes, forceHelpers: false);
	}

	private static void PromoteOrderSensitiveExportIfNeeded(string resourceType, Dictionary<string, IReadOnlyList<string>> originalOrderedNames, IReadOnlyList<string>? modifiedOrderedNames, Dictionary<string, Dictionary<string, string>> modifiedHashes, Dictionary<string, HashSet<string>> changedNamesPerType, HashSet<string> helperForcedResourceTypes, bool forceHelpers)
	{
		if (!originalOrderedNames.TryGetValue(resourceType, out IReadOnlyList<string> originalNames) || modifiedOrderedNames == null || originalNames.SequenceEqual<string>(modifiedOrderedNames, StringComparer.Ordinal))
		{
			return;
		}
		if (resourceType.Equals("Scripts", StringComparison.OrdinalIgnoreCase))
		{
			helperForcedResourceTypes.Add(resourceType);
			LogService.Log("[PatchService] Scripts: order changed, preserving asset order without exporting unchanged scripts");
			return;
		}
		PromoteResourceTypeToFullExport(changedNamesPerType, modifiedHashes, resourceType);
		bool shouldAppend;
		if (forceHelpers)
		{
			helperForcedResourceTypes.Add(resourceType);
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(83, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] ");
				handler.AppendFormatted(resourceType);
				handler.AppendLiteral(": order changed, exporting full type and forcing asset-order helpers");
			}
			LogService.Log(ref handler);
		}
		else
		{
			LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(51, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler2.AppendLiteral("[PatchService] ");
				handler2.AppendFormatted(resourceType);
				handler2.AppendLiteral(": order changed, exporting full type");
			}
			LogService.Log(ref handler2);
		}
	}

	private static void PromoteResourceTypeToFullExport(Dictionary<string, HashSet<string>> changedNamesPerType, Dictionary<string, Dictionary<string, string>> modifiedHashes, string resourceType)
	{
		if (!modifiedHashes.TryGetValue(resourceType, out Dictionary<string, string> typeHashes) || typeHashes.Count == 0)
		{
			return;
		}
		HashSet<string> hashSet = new HashSet<string>();
		foreach (string key in typeHashes.Keys)
		{
			hashSet.Add(key);
		}
		changedNamesPerType[resourceType] = hashSet;
		bool shouldAppend;
		LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(64, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[PatchService] Version change detected, exporting all ");
			handler.AppendFormatted(resourceType);
			handler.AppendLiteral(" resources");
		}
		LogService.Log(ref handler);
	}

	private static Dictionary<string, IReadOnlyList<string>> ReadOrderSensitiveResourceNames(string dataPath)
	{
		using FileStream stream = OpenDataReadStream(dataPath);
		using GameMakerData data = GameMakerIO.Read(stream);
		return GetOrderSensitiveResourceNames(data);
	}

	private static Dictionary<string, IReadOnlyList<string>> GetOrderSensitiveResourceNames(GameMakerData data)
	{
		return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
		{
			["Rooms"] = data.Rooms?.Select((GameMakerRoom r) => r?.Name?.Content ?? "").ToList() ?? new List<string>(),
			["Scripts"] = data.Scripts?.Select((GameMakerScript s) => s?.Name?.Content ?? "").ToList() ?? new List<string>()
		};
	}

	public static Dictionary<string, Dictionary<string, int>> GetResourceNameCountsForReuse(GameMakerData data)
	{
		return GetResourceNameCounts(data);
	}

	public static Dictionary<string, IReadOnlyList<string>> GetOrderSensitiveResourceNamesForReuse(GameMakerData data)
	{
		return GetOrderSensitiveResourceNames(data);
	}

	private static ResourceTypeChanges BuildResourceTypeChangesFromKnownDifferences(Dictionary<string, string> originalHashes, Dictionary<string, string> modifiedHashes, string modifiedExportDir, string resourceType, HashSet<string>? knownChangedOrNewNames)
	{
		ResourceTypeChanges changes = new ResourceTypeChanges
		{
			Changed = new List<ResourceChange>(),
			New = new List<ResourceChange>(),
			Deleted = new List<string>()
		};
		string modifiedResDir = Path.Combine(modifiedExportDir, resourceType);
		Dictionary<string, string> folderMap = BuildBaseNameMap(modifiedResDir);
		if (knownChangedOrNewNames != null)
		{
			foreach (string name in knownChangedOrNewNames)
			{
				List<string> folderNames = new List<string>();
				if (folderMap.TryGetValue(name, out var mappedFolder))
				{
					folderNames.Add(mappedFolder);
				}
				else
				{
					folderNames = GetFolderNamesForBaseName(modifiedResDir, name);
				}
				if (folderNames.Count == 0)
				{
					folderNames.Add(name);
				}
				bool isNew = !originalHashes.ContainsKey(name) && modifiedHashes.ContainsKey(name);
				foreach (string folderName in folderNames)
				{
					if (isNew)
					{
						changes.New.Add(CreateResourceChange(modifiedResDir, resourceType, folderName));
					}
					else
					{
						changes.Changed.Add(CreateResourceChange(modifiedResDir, resourceType, folderName));
					}
				}
			}
		}
		foreach (string name2 in originalHashes.Keys.Intersect(modifiedHashes.Keys))
		{
			if ((knownChangedOrNewNames != null && knownChangedOrNewNames.Contains(name2)) || originalHashes[name2] == modifiedHashes[name2])
			{
				continue;
			}
			List<string> folderNames2 = new List<string>();
			if (folderMap.TryGetValue(name2, out var mappedFolder2))
			{
				folderNames2.Add(mappedFolder2);
			}
			else
			{
				folderNames2 = GetFolderNamesForBaseName(modifiedResDir, name2);
			}
			if (folderNames2.Count == 0)
			{
				folderNames2.Add(name2);
			}
			foreach (string folderName2 in folderNames2)
			{
				changes.Changed.Add(CreateResourceChange(modifiedResDir, resourceType, folderName2));
			}
		}
		foreach (string name3 in originalHashes.Keys.Except(modifiedHashes.Keys))
		{
			changes.Deleted.Add(name3);
		}
		return changes;
	}

	private static List<string> GetFolderNamesForBaseName(string directory, string baseName)
	{
		if (!Directory.Exists(directory))
		{
			return new List<string>();
		}
		List<string> result = new List<string>();
		string[] directories = Directory.GetDirectories(directory);
		for (int i = 0; i < directories.Length; i++)
		{
			string folderName = Path.GetFileName(directories[i]);
			if (string.Equals(StripIdxSuffix(folderName), baseName, StringComparison.Ordinal))
			{
				result.Add(folderName);
			}
		}
		return result;
	}

	private static ResourceChange CreateResourceChange(string sourceDir, string resourceType, string resourceName)
	{
		return new ResourceChange
		{
			Name = resourceName,
			Files = BuildResourceFileMap(sourceDir, resourceType, resourceName)
		};
	}

	private static void PopulateCodeEntryFiles(ResourceTypeChanges changes, Dictionary<string, (string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms)> codeEntriesInMemory)
	{
		foreach (ResourceChange change in (changes.Changed ?? new List<ResourceChange>()).Concat(changes.New ?? new List<ResourceChange>()))
		{
			if (string.IsNullOrWhiteSpace(change.Name) || !codeEntriesInMemory.TryGetValue(change.Name, out (string, string, string, Dictionary<string, (string, string)>) entry))
			{
				ResourceChange resourceChange = change;
				if (resourceChange.Files == null)
				{
					Dictionary<string, string> dictionary = (resourceChange.Files = new Dictionary<string, string>());
				}
				continue;
			}
			Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (entry.Item2 != null)
			{
				files[change.Name + ".gml"] = $"CodeEntries/{change.Name}/{change.Name}.gml";
			}
			if (entry.Item3 != null)
			{
				files[change.Name + ".asm"] = $"CodeEntries/{change.Name}/{change.Name}.asm";
			}
			if (entry.Item4 != null)
			{
				foreach (var (childLeafKey, _) in entry.Item4)
				{
					files[childLeafKey + ".asm"] = $"CodeEntries/{change.Name}/{childLeafKey}.asm";
				}
			}
			change.Files = files;
		}
	}

	private static Dictionary<string, string> BuildResourceFileMap(string sourceDir, string resourceType, string resourceName)
	{
		string resourcePath = Path.Combine(sourceDir, resourceName);
		Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string[] files2;
		if (!Directory.Exists(resourcePath) && resourceName == Path.GetFileName(sourceDir))
		{
			if (!Directory.Exists(sourceDir))
			{
				return files;
			}
			files2 = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
			foreach (string file in files2)
			{
				string relativeToResource = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
				string entryPath = Path.Combine(resourceType, relativeToResource).Replace('\\', '/');
				files[relativeToResource] = entryPath;
			}
			return files;
		}
		if (!Directory.Exists(resourcePath))
		{
			return files;
		}
		files2 = Directory.GetFiles(resourcePath, "*", SearchOption.AllDirectories);
		foreach (string file2 in files2)
		{
			string relativeToResource2 = Path.GetRelativePath(resourcePath, file2).Replace('\\', '/');
			string entryPath2 = Path.Combine(resourceType, resourceName, relativeToResource2).Replace('\\', '/');
			files[relativeToResource2] = entryPath2;
		}
		return files;
	}

	private static Dictionary<string, string> BuildBaseNameMap(string directory)
	{
		Dictionary<string, string> map = new Dictionary<string, string>();
		if (!Directory.Exists(directory))
		{
			return map;
		}
		Dictionary<string, int> baseNameCounts = new Dictionary<string, int>();
		List<(string, string)> entries = new List<(string, string)>();
		string[] directories = Directory.GetDirectories(directory);
		for (int i = 0; i < directories.Length; i++)
		{
			string folderName = Path.GetFileName(directories[i]);
			string baseName = StripIdxSuffix(folderName);
			entries.Add((folderName, baseName));
			baseNameCounts[baseName] = baseNameCounts.GetValueOrDefault(baseName) + 1;
		}
		foreach (var (folderName2, baseName2) in entries)
		{
			if (baseNameCounts[baseName2] > 1)
			{
				map[folderName2] = folderName2;
			}
			else
			{
				map[baseName2] = folderName2;
			}
		}
		return map;
	}

	private static string StripIdxSuffix(string folderName)
	{
		int idxPos = folderName.LastIndexOf("__idx");
		if (idxPos <= 0)
		{
			return folderName;
		}
		string after = folderName.Substring(idxPos + 5);
		if (after.Length > 0 && after.All(char.IsDigit))
		{
			return folderName.Substring(0, idxPos);
		}
		return folderName;
	}

	private static async Task AddResourceToArchiveAsync(ZipArchive archive, string sourceDir, string resourceType, string resourceName, Dictionary<string, string>? filesToInclude = null)
	{
		string resourcePath = Path.Combine(sourceDir, resourceName);
		if (!Directory.Exists(resourcePath) && resourceName == Path.GetFileName(sourceDir))
		{
			if (!Directory.Exists(sourceDir))
			{
				return;
			}
			string[] files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
			foreach (string file in files)
			{
				string relativeToResource = Path.GetRelativePath(sourceDir, file);
				if (filesToInclude != null && !filesToInclude.ContainsKey(relativeToResource))
				{
					continue;
				}
				string entryPath = Path.Combine(resourceType, relativeToResource).Replace('\\', '/');
				using Stream entryStream = await archive.CreateEntry(entryPath, ArchiveCompressionUtil.GetLevel(entryPath)).OpenAsync();
				using FileStream fileStream = File.OpenRead(file);
				await fileStream.CopyToAsync(entryStream);
			}
		}
		else
		{
			if (!Directory.Exists(resourcePath))
			{
				return;
			}
			string[] files = Directory.GetFiles(resourcePath, "*", SearchOption.AllDirectories);
			foreach (string file in files)
			{
				string relativeToResource2 = Path.GetRelativePath(resourcePath, file);
				if (filesToInclude != null && !filesToInclude.ContainsKey(relativeToResource2))
				{
					continue;
				}
				string entryPath2 = Path.Combine(resourceType, resourceName, relativeToResource2).Replace('\\', '/');
				using Stream entryStream = await archive.CreateEntry(entryPath2, ArchiveCompressionUtil.GetLevel(entryPath2)).OpenAsync();
				using FileStream fileStream = File.OpenRead(file);
				await fileStream.CopyToAsync(entryStream);
			}
		}
	}

	public static async Task<PatchApplyResult> ApplyPatchAsync(string dataPath, string patchPath, string outputPath, bool allowXdeltaFallback = false, bool verifyModifiedHash = true)
	{
		if (!File.Exists(dataPath))
		{
			return new PatchApplyResult
			{
				Success = false,
				Error = "Data file not found: " + dataPath
			};
		}
		if (!File.Exists(patchPath))
		{
			return new PatchApplyResult
			{
				Success = false,
				Error = "Patch file not found: " + patchPath
			};
		}
		PatchFileSystem pfsForFallback = null;
		G3MPatchManifest manifestForFallback = null;
		bool shouldAppend;
		try
		{
			Stopwatch totalSw = Stopwatch.StartNew();
			Stopwatch phaseSw = new Stopwatch();
			Stopwatch patchLoadSw = new Stopwatch();
			Stopwatch dataLoadSw = new Stopwatch();
			Stopwatch nonCodeImportSw = new Stopwatch();
			Stopwatch codeImportSw = new Stopwatch();
			Stopwatch finalizeSw = new Stopwatch();
			Stopwatch saveSw = new Stopwatch();
			Stopwatch verifyHashSw = new Stopwatch();
			Stopwatch preCodeReorderSw = new Stopwatch();
			Stopwatch postSpriteReorderSw = new Stopwatch();
			Stopwatch finalReorderSw = new Stopwatch();
			Stopwatch repairDuplicatesSw = new Stopwatch();
			Stopwatch applyEventsSw = new Stopwatch();
			Stopwatch repairDanglingCodeSw = new Stopwatch();
			Stopwatch ensureScriptsSw = new Stopwatch();
			Stopwatch integritySw = new Stopwatch();
			LogService.SetOperation("Applying patch");
			LogService.Progress(0, 100);
			patchLoadSw.Start();
			phaseSw.Restart();
			LogService.Log("[PatchService] Loading .g3mpatch file into memory...");
			PatchFileSystem pfs = await Task.Run(() => PatchFileSystem.LoadFromZip(patchPath, allowXdeltaFallback));
			G3MPatchManifest manifest = pfs.Manifest;
			patchLoadSw.Stop();
			pfsForFallback = pfs;
			manifestForFallback = manifest;
			shouldAppend = allowXdeltaFallback;
			if (shouldAppend)
			{
				shouldAppend = await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfs, manifest);
			}
			if (shouldAppend)
			{
				LogService.Progress(100, 100);
				LogService.ProgressComplete();
				return new PatchApplyResult
				{
					Success = true
				};
			}
			PatchApplyPlan applyPlan = manifest?.ApplyPlan ?? BuildPatchApplyPlan(manifest?.Resources ?? new Dictionary<string, ResourceTypeChanges>(StringComparer.OrdinalIgnoreCase));
			dataLoadSw.Start();
			phaseSw.Restart();
			LogService.Log("[PatchService] Loading data file for .g3mpatch apply...");
			GameMakerData data;
			using (FileStream stream = OpenDataReadStream(dataPath))
			{
				data = GameMakerIO.Read(stream);
			}
			dataLoadSw.Stop();
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(30, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[Timing] Data load: ");
				handler.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
				handler.AppendLiteral("s, RAM: ");
				handler.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
				handler.AppendLiteral("MB");
			}
			LogService.Log(ref handler);
			LogService.Progress(5, 100);
			LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(28, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler2.AppendLiteral("[PatchService] Data loaded: ");
				handler2.AppendFormatted(data.GeneralInfo?.DisplayName?.Content ?? "Unknown");
			}
			LogService.Log(ref handler2);
			ResourceImportService.SetPatchFileSystem(pfs);
			try
			{
				List<string> resourceTypesToProcess = BuildResourceTypesToProcess(pfs, manifest);
				bool scriptOrderOnly = resourceTypesToProcess.Count == 0 && HasScriptOrderOnlyAssetPayload(pfs);
				if (scriptOrderOnly)
				{
					resourceTypesToProcess.Add("Scripts");
				}
				LogInterpolatedStringHandler handler3 = new LogInterpolatedStringHandler(37, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler3.AppendLiteral("[PatchService] Resources to process: ");
					handler3.AppendFormatted(string.Join(", ", resourceTypesToProcess));
				}
				LogService.Log(ref handler3);
				if (resourceTypesToProcess.Count == 0)
				{
					LogService.Log("[PatchService] No resources to apply");
					string noOpOutDir = Path.GetDirectoryName(outputPath);
					if (!string.IsNullOrEmpty(noOpOutDir) && !Directory.Exists(noOpOutDir))
					{
						Directory.CreateDirectory(noOpOutDir);
					}
					File.Copy(dataPath, outputPath, overwrite: true);
					LogService.Progress(100, 100);
					LogService.ProgressComplete();
					return new PatchApplyResult
					{
						Success = true
					};
				}
				if (!scriptOrderOnly && CanUseMinimalStandardApply(applyPlan, resourceTypesToProcess))
				{
					LogInterpolatedStringHandler handler4 = new LogInterpolatedStringHandler(58, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler4.AppendLiteral("[PatchService] Applying only required resource functions: ");
						handler4.AppendFormatted(string.Join(", ", resourceTypesToProcess));
					}
					LogService.Log(ref handler4);
					(int minimalAppliedCount, int minimalFailedCount) = ImportMinimalResources(data, pfs, resourceTypesToProcess, dataPath, outputPath);
					if (minimalFailedCount > 0 || minimalAppliedCount == 0)
					{
						return new PatchApplyResult
						{
							Success = false,
							Error = ((minimalFailedCount > 0) ? $"Patch apply failed: {minimalFailedCount} resource type(s) failed during import; output was not written" : "No resources were applied successfully")
						};
					}
					DataIntegrityResult minimalIntegrity = DataIntegrityService.ValidateOnly(data);
					if (!minimalIntegrity.Success)
					{
						return new PatchApplyResult
						{
							Success = false,
							Error = "Integrity validation failed: " + string.Join("; ", minimalIntegrity.Errors.Take(20))
						};
					}
					phaseSw.Restart();
					LogService.Log("[PatchService] Saving modified data file...");
					saveSw.Start();
					string minimalOutDir = Path.GetDirectoryName(outputPath);
					if (!string.IsNullOrEmpty(minimalOutDir) && !Directory.Exists(minimalOutDir))
					{
						Directory.CreateDirectory(minimalOutDir);
					}
					using (FileStream outStream = OpenDataWriteStream(outputPath))
					{
						GameMakerIO.Write(outStream, data);
					}
					saveSw.Stop();
					string minimalValidationError = ValidateSavedDataFile(outputPath);
					if (minimalValidationError != null)
					{
						DeleteInvalidOutput(outputPath);
						return new PatchApplyResult
						{
							Success = false,
							Error = "Saved data validation failed: " + minimalValidationError
						};
					}
					totalSw.Stop();
					LogInterpolatedStringHandler handler5 = new LogInterpolatedStringHandler(31, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler5.AppendLiteral("[Timing] Minimal apply total: ");
						handler5.AppendFormatted(totalSw.Elapsed.TotalSeconds, "F1");
						handler5.AppendLiteral("s");
					}
					LogService.Log(ref handler5);
					return new PatchApplyResult
					{
						Success = true
					};
				}
				bool hasCodeEntries = CreateImportProgressState(resourceTypesToProcess).HasCodeEntries;
				LogService.Progress(5, 100);
				PatchApplyPreparation preparation = PreparePatchApply(data, pfs, resourceTypesToProcess, applyPlan, hasCodeEntries, scriptOrderOnly);
				int failedCount;
				int appliedCount;
				(appliedCount, failedCount) = ImportPatchResources(data, pfs, manifest, resourceTypesToProcess, preparation, hasCodeEntries, dataPath, outputPath, nonCodeImportSw, codeImportSw, phaseSw, preCodeReorderSw, postSpriteReorderSw, logDetailedCodeImport: true);
				finalizeSw.Start();
				appliedCount += (ApplyFinalAssetOrder(preparation.NeedsFinalAssetReorder, data, pfs, resourceTypesToProcess, preparation.Helpers, preparation.StagedEmbeddedTexturesDir, scriptOrderOnly, finalReorderSw) ? 1 : 0);
				if (preparation.VariablesFunctionsContent != null && preparation.OriginalGmlForCodeRemap.Count > 0)
				{
					preparation.GmlCompilationRan |= RecompileUntouchedCodeForAssetOrderShifts(data, preparation.AssetNamesAtCodeImportStart, preparation.OriginalGmlForCodeRemap, preparation.PatchCodeEntryNames, preparation.Helpers.HelpersDir, preparation.VariablesFunctionsContent);
				}
				RepairImportedResourceStructures(data, preparation.UntouchedEmbeddedTextureSnapshot, resourceTypesToProcess, manifest, repairDuplicatesSw);
				ApplyCapturedObjectEvents(data, resourceTypesToProcess, manifest, preparation.Helpers.ObjectEventsBytes, applyEventsSw);
				if (!preparation.GmlCompilationRan)
				{
					PruneCodeEntriesOutsideTargetMetadata(data, preparation.CodeMetadataForFinalCleanup ?? ReadCodeMetadataContent(pfs, preparation.Helpers.HelpersDir));
				}
				PruneDeletedScriptOrphans(data, manifest);
				PruneMissingScriptCodeOrphans(data);
				DataIntegrityResult integrity = RepairAndValidateImportedData(data, resourceTypesToProcess, preparation.NeedsHeavyFinalize, repairDanglingCodeSw, ensureScriptsSw, integritySw);
				if (integrity != null && !integrity.Success)
				{
					if (await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfs, manifest))
					{
						return new PatchApplyResult
						{
							Success = true
						};
					}
					return new PatchApplyResult
					{
						Success = false,
						Error = "Integrity validation failed: " + string.Join("; ", integrity.Errors.Take(20))
					};
				}
				RepairLocalChildFunctionReferencesFinal(data);
				if (failedCount > 0)
				{
					finalizeSw.Stop();
					if (await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfs, manifest))
					{
						return new PatchApplyResult
						{
							Success = true
						};
					}
					return new PatchApplyResult
					{
						Success = false,
						Error = $"Patch apply failed: {failedCount} resource type(s) failed during import; output was not written"
					};
				}
				phaseSw.Restart();
				LogService.Log("[PatchService] Saving modified data file...");
				saveSw.Start();
				string outDir = Path.GetDirectoryName(outputPath);
				if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
				{
					Directory.CreateDirectory(outDir);
				}
				using (FileStream outStream2 = OpenDataWriteStream(outputPath))
				{
					GameMakerIO.Write(outStream2, data);
				}
				saveSw.Stop();
				string savedValidationError = ValidateSavedDataFile(outputPath);
				if (savedValidationError != null)
				{
					DeleteInvalidOutput(outputPath);
					finalizeSw.Stop();
					if (await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfs, manifest))
					{
						return new PatchApplyResult
						{
							Success = true
						};
					}
					return new PatchApplyResult
					{
						Success = false,
						Error = "Saved data validation failed: " + savedValidationError
					};
				}
				finalizeSw.Stop();
				LogInterpolatedStringHandler handler6 = new LogInterpolatedStringHandler(31, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler6.AppendLiteral("[Timing] Final save: ");
					handler6.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
					handler6.AppendLiteral("s, RAM: ");
					handler6.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler6.AppendLiteral("MB");
				}
				LogService.Log(ref handler6);
				LogService.Progress(100, 100);
				LogService.ProgressComplete();
				totalSw.Stop();
				LogInterpolatedStringHandler handler7 = new LogInterpolatedStringHandler(45, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler7.AppendLiteral("[Timing] === PATCH APPLY TOTAL: ");
					handler7.AppendFormatted(totalSw.Elapsed.TotalSeconds, "F1");
					handler7.AppendLiteral("s === RAM: ");
					handler7.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler7.AppendLiteral("MB");
				}
				LogService.Log(ref handler7);
				string expectedModifiedHash = manifest?.Modified?.Md5;
				if (verifyModifiedHash && allowXdeltaFallback && !string.IsNullOrWhiteSpace(expectedModifiedHash))
				{
					verifyHashSw.Start();
					string obj = await HashService.ComputeFileHashAsync(outputPath);
					verifyHashSw.Stop();
					if (!obj.Equals(expectedModifiedHash, StringComparison.OrdinalIgnoreCase))
					{
						LogService.Warning("[PatchService] Output hash differs from the patch's modified file hash");
						if (await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfs, manifest))
						{
							return new PatchApplyResult
							{
								Success = true
							};
						}
					}
				}
				if (appliedCount == 0)
				{
					if (await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfs, manifest))
					{
						return new PatchApplyResult
						{
							Success = true
						};
					}
					return new PatchApplyResult
					{
						Success = false,
						Error = "No resources were applied successfully"
					};
				}
				if (failedCount > 0)
				{
					LogService.Warning($"Patch applied with warnings: {appliedCount} succeeded, {failedCount} failed");
				}
				LogInterpolatedStringHandler handler8 = new LogInterpolatedStringHandler(235, 15, out shouldAppend);
				if (shouldAppend)
				{
					handler8.AppendLiteral("[Timing] Apply breakdown: patchLoad=");
					handler8.AppendFormatted(patchLoadSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, ");
					handler8.AppendLiteral("dataLoad=");
					handler8.AppendFormatted(dataLoadSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, nonCode=");
					handler8.AppendFormatted(nonCodeImportSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, ");
					handler8.AppendLiteral("code=");
					handler8.AppendFormatted(codeImportSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, finalize=");
					handler8.AppendFormatted(finalizeSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, ");
					handler8.AppendLiteral("save=");
					handler8.AppendFormatted(saveSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, verifyHash=");
					handler8.AppendFormatted(verifyHashSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, ");
					handler8.AppendLiteral("reorderPreCode=");
					handler8.AppendFormatted(preCodeReorderSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, reorderPostSprite=");
					handler8.AppendFormatted(postSpriteReorderSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, ");
					handler8.AppendLiteral("reorderFinal=");
					handler8.AppendFormatted(finalReorderSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, repairDupes=");
					handler8.AppendFormatted(repairDuplicatesSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, ");
					handler8.AppendLiteral("applyEvents=");
					handler8.AppendFormatted(applyEventsSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, repairDangling=");
					handler8.AppendFormatted(repairDanglingCodeSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, ");
					handler8.AppendLiteral("ensureScripts=");
					handler8.AppendFormatted(ensureScriptsSw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s, integrity=");
					handler8.AppendFormatted(integritySw.Elapsed.TotalSeconds, "F2");
					handler8.AppendLiteral("s");
				}
				LogService.Log(ref handler8);
				LogService.Log("[PatchService] Patch applied successfully");
				return new PatchApplyResult
				{
					Success = true
				};
			}
			finally
			{
				ResourceImportService.SetPatchFileSystem(null);
				pfs.ReleaseFileData();
				pfs.ReleaseCodeEntries();
				data.Dispose();
			}
		}
		catch (Exception ex2)
		{
			shouldAppend = pfsForFallback != null;
			if (shouldAppend)
			{
				shouldAppend = await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfsForFallback, manifestForFallback);
			}
			if (shouldAppend)
			{
				return new PatchApplyResult
				{
					Success = true
				};
			}
			try
			{
				if (File.Exists(outputPath))
				{
					File.Delete(outputPath);
				}
			}
			catch
			{
			}
			return new PatchApplyResult
			{
				Success = false,
				Error = "Failed to apply patch: " + ex2.Message
			};
		}
	}

	internal static async Task<PatchApplyResult> ApplyPatchInMemoryAsync(GameMakerData data, string patchPath, string workingDataPath)
	{
		if (!File.Exists(patchPath))
		{
			return new PatchApplyResult
			{
				Success = false,
				Error = "Patch file not found: " + patchPath
			};
		}
		try
		{
			Stopwatch totalSw = Stopwatch.StartNew();
			Stopwatch phaseSw = new Stopwatch();
			Stopwatch patchLoadSw = new Stopwatch();
			Stopwatch nonCodeImportSw = new Stopwatch();
			Stopwatch codeImportSw = new Stopwatch();
			Stopwatch finalizeSw = new Stopwatch();
			Stopwatch preCodeReorderSw = new Stopwatch();
			Stopwatch postSpriteReorderSw = new Stopwatch();
			Stopwatch finalReorderSw = new Stopwatch();
			Stopwatch repairDuplicatesSw = new Stopwatch();
			Stopwatch applyEventsSw = new Stopwatch();
			Stopwatch repairDanglingCodeSw = new Stopwatch();
			Stopwatch ensureScriptsSw = new Stopwatch();
			Stopwatch integritySw = new Stopwatch();
			patchLoadSw.Start();
			phaseSw.Restart();
			LogService.Log("[PatchService] Loading .g3mpatch file into memory for in-memory apply...");
			PatchFileSystem pfs = await Task.Run(() => PatchFileSystem.LoadFromZip(patchPath, loadExactPayloads: false));
			G3MPatchManifest manifest = pfs.Manifest;
			PatchApplyPlan applyPlan = manifest?.ApplyPlan ?? BuildPatchApplyPlan(manifest?.Resources ?? new Dictionary<string, ResourceTypeChanges>(StringComparer.OrdinalIgnoreCase));
			patchLoadSw.Stop();
			ResourceImportService.SetPatchFileSystem(pfs);
			try
			{
				List<string> resourceTypesToProcess = BuildResourceTypesToProcess(pfs, manifest);
				bool scriptOrderOnly = resourceTypesToProcess.Count == 0 && HasScriptOrderOnlyAssetPayload(pfs);
				if (scriptOrderOnly)
				{
					resourceTypesToProcess.Add("Scripts");
				}
				bool shouldAppend;
				LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(49, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler.AppendLiteral("[PatchService] Resources to process (in-memory): ");
					handler.AppendFormatted(string.Join(", ", resourceTypesToProcess));
				}
				LogService.Log(ref handler);
				if (resourceTypesToProcess.Count == 0)
				{
					return new PatchApplyResult
					{
						Success = true
					};
				}
				if (!scriptOrderOnly && CanUseMinimalStandardApply(applyPlan, resourceTypesToProcess))
				{
					LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(70, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler2.AppendLiteral("[PatchService] Applying only required resource functions (in-memory): ");
						handler2.AppendFormatted(string.Join(", ", resourceTypesToProcess));
					}
					LogService.Log(ref handler2);
					(int minimalAppliedCount, int minimalFailedCount) = ImportMinimalResources(data, pfs, resourceTypesToProcess, workingDataPath, workingDataPath);
					if (minimalFailedCount > 0 || minimalAppliedCount == 0)
					{
						return new PatchApplyResult
						{
							Success = false,
							Error = ((minimalFailedCount > 0) ? $"Patch apply failed: {minimalFailedCount} resource type(s) failed during import; in-memory output was not committed" : "No resources were applied successfully")
						};
					}
					DataIntegrityResult minimalIntegrity = DataIntegrityService.ValidateOnly(data);
					if (!minimalIntegrity.Success)
					{
						return new PatchApplyResult
						{
							Success = false,
							Error = "Integrity validation failed: " + string.Join("; ", minimalIntegrity.Errors.Take(20))
						};
					}
					totalSw.Stop();
					LogInterpolatedStringHandler handler3 = new LogInterpolatedStringHandler(41, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler3.AppendLiteral("[Timing] Minimal apply in-memory total: ");
						handler3.AppendFormatted(totalSw.Elapsed.TotalSeconds, "F1");
						handler3.AppendLiteral("s");
					}
					LogService.Log(ref handler3);
					return new PatchApplyResult
					{
						Success = true
					};
				}
				bool hasCodeEntries = CreateImportProgressState(resourceTypesToProcess).HasCodeEntries;
				PatchApplyPreparation preparation = PreparePatchApply(data, pfs, resourceTypesToProcess, applyPlan, hasCodeEntries, scriptOrderOnly);
				int failedCount = ImportPatchResources(data, pfs, manifest, resourceTypesToProcess, preparation, hasCodeEntries, workingDataPath, workingDataPath, nonCodeImportSw, codeImportSw, phaseSw, preCodeReorderSw, postSpriteReorderSw, logDetailedCodeImport: false).FailedCount;
				finalizeSw.Start();
				ApplyFinalAssetOrder(preparation.NeedsFinalAssetReorder, data, pfs, resourceTypesToProcess, preparation.Helpers, preparation.StagedEmbeddedTexturesDir, scriptOrderOnly, finalReorderSw);
				RepairImportedResourceStructures(data, preparation.UntouchedEmbeddedTextureSnapshot, resourceTypesToProcess, manifest, repairDuplicatesSw);
				ApplyCapturedObjectEvents(data, resourceTypesToProcess, manifest, preparation.Helpers.ObjectEventsBytes, applyEventsSw);
				if (preparation.VariablesFunctionsContent != null && preparation.OriginalGmlForCodeRemap.Count > 0)
				{
					preparation.GmlCompilationRan |= RecompileUntouchedCodeForAssetOrderShifts(data, preparation.AssetNamesAtCodeImportStart, preparation.OriginalGmlForCodeRemap, preparation.PatchCodeEntryNames, preparation.Helpers.HelpersDir, preparation.VariablesFunctionsContent);
				}
				if (!preparation.GmlCompilationRan)
				{
					PruneCodeEntriesOutsideTargetMetadata(data, preparation.CodeMetadataForFinalCleanup ?? ReadCodeMetadataContent(pfs, preparation.Helpers.HelpersDir));
				}
				PruneDeletedScriptOrphans(data, manifest);
				PruneMissingScriptCodeOrphans(data);
				DataIntegrityResult integrity = RepairAndValidateImportedData(data, resourceTypesToProcess, preparation.NeedsHeavyFinalize, repairDanglingCodeSw, ensureScriptsSw, integritySw);
				if (integrity != null && !integrity.Success)
				{
					finalizeSw.Stop();
					return new PatchApplyResult
					{
						Success = false,
						Error = "Integrity validation failed: " + string.Join("; ", integrity.Errors.Take(20))
					};
				}
				RepairLocalChildFunctionReferencesFinal(data);
				finalizeSw.Stop();
				if (failedCount > 0)
				{
					return new PatchApplyResult
					{
						Success = false,
						Error = $"Patch apply failed: {failedCount} resource type(s) failed during import; in-memory output was not committed"
					};
				}
				totalSw.Stop();
				LogInterpolatedStringHandler handler4 = new LogInterpolatedStringHandler(55, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler4.AppendLiteral("[Timing] === PATCH APPLY IN-MEMORY TOTAL: ");
					handler4.AppendFormatted(totalSw.Elapsed.TotalSeconds, "F1");
					handler4.AppendLiteral("s === RAM: ");
					handler4.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler4.AppendLiteral("MB");
				}
				LogService.Log(ref handler4);
				LogInterpolatedStringHandler handler5 = new LogInterpolatedStringHandler(211, 12, out shouldAppend);
				if (shouldAppend)
				{
					handler5.AppendLiteral("[Timing] Apply in-memory breakdown: patchLoad=");
					handler5.AppendFormatted(patchLoadSw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s, ");
					handler5.AppendLiteral("nonCode=");
					handler5.AppendFormatted(nonCodeImportSw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s, code=");
					handler5.AppendFormatted(codeImportSw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s, ");
					handler5.AppendLiteral("finalize=");
					handler5.AppendFormatted(finalizeSw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s, reorderPreCode=");
					handler5.AppendFormatted(preCodeReorderSw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s, ");
					handler5.AppendLiteral("reorderPostSprite=");
					handler5.AppendFormatted(postSpriteReorderSw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s, reorderFinal=");
					handler5.AppendFormatted(finalReorderSw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s, ");
					handler5.AppendLiteral("repairDupes=");
					handler5.AppendFormatted(repairDuplicatesSw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s, applyEvents=");
					handler5.AppendFormatted(applyEventsSw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s, ");
					handler5.AppendLiteral("repairDangling=");
					handler5.AppendFormatted(repairDanglingCodeSw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s, ensureScripts=");
					handler5.AppendFormatted(ensureScriptsSw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s, ");
					handler5.AppendLiteral("integrity=");
					handler5.AppendFormatted(integritySw.Elapsed.TotalSeconds, "F2");
					handler5.AppendLiteral("s");
				}
				LogService.Log(ref handler5);
				return new PatchApplyResult
				{
					Success = true
				};
			}
			finally
			{
				ResourceImportService.SetPatchFileSystem(null);
			}
		}
		catch (Exception ex2)
		{
			return new PatchApplyResult
			{
				Success = false,
				Error = "Failed to apply patch in memory: " + ex2.Message
			};
		}
	}

	private static void RepairImportedResourceStructures(GameMakerData data, Dictionary<int, byte[]> untouchedEmbeddedTextureSnapshot, List<string> resourceTypes, G3MPatchManifest? manifest, Stopwatch repairDuplicatesSw)
	{
		RepairDetachedFontTexturePageItems(data);
		RestoreUntouchedEmbeddedTextures(data, untouchedEmbeddedTextureSnapshot);
		TrimUnreferencedTrailingEmbeddedTextures(data);
		repairDuplicatesSw.Start();
		if (RequiresDuplicateGameObjectRepair(resourceTypes, manifest))
		{
			RepairDuplicateGameObjectPlaceholders(data);
		}
		repairDuplicatesSw.Stop();
	}

	private static void ApplyCapturedObjectEvents(GameMakerData data, List<string> resourceTypes, G3MPatchManifest? manifest, byte[]? capturedObjectEventsBytes, Stopwatch applyEventsSw)
	{
		if (capturedObjectEventsBytes != null && ShouldApplyAuthoritativeObjectEvents(resourceTypes, manifest, capturedObjectEventsBytes))
		{
			applyEventsSw.Start();
			HashSet<string> objectEventCodeFilter = (resourceTypes.Contains<string>("GameObjects", StringComparer.OrdinalIgnoreCase) ? null : GetChangedCodeEntryNames(manifest));
			ApplyAuthoritativeObjectEvents(data, Encoding.UTF8.GetString(capturedObjectEventsBytes), objectEventCodeFilter);
			applyEventsSw.Stop();
		}
	}

	private static DataIntegrityResult RepairAndValidateImportedData(GameMakerData data, List<string> resourceTypes, bool requiresHeavyFinalize, Stopwatch repairDanglingCodeSw, Stopwatch ensureScriptsSw, Stopwatch integritySw)
	{
		if (requiresHeavyFinalize)
		{
			repairDanglingCodeSw.Start();
			RepairDanglingCodeReferences(data);
			repairDanglingCodeSw.Stop();
			if (RequiresScriptEntryEnsure(resourceTypes))
			{
				ensureScriptsSw.Start();
				EnsureScriptEntriesForLiveScriptCode(data);
				ensureScriptsSw.Stop();
			}
		}
		integritySw.Start();
		DataIntegrityResult result = DataIntegrityService.RepairAndValidate(data);
		integritySw.Stop();
		return result;
	}

	private static void RepairDuplicateGameObjectPlaceholders(GameMakerData data)
	{
		Dictionary<string, List<GameMakerGameObject>> byName = new Dictionary<string, List<GameMakerGameObject>>(StringComparer.Ordinal);
		foreach (GameMakerGameObject obj in data.GameObjects)
		{
			string name = obj?.Name?.Content;
			if (!string.IsNullOrEmpty(name))
			{
				if (!byName.TryGetValue(name, out var list))
				{
					list = (byName[name] = new List<GameMakerGameObject>());
				}
				list.Add(obj);
			}
		}
		int repaired = 0;
		foreach (List<GameMakerGameObject> group in byName.Values)
		{
			if (group.Count < 2)
			{
				continue;
			}
			GameMakerGameObject source = group.Where((GameMakerGameObject o) => !IsGameObjectPlaceholder(o)).OrderByDescending(GameObjectCompletenessScore).FirstOrDefault();
			if (source == null)
			{
				continue;
			}
			foreach (GameMakerGameObject target in group)
			{
				if (source != target && IsGameObjectPlaceholder(target))
				{
					CopyGameObjectProperties(source, target);
					repaired++;
				}
			}
		}
		if (repaired > 0)
		{
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(60, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Repaired ");
				handler.AppendFormatted(repaired);
				handler.AppendLiteral(" duplicate GameObject placeholder(s)");
			}
			LogService.Log(ref handler);
		}
	}

	private static bool IsGameObjectPlaceholder(GameMakerGameObject obj)
	{
		bool hasEvents = obj.Events.Any((GameMakerPointerList<GameMakerGameObject.Event> e) => e.Count > 0);
		if (obj.Sprite == null && obj.ParentId == null && obj.TextureMaskId == null && !obj.Solid && obj.Depth == 0 && !obj.Persistent)
		{
			return !hasEvents;
		}
		return false;
	}

	private static int GameObjectCompletenessScore(GameMakerGameObject obj)
	{
		int score = 0;
		if (obj.Sprite != null)
		{
			score += 4;
		}
		if (obj.ParentId != null)
		{
			score += 4;
		}
		if (obj.TextureMaskId != null)
		{
			score += 2;
		}
		if (obj.Events.Any((GameMakerPointerList<GameMakerGameObject.Event> e) => e.Count > 0))
		{
			score += 4;
		}
		if (obj.Solid)
		{
			score++;
		}
		if (obj.Depth != 0)
		{
			score++;
		}
		if (obj.Persistent)
		{
			score++;
		}
		if (obj.UsesPhysics)
		{
			score++;
		}
		return score;
	}

	private static void CopyGameObjectProperties(GameMakerGameObject source, GameMakerGameObject target)
	{
		target.Sprite = source.Sprite;
		target.Visible = source.Visible;
		target.Managed = source.Managed;
		target.Solid = source.Solid;
		target.Depth = source.Depth;
		target.Persistent = source.Persistent;
		target.ParentId = source.ParentId;
		target.TextureMaskId = source.TextureMaskId;
		target.UsesPhysics = source.UsesPhysics;
		target.IsSensor = source.IsSensor;
		target.CollisionShape = source.CollisionShape;
		target.Density = source.Density;
		target.Restitution = source.Restitution;
		target.Group = source.Group;
		target.LinearDamping = source.LinearDamping;
		target.AngularDamping = source.AngularDamping;
		target.Friction = source.Friction;
		target.Awake = source.Awake;
		target.Kinematic = source.Kinematic;
		for (int eventType = 0; eventType < Math.Min(source.Events.Count, target.Events.Count); eventType++)
		{
			target.Events[eventType].Clear();
			foreach (GameMakerGameObject.Event sourceEvent in source.Events[eventType])
			{
				GameMakerGameObject.Event targetEvent = new GameMakerGameObject.Event
				{
					EventSubtype = sourceEvent.EventSubtype
				};
				foreach (GameMakerGameObject.EventAction sourceAction in sourceEvent.Actions)
				{
					targetEvent.Actions.Add(CloneEventAction(sourceAction));
				}
				target.Events[eventType].Add(targetEvent);
			}
		}
	}

	private static GameMakerGameObject.EventAction CloneEventAction(GameMakerGameObject.EventAction source)
	{
		return new GameMakerGameObject.EventAction
		{
			LibID = source.LibID,
			ID = source.ID,
			Kind = source.Kind,
			UseRelative = source.UseRelative,
			IsQuestion = source.IsQuestion,
			UseApplyTo = source.UseApplyTo,
			ExeType = source.ExeType,
			ActionName = source.ActionName,
			CodeId = source.CodeId,
			ArgumentCount = source.ArgumentCount,
			Who = source.Who,
			Relative = source.Relative,
			IsNot = source.IsNot,
			UnknownAlwaysZero = source.UnknownAlwaysZero
		};
	}

	private static GameMakerGameObject.EventAction CreateEventActionFromJson(GameMakerData data, JsonElement actionElm)
	{
		GameMakerGameObject.EventAction action = new GameMakerGameObject.EventAction();
		if (actionElm.TryGetProperty("libId", out var libIdElm))
		{
			action.LibID = (uint)libIdElm.GetInt64();
		}
		if (actionElm.TryGetProperty("id", out var idElm))
		{
			action.ID = (uint)idElm.GetInt64();
		}
		if (actionElm.TryGetProperty("kind", out var kindElm))
		{
			action.Kind = (uint)kindElm.GetInt64();
		}
		if (actionElm.TryGetProperty("useRelative", out var useRelativeElm))
		{
			action.UseRelative = useRelativeElm.GetBoolean();
		}
		if (actionElm.TryGetProperty("isQuestion", out var isQuestionElm))
		{
			action.IsQuestion = isQuestionElm.GetBoolean();
		}
		if (actionElm.TryGetProperty("useApplyTo", out var useApplyToElm))
		{
			action.UseApplyTo = useApplyToElm.GetBoolean();
		}
		if (actionElm.TryGetProperty("exeType", out var exeTypeElm))
		{
			action.ExeType = (uint)exeTypeElm.GetInt64();
		}
		if (actionElm.TryGetProperty("actionName", out var actionNameElm))
		{
			string actionName = actionNameElm.GetString();
			action.ActionName = ((!string.IsNullOrEmpty(actionName)) ? data.Strings.MakeString(actionName) : null);
		}
		if (actionElm.TryGetProperty("codeId", out var codeIdElm))
		{
			string codeName = codeIdElm.GetString();
			if (!string.IsNullOrEmpty(codeName))
			{
				action.CodeId = data.Code.ByName(codeName);
			}
		}
		if (actionElm.TryGetProperty("argumentCount", out var argumentCountElm))
		{
			action.ArgumentCount = (uint)argumentCountElm.GetInt64();
		}
		if (actionElm.TryGetProperty("who", out var whoElm))
		{
			action.Who = whoElm.GetInt32();
		}
		if (actionElm.TryGetProperty("relative", out var relativeElm))
		{
			action.Relative = relativeElm.GetBoolean();
		}
		if (actionElm.TryGetProperty("isNot", out var isNotElm))
		{
			action.IsNot = isNotElm.GetBoolean();
		}
		return action;
	}

	private static void RepairDanglingCodeReferences(GameMakerData data)
	{
		HashSet<GameMakerCode> liveCodeEntries = new HashSet<GameMakerCode>();
		foreach (GameMakerCode code in data.Code)
		{
			if (code != null)
			{
				liveCodeEntries.Add(code);
			}
		}
		int repairedScripts = 0;
		int nulledScripts = 0;
		foreach (GameMakerScript script in data.Scripts)
		{
			if (script?.Code == null || liveCodeEntries.Contains(script.Code))
			{
				continue;
			}
			GameMakerCode replacement = null;
			string currentCodeName = script.Code.Name?.Content;
			if (!string.IsNullOrEmpty(currentCodeName))
			{
				replacement = ScriptCodeResolver.Resolve(data, script.Name?.Content, currentCodeName);
			}
			if (replacement == null)
			{
				string scriptName = script.Name?.Content;
				if (scriptName != null && !string.IsNullOrEmpty(scriptName))
				{
					replacement = ScriptCodeResolver.Resolve(data, scriptName);
				}
			}
			if (replacement != null)
			{
				script.Code = replacement;
				repairedScripts++;
			}
			else
			{
				script.Code = null;
				nulledScripts++;
			}
		}
		int removedGlobalInit = 0;
		for (int i = data.GlobalInitScripts.Count - 1; i >= 0; i--)
		{
			if (data.GlobalInitScripts[i]?.Code != null && !liveCodeEntries.Contains(data.GlobalInitScripts[i].Code))
			{
				data.GlobalInitScripts.RemoveAt(i);
				removedGlobalInit++;
			}
		}
		if (repairedScripts > 0 || nulledScripts > 0 || removedGlobalInit > 0)
		{
			LogService.Warning($"[PatchService] Repaired dangling code references: {repairedScripts} relinked scripts, {nulledScripts} nulled scripts, {removedGlobalInit} removed global init entries");
		}
	}

	private static void EnsureScriptEntriesForLiveScriptCode(GameMakerData data)
	{
		if (data.Scripts == null || data.Code == null)
		{
			return;
		}
		int relinked = 0;
		int created = 0;
		HashSet<string> scriptNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (GameMakerScript script in data.Scripts)
		{
			if (script == null || script.Name?.Content == null)
			{
				continue;
			}
			scriptNames.Add(script.Name.Content);
			if (script.Code == null)
			{
				script.Code = ScriptCodeResolver.Resolve(data, script.Name.Content);
				if (script.Code != null)
				{
					relinked++;
				}
			}
		}
		foreach (GameMakerCode item in data.Code)
		{
			foreach (GameMakerCode code in EnumerateCodeTreeRecursive(item))
			{
				string codeName = code?.Name?.Content;
				if (!string.IsNullOrEmpty(codeName) && codeName.StartsWith("gml_Script_", StringComparison.Ordinal) && scriptNames.Add(codeName))
				{
					data.Scripts.Add(new GameMakerScript
					{
						Name = data.Strings.MakeString(codeName),
						Code = code,
						IsConstructor = codeName.StartsWith("gml_Script____struct___", StringComparison.Ordinal)
					});
					created++;
				}
			}
		}
		bool shouldAppend;
		if (relinked > 0)
		{
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(53, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Re-linked ");
				handler.AppendFormatted(relinked);
				handler.AppendLiteral(" Script entries to live code");
			}
			LogService.Log(ref handler);
		}
		if (created > 0)
		{
			LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(71, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler2.AppendLiteral("[PatchService] Created ");
				handler2.AppendFormatted(created);
				handler2.AppendLiteral(" missing Script entries for live gml_Script code");
			}
			LogService.Log(ref handler2);
		}
	}

	private static int ApplyDeletedResources(GameMakerData data, G3MPatchManifest? manifest, string resourceType)
	{
		if (manifest?.Resources == null || !manifest.Resources.TryGetValue(resourceType, out ResourceTypeChanges changes) || changes.Deleted == null || changes.Deleted.Count == 0)
		{
			return 0;
		}
		int handled = 0;
		foreach (string name in changes.Deleted)
		{
			if (RemoveResourceByName(data, resourceType, name))
			{
				handled++;
			}
		}
		if (handled > 0)
		{
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(48, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Handled ");
				handler.AppendFormatted(handled);
				handler.AppendLiteral(" ");
				handler.AppendFormatted(resourceType);
				handler.AppendLiteral(" deletions from manifest");
			}
			LogService.Log(ref handler);
		}
		return handled;
	}

	private static bool RemoveResourceByName(GameMakerData data, string resourceType, string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		return resourceType switch
		{
			"Sprites" => RemoveReferencedOrNamed(data, data.Sprites, "Sprites", name, IsSpriteReferenced),
			"Backgrounds" => RemoveReferencedOrNamed(data, data.Backgrounds, "Backgrounds", name, IsBackgroundReferenced),
			"Fonts" => RemoveReferencedOrNamed(data, data.Fonts, "Fonts", name, IsFontReferenced),
			"Sounds" => RemoveReferencedOrNamed(data, data.Sounds, "Sounds", name, IsSoundReferenced),
			"Paths" => RemoveNamed(data.Paths, name),
			"Tilesets" => RemoveNamed(data.Backgrounds, name),
			"Shaders" => RemoveNamed(data.Shaders, name),
			"Timelines" => RemoveNamed(data.Timelines, name),
			"GameObjects" => RemoveReferencedOrNamed(data, data.GameObjects, "GameObjects", name, IsGameObjectReferenced),
			"Rooms" => RemoveNamed(data.Rooms, name),
			"TextureGroupInfo" => RemoveNamed(data.TextureGroupInfo, name),
			"Extensions" => RemoveExtension(data, name),
			"AudioGroups" => RemoveNamed(data.AudioGroups, name),
			"Scripts" => RemoveScriptOnlyWhenCodeIsGone(data, name),
			"EmbeddedImages" => data.EmbeddedImages != null && RemoveNamed(data.EmbeddedImages, name),
			"FilterEffects" => data.FilterEffects != null && RemoveNamed(data.FilterEffects, name),
			"AnimationCurves" => data.AnimationCurves != null && RemoveNamed(data.AnimationCurves, name),
			"ParticleSystemEmitters" => data.ParticleSystemEmitters != null && RemoveNamed(data.ParticleSystemEmitters, name),
			"ParticleSystems" => data.ParticleSystems != null && RemoveNamed(data.ParticleSystems, name),
			"Sequences" => data.Sequences != null && RemoveNamed(data.Sequences, name),
			"CodeEntries" => RemoveReferencedOrNamed(data, data.Code, "CodeEntries", name, IsCodeReferenced),
			_ => false,
		};
	}

	private static bool RemoveReferencedOrNamed<T>(GameMakerData data, IList<T> list, string resourceType, string name, Func<GameMakerData, T, bool> isReferenced)
	{
		for (int i = list.Count - 1; i >= 0; i--)
		{
			T item = list[i];
			object itemName = item?.GetType().GetProperty("Name")?.GetValue(item);
			if (string.Equals(itemName?.GetType().GetProperty("Content")?.GetValue(itemName) as string, name, StringComparison.Ordinal))
			{
				if (item != null && isReferenced(data, item))
				{
					bool shouldAppend;
					LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(69, 2, out shouldAppend);
					if (shouldAppend)
					{
						handler.AppendLiteral("[PatchService] Preserved ");
						handler.AppendFormatted(resourceType);
						handler.AppendLiteral(" deletion for ");
						handler.AppendFormatted(name);
						handler.AppendLiteral(": resource is still referenced");
					}
					LogService.Log(ref handler);
					return true;
				}
				list.RemoveAt(i);
				return true;
			}
		}
		return false;
	}

	private static bool RemoveNamed<T>(IList<T> list, string name)
	{
		for (int i = list.Count - 1; i >= 0; i--)
		{
			T item = list[i];
			object itemName = item?.GetType().GetProperty("Name")?.GetValue(item);
			if (string.Equals(itemName?.GetType().GetProperty("Content")?.GetValue(itemName) as string, name, StringComparison.Ordinal))
			{
				list.RemoveAt(i);
				return true;
			}
		}
		return false;
	}

	private static bool RemoveExtension(GameMakerData data, string name)
	{
		for (int i = data.Extensions.Count - 1; i >= 0; i--)
		{
			if (string.Equals(data.Extensions[i]?.Name?.Content, name, StringComparison.Ordinal))
			{
				data.Extensions.RemoveAt(i);
				List<byte[]> productIdData = data.FORM.EXTN?.productIdData;
				if (productIdData != null && i < productIdData.Count)
				{
					productIdData.RemoveAt(i);
				}
				return true;
			}
		}
		return false;
	}

	private static bool RemoveScriptOnlyWhenCodeIsGone(GameMakerData data, string name)
	{
		for (int i = data.Scripts.Count - 1; i >= 0; i--)
		{
			GameMakerScript script = data.Scripts[i];
			if (string.Equals(script?.Name?.Content, name, StringComparison.Ordinal))
			{
				string codeName = script.Code?.Name?.Content;
				if (!string.IsNullOrEmpty(codeName) && ScriptCodeResolver.Resolve(data, name, codeName) != null)
				{
					bool shouldAppend;
					LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(77, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler.AppendLiteral("[PatchService] Preserved Script deletion for ");
						handler.AppendFormatted(name);
						handler.AppendLiteral(": linked CodeEntry is still live");
					}
					LogService.Log(ref handler);
					return true;
				}
				data.Scripts.RemoveAt(i);
				RemoveOrphanScriptFunction(data, codeName ?? name);
				return true;
			}
		}
		RemoveOrphanScriptFunction(data, name);
		return false;
	}

	private static bool RemoveOrphanScriptFunction(GameMakerData data, string? codeName)
	{
		if (string.IsNullOrWhiteSpace(codeName) || data.Functions == null)
		{
			return false;
		}
		if (ScriptCodeResolver.Resolve(data, codeName, codeName) != null)
		{
			return false;
		}
		if (IsFunctionReferencedByCode(data, codeName))
		{
			return false;
		}
		for (int i = data.Functions.Count - 1; i >= 0; i--)
		{
			if (string.Equals(data.Functions[i]?.Name?.Content, codeName, StringComparison.Ordinal))
			{
				data.Functions.RemoveAt(i);
				bool shouldAppend;
				LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(63, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler.AppendLiteral("[PatchService] Removed orphan Function for deleted script code ");
					handler.AppendFormatted(codeName);
				}
				LogService.Log(ref handler);
				return true;
			}
		}
		return false;
	}

	private static bool IsFunctionReferencedByCode(GameMakerData data, string functionName)
	{
		foreach (GameMakerCode item in data.Code)
		{
			foreach (GameMakerCode item2 in EnumerateCodeTreeRecursive(item))
			{
				foreach (GameMakerInstruction item3 in item2.Instructions ?? new List<GameMakerInstruction>())
				{
					if (string.Equals(item3.ValueFunction?.Name?.Content, functionName, StringComparison.Ordinal))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static void PruneMissingScriptCodeOrphans(GameMakerData data)
	{
		int removedScripts = 0;
		int removedFunctions = 0;
		for (int i = data.Scripts.Count - 1; i >= 0; i--)
		{
			GameMakerScript script = data.Scripts[i];
			string scriptName = script?.Name?.Content;
			if (!string.IsNullOrWhiteSpace(scriptName) && script?.Code != null)
			{
				GameMakerCode liveCode = ScriptCodeResolver.Resolve(data, scriptName, script.Code.Name?.Content);
				if (liveCode != null)
				{
					if (script.Code != liveCode)
					{
						script.Code = liveCode;
					}
				}
				else
				{
					data.Scripts.RemoveAt(i);
					removedScripts++;
					if (RemoveOrphanScriptFunction(data, script?.Code?.Name?.Content ?? scriptName))
					{
						removedFunctions++;
					}
				}
			}
		}
		if (removedScripts > 0 || removedFunctions > 0)
		{
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(71, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Pruned missing script-code orphans: ");
				handler.AppendFormatted(removedScripts);
				handler.AppendLiteral(" scripts, ");
				handler.AppendFormatted(removedFunctions);
				handler.AppendLiteral(" functions");
			}
			LogService.Log(ref handler);
		}
	}

	private static void PruneDeletedScriptOrphans(GameMakerData data, G3MPatchManifest? manifest)
	{
		if (manifest?.Resources == null || !manifest.Resources.TryGetValue("Scripts", out ResourceTypeChanges changes) || changes.Deleted == null || changes.Deleted.Count == 0)
		{
			return;
		}
		int removedScripts = 0;
		int removedFunctions = 0;
		foreach (string deletedScriptName in changes.Deleted)
		{
			if (string.IsNullOrWhiteSpace(deletedScriptName) || ScriptCodeResolver.Resolve(data, deletedScriptName, deletedScriptName) != null)
			{
				continue;
			}
			for (int i = data.Scripts.Count - 1; i >= 0; i--)
			{
				GameMakerScript script = data.Scripts[i];
				if (string.Equals(script?.Name?.Content, deletedScriptName, StringComparison.Ordinal) || string.Equals(script?.Code?.Name?.Content, deletedScriptName, StringComparison.Ordinal))
				{
					data.Scripts.RemoveAt(i);
					removedScripts++;
				}
			}
			if (RemoveOrphanScriptFunction(data, deletedScriptName))
			{
				removedFunctions++;
			}
		}
		if (removedScripts > 0 || removedFunctions > 0)
		{
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(66, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Pruned deleted script orphans: ");
				handler.AppendFormatted(removedScripts);
				handler.AppendLiteral(" scripts, ");
				handler.AppendFormatted(removedFunctions);
				handler.AppendLiteral(" functions");
			}
			LogService.Log(ref handler);
		}
	}

	private static bool IsCodeReferenced(GameMakerData data, GameMakerCode code)
	{
		foreach (GameMakerScript script in data.Scripts)
		{
			if (script?.Code == code)
			{
				return true;
			}
		}
		foreach (GameMakerGlobalInit item in data.GlobalInitScripts ?? new List<GameMakerGlobalInit>())
		{
			if (item?.Code == code)
			{
				return true;
			}
		}
		foreach (GameMakerGlobalInit item2 in data.GameEndScripts ?? new List<GameMakerGlobalInit>())
		{
			if (item2?.Code == code)
			{
				return true;
			}
		}
		foreach (GameMakerGameObject gameObject in data.GameObjects)
		{
			foreach (GameMakerPointerList<GameMakerGameObject.Event> item3 in gameObject?.Events ?? new GameMakerPointerList<GameMakerPointerList<GameMakerGameObject.Event>>())
			{
				foreach (GameMakerGameObject.Event item4 in item3 ?? new GameMakerPointerList<GameMakerGameObject.Event>())
				{
					foreach (GameMakerGameObject.EventAction item5 in item4.Actions ?? new GameMakerPointerList<GameMakerGameObject.EventAction>())
					{
						if (item5?.CodeId == code)
						{
							return true;
						}
					}
				}
			}
		}
		foreach (GameMakerRoom room in data.Rooms)
		{
			if (room?.CreationCodeId == code)
			{
				return true;
			}
			foreach (GameMakerRoom.GameObject inst in room?.GameObjects ?? new GameMakerPointerList<GameMakerRoom.GameObject>())
			{
				if (inst?.CreationCode == code || inst?.PreCreateCode == code)
				{
					return true;
				}
			}
			foreach (GameMakerRoom.Layer item6 in room?.Layers ?? new GameMakerPointerList<GameMakerRoom.Layer>())
			{
				foreach (GameMakerRoom.GameObject inst2 in item6?.InstancesData?.Instances ?? new ObservableCollection<GameMakerRoom.GameObject>())
				{
					if (inst2?.CreationCode == code || inst2?.PreCreateCode == code)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static bool IsGameObjectReferenced(GameMakerData data, GameMakerGameObject gameObject)
	{
		foreach (GameMakerGameObject obj in data.GameObjects)
		{
			if (obj?.ParentId == gameObject)
			{
				return true;
			}
			int eventType = 0;
			while (obj != null && eventType < obj.Events.Count)
			{
				if (eventType == 4)
				{
					foreach (GameMakerGameObject.Event evt in obj.Events[eventType])
					{
						if (evt.EventSubtype < data.GameObjects.Count && data.GameObjects[(int)evt.EventSubtype] == gameObject)
						{
							return true;
						}
					}
				}
				eventType++;
			}
		}
		foreach (GameMakerRoom room in data.Rooms)
		{
			foreach (GameMakerRoom.View item in room?.Views ?? new GameMakerPointerList<GameMakerRoom.View>())
			{
				if (item?.ObjectId == gameObject)
				{
					return true;
				}
			}
			foreach (GameMakerRoom.GameObject item2 in room?.GameObjects ?? new GameMakerPointerList<GameMakerRoom.GameObject>())
			{
				if (item2?.ObjectDefinition == gameObject)
				{
					return true;
				}
			}
			foreach (GameMakerRoom.Layer item3 in room?.Layers ?? new GameMakerPointerList<GameMakerRoom.Layer>())
			{
				foreach (GameMakerRoom.GameObject item4 in item3?.InstancesData?.Instances ?? new ObservableCollection<GameMakerRoom.GameObject>())
				{
					if (item4?.ObjectDefinition == gameObject)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static bool IsSpriteReferenced(GameMakerData data, GameMakerSprite sprite)
	{
		foreach (GameMakerGameObject obj in data.GameObjects)
		{
			if (obj?.Sprite == sprite || obj?.TextureMaskId == sprite)
			{
				return true;
			}
		}
		foreach (GameMakerBackground background in data.Backgrounds)
		{
			if (background?.GMS2ExportedSprite == sprite)
			{
				return true;
			}
		}
		foreach (GameMakerParticleSystemEmitter item in data.ParticleSystemEmitters ?? new List<GameMakerParticleSystemEmitter>())
		{
			if (item?.Sprite == sprite)
			{
				return true;
			}
		}
		foreach (GameMakerRoom room in data.Rooms)
		{
			foreach (GameMakerRoom.Tile tile in room?.Tiles ?? new GameMakerPointerList<GameMakerRoom.Tile>())
			{
				if (tile != null && tile.spriteMode && tile.SpriteDefinition == sprite)
				{
					return true;
				}
			}
			foreach (GameMakerRoom.Layer layer in room?.Layers ?? new GameMakerPointerList<GameMakerRoom.Layer>())
			{
				if (layer?.BackgroundData?.Sprite == sprite)
				{
					return true;
				}
				foreach (GameMakerRoom.SpriteInstance item2 in layer?.AssetsData?.Sprites ?? new GameMakerPointerList<GameMakerRoom.SpriteInstance>())
				{
					if (item2?.Sprite == sprite)
					{
						return true;
					}
				}
				foreach (GameMakerRoom.Tile tile2 in layer?.AssetsData?.LegacyTiles ?? new GameMakerPointerList<GameMakerRoom.Tile>())
				{
					if (tile2 != null && tile2.spriteMode && tile2.SpriteDefinition == sprite)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static bool IsBackgroundReferenced(GameMakerData data, GameMakerBackground background)
	{
		foreach (GameMakerRoom room in data.Rooms)
		{
			foreach (GameMakerRoom.Background item in room?.Backgrounds ?? new GameMakerPointerList<GameMakerRoom.Background>())
			{
				if (item?.BackgroundDefinition == background)
				{
					return true;
				}
			}
			foreach (GameMakerRoom.Tile tile in room?.Tiles ?? new GameMakerPointerList<GameMakerRoom.Tile>())
			{
				if (tile != null && !tile.spriteMode && tile.BackgroundDefinition == background)
				{
					return true;
				}
			}
			foreach (GameMakerRoom.Layer item2 in room?.Layers ?? new GameMakerPointerList<GameMakerRoom.Layer>())
			{
				if (item2?.TilesData?.Background == background)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool IsFontReferenced(GameMakerData data, GameMakerFont font)
	{
		foreach (GameMakerRoom room in data.Rooms)
		{
			foreach (GameMakerRoom.Layer item in room?.Layers ?? new GameMakerPointerList<GameMakerRoom.Layer>())
			{
				foreach (GameMakerRoom.TextItemInstance item2 in item?.AssetsData?.TextItems ?? new GameMakerPointerList<GameMakerRoom.TextItemInstance>())
				{
					if (item2?.Font == font)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static bool IsSoundReferenced(GameMakerData data, GameMakerSound sound)
	{
		foreach (GameMakerSequence item in data.Sequences ?? new List<GameMakerSequence>())
		{
			if (SequenceReferencesSound(item, sound))
			{
				return true;
			}
		}
		return false;
	}

	private static bool SequenceReferencesSound(GameMakerSequence? sequence, GameMakerSound sound)
	{
		if (sequence == null)
		{
			return false;
		}
		HashSet<object> seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
		return ObjectGraphReferences(sequence, sound, seen, 0);
	}

	private static bool ObjectGraphReferences(object? node, object target, HashSet<object> seen, int depth)
	{
		if (node == null || depth > 8)
		{
			return false;
		}
		if (node == target)
		{
			return true;
		}
		if (node is string || node.GetType().IsPrimitive || node.GetType().IsEnum)
		{
			return false;
		}
		if (!seen.Add(node))
		{
			return false;
		}
		if (node is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				if (ObjectGraphReferences(item, target, seen, depth + 1))
				{
					return true;
				}
			}
			return false;
		}
		PropertyInfo[] properties = node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
		foreach (PropertyInfo prop in properties)
		{
			if (prop.CanRead && prop.GetIndexParameters().Length == 0)
			{
				object value;
				try
				{
					value = prop.GetValue(node);
				}
				catch
				{
					continue;
				}
				if (ObjectGraphReferences(value, target, seen, depth + 1))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static async Task<bool> TryApplyXdeltaFallbackAsync(string dataPath, string outputPath, PatchFileSystem pfs, G3MPatchManifest? manifest)
	{
		if (pfs.ExactPatchBytes == null)
		{
			return false;
		}
		string currentHash = await HashService.ComputeFileHashAsync(dataPath);
		string expectedOriginalHash = manifest?.Original?.Md5;
		if (!string.IsNullOrEmpty(expectedOriginalHash) && !currentHash.Equals(expectedOriginalHash, StringComparison.OrdinalIgnoreCase))
		{
			LogService.Warning("[PatchService] Xdelta fallback skipped because the original MD5 does not match");
			return false;
		}
		string extension = Path.GetExtension(pfs.ExactPatchPath ?? string.Empty);
		if (string.IsNullOrWhiteSpace(extension))
		{
			extension = ".xdelta";
		}
		string tempExactPatch = Path.Combine(Path.GetTempPath(), $"g3mtool_exact_apply_{Guid.NewGuid():N}{extension}");
		try
		{
			await File.WriteAllBytesAsync(tempExactPatch, pfs.ExactPatchBytes);
			try
			{
				if (File.Exists(outputPath))
				{
					File.Delete(outputPath);
				}
			}
			catch
			{
			}
			XDeltaResult exactResult = await new XDeltaService().ApplyPatchAsync(dataPath, tempExactPatch, outputPath);
			if (!exactResult.Success)
			{
				LogService.Warning("[PatchService] Xdelta fallback failed: " + exactResult.Error);
				return false;
			}
			string expectedModifiedHash = manifest?.Modified?.Md5;
			if (!string.IsNullOrEmpty(expectedModifiedHash) && !(await HashService.ComputeFileHashAsync(outputPath)).Equals(expectedModifiedHash, StringComparison.OrdinalIgnoreCase))
			{
				LogService.Warning("[PatchService] Xdelta fallback output hash mismatch");
				try
				{
					File.Delete(outputPath);
				}
				catch
				{
				}
				return false;
			}
			LogService.Log("[PatchService] Patch applied via xdelta fallback");
			return true;
		}
		finally
		{
			try
			{
				File.Delete(tempExactPatch);
			}
			catch
			{
			}
		}
	}

	private static async Task<bool> TryApplyXdeltaFallbackFromPatchAsync(string dataPath, string outputPath, string patchPath, PatchFileSystem pfs, G3MPatchManifest? manifest)
	{
		if (pfs.ExactPatchBytes == null)
		{
			try
			{
				pfs = await Task.Run(() => PatchFileSystem.LoadFromZip(patchPath, loadExactPayloads: true, skipAsmBackedGml: true));
				if (manifest == null)
				{
					manifest = pfs.Manifest;
				}
			}
			catch (Exception ex)
			{
				LogService.Warning("[PatchService] Xdelta fallback could not be loaded: " + ex.Message);
				return false;
			}
		}
		return await TryApplyXdeltaFallbackAsync(dataPath, outputPath, pfs, manifest);
	}

	private static void ApplyDeferredGlobalScripts(GameMakerData data, string globalScriptsJson)
	{
		string tempDir = Path.Combine(Path.GetTempPath(), $"g3mtool_globals_{Guid.NewGuid():N}");
		try
		{
			Directory.CreateDirectory(tempDir);
			File.WriteAllText(Path.Combine(tempDir, "global_scripts.json"), globalScriptsJson);
			ResourceImportService.SetPatchFileSystem(null);
			ResourceImportService.Import("GlobalScripts", data, tempDir);
		}
		finally
		{
			try
			{
				Directory.Delete(tempDir, recursive: true);
			}
			catch
			{
			}
		}
	}

	private static IEnumerable<GameMakerCode> EnumerateCodeTreeRecursive(GameMakerCode? code)
	{
		if (code == null)
		{
			yield break;
		}
		yield return code;
		if (code.ChildEntries == null)
		{
			yield break;
		}
		foreach (GameMakerCode child in code.ChildEntries)
		{
			foreach (GameMakerCode item in EnumerateCodeTreeRecursive(child))
			{
				yield return item;
			}
		}
	}

	private static Dictionary<string, GameMakerCode> BuildArchiveCodeLookup(GameMakerData data)
	{
		Dictionary<string, GameMakerCode> lookup = new Dictionary<string, GameMakerCode>(StringComparer.Ordinal);
		Dictionary<string, List<GameMakerCode>> topLevelByName = new Dictionary<string, List<GameMakerCode>>(StringComparer.Ordinal);
		foreach (GameMakerCode code in data.Code)
		{
			string name = code?.Name?.Content;
			if (code != null && code.ParentEntry == null && !string.IsNullOrEmpty(name))
			{
				if (!topLevelByName.TryGetValue(name, out var list))
				{
					list = (topLevelByName[name] = new List<GameMakerCode>());
				}
				list.Add(code);
			}
		}
		Dictionary<GameMakerCode, Dictionary<string, List<GameMakerCode>>> childByParentAndName = new Dictionary<GameMakerCode, Dictionary<string, List<GameMakerCode>>>();
		foreach (CodeArchiveEntryDescriptor descriptor in CodeEntryArchiveIdentity.DescribeEntries(data))
		{
			GameMakerCode code2 = null;
			List<GameMakerCode> matches2;
			if (descriptor.ParentArchiveKey != null)
			{
				if (!lookup.TryGetValue(descriptor.ParentArchiveKey, out var parent))
				{
					continue;
				}
				if (!childByParentAndName.TryGetValue(parent, out var childrenByName))
				{
					childrenByName = new Dictionary<string, List<GameMakerCode>>(StringComparer.Ordinal);
					foreach (GameMakerCode child in parent.ChildEntries)
					{
						string childName = child?.Name?.Content;
						if (child != null && !string.IsNullOrEmpty(childName))
						{
							if (!childrenByName.TryGetValue(childName, out var children))
							{
								children = (childrenByName[childName] = new List<GameMakerCode>());
							}
							children.Add(child);
						}
					}
					childByParentAndName[parent] = childrenByName;
				}
				if (childrenByName.TryGetValue(descriptor.LogicalName, out var matches) && descriptor.Occurrence > 0 && descriptor.Occurrence <= matches.Count)
				{
					code2 = matches[descriptor.Occurrence - 1];
				}
			}
			else if (topLevelByName.TryGetValue(descriptor.LogicalName, out matches2) && descriptor.Occurrence > 0 && descriptor.Occurrence <= matches2.Count)
			{
				code2 = matches2[descriptor.Occurrence - 1];
			}
			if (code2 != null)
			{
				lookup[descriptor.ArchiveKey] = code2;
			}
		}
		return lookup;
	}

	private static void LogWatchedArchiveCodeState(string phase, Dictionary<string, GameMakerCode> archiveCodeLookup)
	{
		if (!LogService.Verbose)
		{
			return;
		}
		try
		{
			List<string> lines = new List<string>(s_codeTopologyWatchKeys.Length);
			string[] array = s_codeTopologyWatchKeys;
			foreach (string key in array)
			{
				if (!archiveCodeLookup.TryGetValue(key, out GameMakerCode code) || code == null)
				{
					lines.Add(key + " => <missing>");
					continue;
				}
				string parent = code.ParentEntry?.Name?.Content ?? "<top>";
				lines.Add($"{key} => {code.Name?.Content ?? "<null>"} | parent={parent} | children={code.ChildEntries?.Count ?? 0}");
			}
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(28, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[ImportCodeEntries][Watch:");
				handler.AppendFormatted(phase);
				handler.AppendLiteral("] ");
				handler.AppendFormatted(string.Join("; ", lines));
			}
			LogService.Log(ref handler);
		}
		catch
		{
		}
	}

	private static Dictionary<string, (string LogicalName, int Occurrence, string? ParentArchiveKey)> ParseTargetCodeEntryMetadata(JsonElement root, Dictionary<string, string>? fallbackLogicalNames)
	{
		Dictionary<string, (string, int, string)> map = new Dictionary<string, (string, int, string)>(StringComparer.Ordinal);
		if (!root.TryGetProperty("codeEntries", out var ceArray) || ceArray.ValueKind != JsonValueKind.Array)
		{
			return map;
		}
		foreach (JsonElement ce in ceArray.EnumerateArray())
		{
			if (ce.ValueKind == JsonValueKind.Object)
			{
				JsonElement keyElem;
				string key = (ce.TryGetProperty("key", out keyElem) ? keyElem.GetString() : null);
				JsonElement nameElem;
				string name = (ce.TryGetProperty("name", out nameElem) ? nameElem.GetString() : null);
				JsonElement occElem;
				int occurrence = ((!ce.TryGetProperty("occurrence", out occElem)) ? 1 : occElem.GetInt32());
				JsonElement parentElem;
				string parentArchiveKey = (ce.TryGetProperty("parent", out parentElem) ? parentElem.GetString() : null);
				if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(name))
				{
					map[key] = (name, occurrence, parentArchiveKey);
				}
			}
			else
			{
				if (ce.ValueKind != JsonValueKind.String)
				{
					continue;
				}
				string name2 = ce.GetString();
				if (!string.IsNullOrWhiteSpace(name2))
				{
					string key2 = fallbackLogicalNames?.FirstOrDefault((KeyValuePair<string, string> x) => x.Value == name2).Key ?? name2;
					int occurrence2 = map.Values.Count<(string, int, string)>(((string LogicalName, int Occurrence, string ParentArchiveKey) x) => x.LogicalName == name2) + 1;
					map[key2] = (name2, occurrence2, null);
				}
			}
		}
		return map;
	}

	private static bool HasCompleteCodeEntryMetadata(JsonElement root)
	{
		if (root.TryGetProperty("completeCodeEntries", out var completeElement))
		{
			return completeElement.ValueKind != JsonValueKind.False;
		}
		return true;
	}

	private static void ReorderCodeEntriesFromTargetMetadata(GameMakerData data, Dictionary<string, (string LogicalName, int Occurrence, string? ParentArchiveKey)> targetCodeEntriesByKey)
	{
		if (targetCodeEntriesByKey.Count == 0)
		{
			return;
		}
		Dictionary<string, GameMakerCode> archiveLookup = BuildArchiveCodeLookup(data);
		HashSet<GameMakerCode> used = new HashSet<GameMakerCode>(ReferenceEqualityComparer.Instance);
		List<GameMakerCode> reordered = new List<GameMakerCode>(data.Code.Count);
		foreach (string entryKey in targetCodeEntriesByKey.Keys)
		{
			if (archiveLookup.TryGetValue(entryKey, out var code) && code != null && used.Add(code))
			{
				reordered.Add(code);
			}
		}
		foreach (GameMakerCode code2 in data.Code)
		{
			if (code2 != null && used.Add(code2))
			{
				reordered.Add(code2);
			}
		}
		if (reordered.Count != data.Code.Count)
		{
			return;
		}
		data.Code.Clear();
		foreach (GameMakerCode code3 in reordered)
		{
			data.Code.Add(code3);
		}
	}

	private static void PruneCodeEntriesOutsideTargetMetadata(GameMakerData data, Dictionary<string, (string LogicalName, int Occurrence, string? ParentArchiveKey)> targetCodeEntriesByKey)
	{
		if (targetCodeEntriesByKey.Count == 0)
		{
			return;
		}
		Dictionary<string, GameMakerCode> dictionary = BuildArchiveCodeLookup(data);
		HashSet<GameMakerCode> seen = new HashSet<GameMakerCode>(ReferenceEqualityComparer.Instance);
		List<GameMakerCode> toRemove = new List<GameMakerCode>();
		foreach (var (entryKey, code) in dictionary)
		{
			if (!targetCodeEntriesByKey.ContainsKey(entryKey) && seen.Add(code))
			{
				toRemove.Add(code);
			}
		}
		if (toRemove.Count == 0)
		{
			return;
		}
		HashSet<GameMakerCode> removeSet = new HashSet<GameMakerCode>(toRemove, ReferenceEqualityComparer.Instance);
		foreach (GameMakerCode code2 in toRemove)
		{
			code2.ParentEntry?.ChildEntries.Remove(code2);
			code2.ParentEntry = null;
			for (int i = code2.ChildEntries.Count - 1; i >= 0; i--)
			{
				if (removeSet.Contains(code2.ChildEntries[i]))
				{
					code2.ChildEntries.RemoveAt(i);
				}
			}
			for (int i2 = data.Scripts.Count - 1; i2 >= 0; i2--)
			{
				GameMakerScript script = data.Scripts[i2];
				if (script?.Code == code2 || string.Equals(script?.Name?.Content, code2.Name?.Content, StringComparison.Ordinal))
				{
					data.Scripts.RemoveAt(i2);
				}
			}
			for (int i3 = data.CodeLocals.Count - 1; i3 >= 0; i3--)
			{
				if (data.CodeLocals[i3].Name == code2.Name || string.Equals(data.CodeLocals[i3].Name?.Content, code2.Name?.Content, StringComparison.Ordinal))
				{
					data.CodeLocals.RemoveAt(i3);
				}
			}
			data.Code.Remove(code2);
		}
		bool shouldAppend;
		LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(66, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[ImportCodeEntries] Pruned ");
			handler.AppendFormatted(toRemove.Count);
			handler.AppendLiteral(" code entrie(s) outside target metadata");
		}
		LogService.Log(ref handler);
	}

	private static void PruneCodeEntriesOutsideTargetMetadata(GameMakerData data, string? variablesFunctionsJson)
	{
		if (string.IsNullOrWhiteSpace(variablesFunctionsJson))
		{
			return;
		}
		try
		{
			using JsonDocument doc = JsonDocument.Parse(variablesFunctionsJson);
			if (HasCompleteCodeEntryMetadata(doc.RootElement))
			{
				Dictionary<string, (string, int, string)> targetCodeEntriesByKey = ParseTargetCodeEntryMetadata(doc.RootElement, null);
				PruneCodeEntriesOutsideTargetMetadata(data, targetCodeEntriesByKey);
				PruneExcessCodeAndScriptsByTargetNames(data, targetCodeEntriesByKey.Values.Select<(string, int, string), string>(((string LogicalName, int Occurrence, string ParentArchiveKey) x) => x.LogicalName));
			}
		}
		catch (Exception ex)
		{
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(46, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Code metadata cleanup warning: ");
				handler.AppendFormatted(ex.Message);
			}
			LogService.Log(ref handler);
		}
	}

	private static void PruneExcessCodeAndScriptsByTargetNames(GameMakerData data, IEnumerable<string> targetCodeNames)
	{
		Dictionary<string, int> remainingByName = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (string name in targetCodeNames)
		{
			if (!string.IsNullOrEmpty(name))
			{
				remainingByName[name] = remainingByName.GetValueOrDefault(name) + 1;
			}
		}
		if (remainingByName.Count == 0)
		{
			return;
		}
		HashSet<GameMakerCode> removeCodes = new HashSet<GameMakerCode>(ReferenceEqualityComparer.Instance);
		for (int i = data.Code.Count - 1; i >= 0; i--)
		{
			GameMakerCode code = data.Code[i];
			string name2 = code?.Name?.Content;
			if (code != null && !string.IsNullOrEmpty(name2))
			{
				int remaining = remainingByName.GetValueOrDefault(name2);
				if (remaining > 0)
				{
					remainingByName[name2] = remaining - 1;
				}
				else
				{
					removeCodes.Add(code);
					data.Code.RemoveAt(i);
				}
			}
		}
		int removedScripts = 0;
		if (removeCodes.Count <= 0)
		{
			return;
		}
		for (int i2 = data.Scripts.Count - 1; i2 >= 0; i2--)
		{
			GameMakerScript script = data.Scripts[i2];
			GameMakerCode code2 = script?.Code;
			string scriptName = script?.Name?.Content;
			if ((code2 != null && removeCodes.Contains(code2)) || (!string.IsNullOrEmpty(scriptName) && removeCodes.Any((GameMakerCode c) => string.Equals(c.Name?.Content, scriptName, StringComparison.Ordinal))))
			{
				data.Scripts.RemoveAt(i2);
				removedScripts++;
			}
		}
		foreach (GameMakerCode code3 in removeCodes)
		{
			code3.ParentEntry?.ChildEntries.Remove(code3);
			code3.ParentEntry = null;
		}
		bool shouldAppend;
		LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(65, 2, out shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[PatchService] Pruned excess code/script entries: code=");
			handler.AppendFormatted(removeCodes.Count);
			handler.AppendLiteral(", scripts=");
			handler.AppendFormatted(removedScripts);
		}
		LogService.Log(ref handler);
	}

	private static string? ReadCodeMetadataContent(PatchFileSystem pfs, string helpersDir)
	{
		string compactPath = Path.Combine(helpersDir, "code_patch_metadata.json");
		if (pfs.FileExists(compactPath))
		{
			return pfs.ReadAllText(compactPath);
		}
		string legacyPath = Path.Combine(helpersDir, "variables_functions.json");
		if (!pfs.FileExists(legacyPath))
		{
			return null;
		}
		return pfs.ReadAllText(legacyPath);
	}

	private static Dictionary<string, List<string>> CaptureAssetNamesForCodeRemap(GameMakerData data)
	{
		return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
		{
			["sprites"] = data.Sprites.Select((GameMakerSprite s) => s?.Name?.Content ?? "").ToList(),
			["objects"] = data.GameObjects.Select((GameMakerGameObject o) => o?.Name?.Content ?? "").ToList(),
			["sounds"] = data.Sounds.Select((GameMakerSound s) => s?.Name?.Content ?? "").ToList(),
			["backgrounds"] = data.Backgrounds.Select((GameMakerBackground b) => b?.Name?.Content ?? "").ToList(),
			["paths"] = data.Paths.Select((GameMakerPath p) => p?.Name?.Content ?? "").ToList(),
			["scripts"] = data.Scripts.Select((GameMakerScript s) => s?.Name?.Content ?? "").ToList(),
			["fonts"] = data.Fonts.Select((GameMakerFont f) => f?.Name?.Content ?? "").ToList(),
			["rooms"] = data.Rooms.Select((GameMakerRoom r) => r?.Name?.Content ?? "").ToList(),
			["timelines"] = data.Timelines.Select((GameMakerTimeline t) => t?.Name?.Content ?? "").ToList(),
			["shaders"] = data.Shaders.Select((GameMakerShader s) => s?.Name?.Content ?? "").ToList(),
			["audiogroups"] = data.AudioGroups.Select((GameMakerAudioGroup a) => a?.Name?.Content ?? "").ToList()
		};
	}

	private static Dictionary<int, byte[]> CaptureUntouchedEmbeddedTextures(GameMakerData data, PatchFileSystem pfs)
	{
		HashSet<int> touched = GetTouchedEmbeddedTextureIndices(pfs);
		Dictionary<int, byte[]> result = new Dictionary<int, byte[]>();
		bool gm2022_5 = data.IsVersionAtLeast(2022u, 5u);
		for (int i = 0; i < data.EmbeddedTextures.Count; i++)
		{
			if (touched.Contains(i))
			{
				continue;
			}
			GMImage image = data.EmbeddedTextures[i]?.TextureData?.Image;
			if (image != null)
			{
				try
				{
					result[i] = image.ToSpan(gm2022_5).ToArray();
				}
				catch
				{
				}
			}
		}
		return result;
	}

	private static HashSet<int> GetTouchedEmbeddedTextureIndices(PatchFileSystem pfs)
	{
		HashSet<int> result = new HashSet<int>();
		foreach (string path in pfs.GetAllFilePaths())
		{
			if (!path.StartsWith("EmbeddedTextures/", StringComparison.OrdinalIgnoreCase) && !path.StartsWith(pfs.HelpersPrefix + "/EmbeddedTextures/", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string[] parts = path.Split(new char[2] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < parts.Length; i++)
			{
				if (parts[i].StartsWith("texture_", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[i].Substring("texture_".Length), out var index))
				{
					result.Add(index);
					break;
				}
			}
		}
		return result;
	}

	private static void RestoreUntouchedEmbeddedTextures(GameMakerData data, Dictionary<int, byte[]> snapshot)
	{
		int restored = 0;
		bool gm2022_5 = data.IsVersionAtLeast(2022u, 5u);
		foreach (var (index, imageBytes) in snapshot)
		{
			if (index < 0 || index >= data.EmbeddedTextures.Count || data.EmbeddedTextures[index]?.TextureData == null)
			{
				continue;
			}
			try
			{
				using MemoryStream stream = new MemoryStream(imageBytes, writable: false);
				using FileBinaryReader reader = new FileBinaryReader(stream);
				data.EmbeddedTextures[index].TextureData.Image = GMImage.FromBinaryReader(reader, stream.Length, gm2022_5);
				restored++;
			}
			catch
			{
			}
		}
		if (restored > 0)
		{
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(51, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Restored ");
				handler.AppendFormatted(restored);
				handler.AppendLiteral(" untouched EmbeddedTextures");
			}
			LogService.Log(ref handler);
		}
	}

	private static void TrimUnreferencedTrailingEmbeddedTextures(GameMakerData data)
	{
		HashSet<GameMakerEmbeddedTexture> referenced = new HashSet<GameMakerEmbeddedTexture>(ReferenceEqualityComparer.Instance);
		foreach (GameMakerTexturePageItem tpi in data.TexturePageItems)
		{
			if (tpi?.TexturePage != null)
			{
				referenced.Add(tpi.TexturePage);
			}
		}
		int oldCount = data.EmbeddedTextures.Count;
		while (data.EmbeddedTextures.Count > 0)
		{
			IList<GameMakerEmbeddedTexture> embeddedTextures = data.EmbeddedTextures;
			GameMakerEmbeddedTexture texture = embeddedTextures[embeddedTextures.Count - 1];
			if (texture != null && referenced.Contains(texture))
			{
				break;
			}
			data.EmbeddedTextures.RemoveAt(data.EmbeddedTextures.Count - 1);
		}
		if (data.EmbeddedTextures.Count != oldCount)
		{
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(67, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Trimmed unreferenced trailing EmbeddedTextures: ");
				handler.AppendFormatted(oldCount);
				handler.AppendLiteral(" -> ");
				handler.AppendFormatted(data.EmbeddedTextures.Count);
			}
			LogService.Log(ref handler);
		}
	}

	private static byte[] RemoveUnsafeRelinksFromSpriteFrameMap(byte[] spriteFrameMapBytes, bool removeFontRelinks)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(spriteFrameMapBytes);
			using MemoryStream stream = new MemoryStream();
			using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions
			{
				Indented = true
			}))
			{
				writer.WriteStartObject();
				foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
				{
					if (removeFontRelinks && prop.NameEquals("fonts"))
					{
						continue;
					}
					if (prop.NameEquals("sprites"))
					{
						writer.WritePropertyName(prop.Name);
						writer.WriteStartObject();
						foreach (JsonProperty item in prop.Value.EnumerateObject())
						{
							item.WriteTo(writer);
						}
						writer.WriteEndObject();
					}
					else
					{
						prop.WriteTo(writer);
					}
				}
				writer.WriteEndObject();
			}
			return stream.ToArray();
		}
		catch
		{
			return spriteFrameMapBytes;
		}
	}

	private static void RepairDetachedFontTexturePageItems(GameMakerData data)
	{
		int repairedTpi = 0;
		int repairedTextures = 0;
		HashSet<GameMakerTexturePageItem> tpiSet = new HashSet<GameMakerTexturePageItem>(data.TexturePageItems);
		HashSet<GameMakerEmbeddedTexture> textureSet = new HashSet<GameMakerEmbeddedTexture>(data.EmbeddedTextures);
		foreach (GameMakerFont font in data.Fonts)
		{
			GameMakerTexturePageItem tpi = font?.Texture;
			if (tpi != null)
			{
				if (!tpiSet.Contains(tpi))
				{
					data.TexturePageItems.Add(tpi);
					tpiSet.Add(tpi);
					repairedTpi++;
				}
				if (tpi.TexturePage != null && !textureSet.Contains(tpi.TexturePage))
				{
					data.EmbeddedTextures.Add(tpi.TexturePage);
					textureSet.Add(tpi.TexturePage);
					repairedTextures++;
				}
			}
		}
		if (repairedTpi > 0 || repairedTextures > 0)
		{
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(76, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Re-attached ");
				handler.AppendFormatted(repairedTpi);
				handler.AppendLiteral(" font TexturePageItems and ");
				handler.AppendFormatted(repairedTextures);
				handler.AppendLiteral(" font EmbeddedTextures");
			}
			LogService.Log(ref handler);
		}
	}

	private static void RepairLocalChildFunctionReferencesFinal(GameMakerData data)
	{
		if (data.Code == null || data.Functions == null)
		{
			return;
		}
		int reordered = ReorderLocalChildFunctions(data);
		bool shouldAppend;
		if (reordered > 0)
		{
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(69, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Restored local child Function order for ");
				handler.AppendFormatted(reordered);
				handler.AppendLiteral(" table slot(s)");
			}
			LogService.Log(ref handler);
		}
		Dictionary<string, GameMakerCode> codeByName = new Dictionary<string, GameMakerCode>(StringComparer.Ordinal);
		foreach (GameMakerCode code in data.Code)
		{
			string name = code?.Name?.Content;
			if (!string.IsNullOrEmpty(name) && code != null)
			{
				codeByName[name] = code;
			}
		}
		Dictionary<string, GameMakerFunction> functionByName = new Dictionary<string, GameMakerFunction>(StringComparer.Ordinal);
		foreach (GameMakerFunction function in data.Functions)
		{
			string name2 = function?.Name?.Content;
			if (!string.IsNullOrEmpty(name2) && function != null && !functionByName.ContainsKey(name2))
			{
				functionByName[name2] = function;
			}
		}
		Dictionary<GameMakerCode, Dictionary<string, GameMakerFunction?>> localMaps = new Dictionary<GameMakerCode, Dictionary<string, GameMakerFunction>>();
		Dictionary<GameMakerCode, Dictionary<string, GameMakerFunction>> localOrdinalMaps = new Dictionary<GameMakerCode, Dictionary<string, GameMakerFunction>>();
		int repaired = 0;
		foreach (GameMakerCode code2 in data.Code)
		{
			if (code2 == null)
			{
				continue;
			}
			GameMakerCode owner = code2.ParentEntry ?? code2;
			if (owner.ChildEntries == null || owner.ChildEntries.Count == 0)
			{
				continue;
			}
			Dictionary<string, GameMakerFunction> localMap = GetLocalMap(owner);
			if (localMap.Count == 0)
			{
				continue;
			}
			foreach (GameMakerInstruction instruction in code2.Instructions ?? new List<GameMakerInstruction>())
			{
				GameMakerFunction currentFunction = instruction.ValueFunction;
				string currentName = currentFunction?.Name?.Content;
				if (!string.IsNullOrEmpty(currentName) && FunctionNameUtil.IsLocalChildFunctionName(currentName) && (!codeByName.TryGetValue(currentName, out var referencedCode) || referencedCode.ParentEntry != owner))
				{
					if ((!localMap.TryGetValue(FunctionNameUtil.NormalizeLocalChildFunctionName(currentName), out var replacement) || replacement == null) && FunctionNameUtil.TryGetLocalChildOrdinalFamily(currentName, out string family, out int ordinal))
					{
						GetLocalOrdinalMap(owner).TryGetValue(family + "|" + ordinal, out replacement);
					}
					if (replacement != null && replacement != currentFunction)
					{
						instruction.ValueFunction = replacement;
						repaired++;
					}
				}
			}
		}
		if (repaired > 0)
		{
			LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(83, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler2.AppendLiteral("[PatchService] Re-linked ");
				handler2.AppendFormatted(repaired);
				handler2.AppendLiteral(" local child Function references to their owning CodeEntry");
			}
			LogService.Log(ref handler2);
		}
		int pruned = PruneDetachedLocalStructFunctions(data, codeByName);
		if (pruned > 0)
		{
			LogInterpolatedStringHandler handler3 = new LogInterpolatedStringHandler(61, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler3.AppendLiteral("[PatchService] Removed ");
				handler3.AppendFormatted(pruned);
				handler3.AppendLiteral(" detached local child Function entries");
			}
			LogService.Log(ref handler3);
		}
		Dictionary<string, GameMakerFunction?> GetLocalMap(GameMakerCode gameMakerCode)
		{
			if (localMaps.TryGetValue(gameMakerCode, out Dictionary<string, GameMakerFunction> cached))
			{
				return cached;
			}
			return localMaps[gameMakerCode] = DataIntegrityService.BuildLocalChildFunctionMap(gameMakerCode, functionByName);
		}
		Dictionary<string, GameMakerFunction> GetLocalOrdinalMap(GameMakerCode gameMakerCode)
		{
			if (localOrdinalMaps.TryGetValue(gameMakerCode, out Dictionary<string, GameMakerFunction> cached))
			{
				return cached;
			}
			return localOrdinalMaps[gameMakerCode] = DataIntegrityService.BuildLocalChildOrdinalMap(data, gameMakerCode, functionByName);
		}
	}

	private static int ReorderLocalChildFunctions(GameMakerData data)
	{
		Dictionary<string, GameMakerFunction> functionByName = new Dictionary<string, GameMakerFunction>(StringComparer.Ordinal);
		foreach (GameMakerFunction function in data.Functions)
		{
			string name = function?.Name?.Content;
			if (!string.IsNullOrEmpty(name) && function != null)
			{
				functionByName.TryAdd(name, function);
			}
		}
		int reordered = 0;
		foreach (GameMakerCode parent in data.Code)
		{
			if (parent?.ParentEntry != null || parent?.ChildEntries == null)
			{
				continue;
			}
			List<GameMakerFunction> expected = new List<GameMakerFunction>();
			foreach (GameMakerCode childEntry in parent.ChildEntries)
			{
				string name2 = childEntry?.Name?.Content;
				if (!string.IsNullOrEmpty(name2) && FunctionNameUtil.IsLocalChildFunctionName(name2) && functionByName.TryGetValue(name2, out var function2))
				{
					expected.Add(function2);
				}
			}
			if (expected.Count < 2)
			{
				continue;
			}
			HashSet<GameMakerFunction> expectedSet = new HashSet<GameMakerFunction>(expected);
			List<int> slots = (from index2 in Enumerable.Range(0, data.Functions.Count)
				where expectedSet.Contains(data.Functions[index2])
				select index2).ToList();
			if (slots.Count != expected.Count)
			{
				continue;
			}
			for (int index = 0; index < slots.Count; index++)
			{
				if (data.Functions[slots[index]] != expected[index])
				{
					data.Functions[slots[index]] = expected[index];
					reordered++;
				}
			}
		}
		return reordered;
	}

	private static int PruneDetachedLocalStructFunctions(GameMakerData data, IReadOnlyDictionary<string, GameMakerCode> codeByName)
	{
		if (data.Functions == null)
		{
			return 0;
		}
		HashSet<GameMakerFunction> referenced = new HashSet<GameMakerFunction>();
		foreach (GameMakerCode code in data.Code ?? new List<GameMakerCode>())
		{
			if (code == null)
			{
				continue;
			}
			foreach (GameMakerCode entry in new GameMakerCode[1] { code }.Concat(code.ChildEntries ?? new List<GameMakerCode>()))
			{
				if (entry?.Instructions == null)
				{
					continue;
				}
				foreach (GameMakerInstruction instruction in entry.Instructions)
				{
					if (instruction.ValueFunction != null)
					{
						referenced.Add(instruction.ValueFunction);
					}
				}
			}
		}
		HashSet<string> liveLocalSignatures = new HashSet<string>(StringComparer.Ordinal);
		foreach (GameMakerCode code2 in data.Code ?? new List<GameMakerCode>())
		{
			if (code2?.ParentEntry != null && !string.IsNullOrEmpty(code2.Name?.Content) && FunctionNameUtil.IsLocalChildFunctionName(code2.Name.Content))
			{
				liveLocalSignatures.Add(FunctionNameUtil.NormalizeLocalChildFunctionName(code2.Name.Content));
			}
		}
		int removed = 0;
		for (int i = data.Functions.Count - 1; i >= 0; i--)
		{
			GameMakerFunction function = data.Functions[i];
			string name = function?.Name?.Content;
			if (function != null && !string.IsNullOrEmpty(name) && FunctionNameUtil.IsLocalChildFunctionName(name) && !codeByName.ContainsKey(name) && !referenced.Contains(function) && liveLocalSignatures.Contains(FunctionNameUtil.NormalizeLocalChildFunctionName(name)))
			{
				data.Functions.RemoveAt(i);
				removed++;
			}
		}
		return removed;
	}

	private static Dictionary<string, string> CaptureOriginalCodeGmlForAssetRemap(GameMakerData data, IReadOnlyCollection<string> resourceTypesToProcess)
	{
		if (!RequiresCodeRemapPreparation(resourceTypesToProcess))
		{
			return new Dictionary<string, string>();
		}
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
		GlobalDecompileContext ctx = new GlobalDecompileContext(data);
		foreach (GameMakerCode code in data.Code)
		{
			string codeName = code?.Name?.Content;
			if (code != null && !string.IsNullOrWhiteSpace(codeName))
			{
				try
				{
					result[codeName] = new DecompileContext(ctx, code, data.ToolInfo.DecompilerSettings).DecompileToString();
				}
				catch
				{
				}
			}
		}
		return result;
	}

	private static HashSet<string> CapturePatchCodeEntryNames(PatchFileSystem pfs)
	{
		HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
		foreach (string key in pfs.GmlEntries.Keys.Concat(pfs.AsmEntries.Keys))
		{
			names.Add(key);
			if (pfs.CodeEntryLogicalNames.TryGetValue(key, out string logicalName))
			{
				names.Add(logicalName);
			}
		}
		return names;
	}

	private static bool RecompileUntouchedCodeForAssetOrderShifts(GameMakerData data, Dictionary<string, List<string>> originalAssetNames, Dictionary<string, string> originalGmlByCodeName, HashSet<string> patchCodeEntryNames, string helpersDir, string variablesFunctionsContent)
	{
		HashSet<string> shiftedNames = GetShiftedAssetNames(originalAssetNames, CaptureAssetNamesForCodeRemap(data));
		if (shiftedNames.Count == 0)
		{
			return false;
		}
		Dictionary<string, string> gmlToRecompile = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (codeName, gml) in originalGmlByCodeName)
		{
			if (!patchCodeEntryNames.Contains(codeName) && ContainsAnyShiftedAssetName(gml, shiftedNames))
			{
				gmlToRecompile[codeName] = gml;
			}
		}
		if (gmlToRecompile.Count == 0)
		{
			return false;
		}
		bool shouldAppend;
		LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(101, 2, out shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[PatchService] Recompiling ");
			handler.AppendFormatted(gmlToRecompile.Count);
			handler.AppendLiteral(" untouched code entries for shifted asset indices (");
			handler.AppendFormatted(shiftedNames.Count);
			handler.AppendLiteral(" shifted asset name(s))");
		}
		LogService.Log(ref handler);
		ImportCodeEntriesDirect(data, gmlToRecompile, new Dictionary<string, string>(), null, null, helpersDir, out var gmlCompilationRan, variablesFunctionsContent);
		return gmlCompilationRan;
	}

	private static HashSet<string> GetShiftedAssetNames(Dictionary<string, List<string>> before, Dictionary<string, List<string>> after)
	{
		HashSet<string> shifted = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (section, beforeNames) in before)
		{
			if (!after.TryGetValue(section, out List<string> afterNames))
			{
				continue;
			}
			Dictionary<string, Queue<int>> afterIndexByName = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
			for (int i = 0; i < afterNames.Count; i++)
			{
				string name = afterNames[i];
				if (IsSafeAssetNameForCodeRemap(name))
				{
					if (!afterIndexByName.TryGetValue(name, out var queue))
					{
						queue = (afterIndexByName[name] = new Queue<int>());
					}
					queue.Enqueue(i);
				}
			}
			for (int j = 0; j < beforeNames.Count; j++)
			{
				string name2 = beforeNames[j];
				if (IsSafeAssetNameForCodeRemap(name2) && afterIndexByName.TryGetValue(name2, out var queue3) && queue3.Count != 0 && queue3.Dequeue() != j)
				{
					shifted.Add(name2);
				}
			}
		}
		return shifted;
	}

	private static bool ContainsAnyShiftedAssetName(string gml, HashSet<string> shiftedNames)
	{
		foreach (string name in shiftedNames)
		{
			if (ContainsIdentifierReference(gml, name))
			{
				return true;
			}
		}
		return false;
	}

	private static bool ContainsIdentifierReference(string text, string identifier)
	{
		int searchIndex = 0;
		while (searchIndex < text.Length)
		{
			int matchIndex = text.IndexOf(identifier, searchIndex, StringComparison.Ordinal);
			if (matchIndex < 0)
			{
				return false;
			}
			int beforeIndex = matchIndex - 1;
			int afterIndex = matchIndex + identifier.Length;
			bool num = beforeIndex < 0 || !IsIdentifierChar(text[beforeIndex]);
			bool hasIdentifierBoundaryAfter = afterIndex >= text.Length || !IsIdentifierChar(text[afterIndex]);
			if (num && hasIdentifierBoundaryAfter)
			{
				return true;
			}
			searchIndex = matchIndex + identifier.Length;
		}
		return false;
	}

	private static bool IsIdentifierChar(char c)
	{
		if (!char.IsLetterOrDigit(c))
		{
			return c == '_';
		}
		return true;
	}

	private static bool IsSafeAssetNameForCodeRemap(string? name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			if (!name.StartsWith("obj_", StringComparison.Ordinal) && !name.StartsWith("spr_", StringComparison.Ordinal) && !name.StartsWith("snd_", StringComparison.Ordinal) && !name.StartsWith("bg_", StringComparison.Ordinal) && !name.StartsWith("bkg_", StringComparison.Ordinal) && !name.StartsWith("fnt_", StringComparison.Ordinal) && !name.StartsWith("font_", StringComparison.Ordinal) && !name.StartsWith("path_", StringComparison.Ordinal) && !name.StartsWith("pth_", StringComparison.Ordinal) && !name.StartsWith("tl_", StringComparison.Ordinal) && !name.StartsWith("timeline_", StringComparison.Ordinal) && !name.StartsWith("rm_", StringComparison.Ordinal))
			{
				return name.StartsWith("shd_", StringComparison.Ordinal);
			}
			return true;
		}
		return false;
	}

	private static void ImportCodeEntriesDirect(GameMakerData data, Dictionary<string, string> gmlEntries, Dictionary<string, string> asmEntries, Dictionary<string, string>? asmEntryPaths, Dictionary<string, string>? codeEntryLogicalNames, string helpersDir, out bool gmlCompilationRan, string? vfContentOverride = null, string? objectEventsContentOverride = null)
	{
		using (s_codeImportLock.EnterScope())
		{
			ImportCodeEntriesDirectCore(data, gmlEntries, asmEntries, asmEntryPaths, codeEntryLogicalNames, helpersDir, out gmlCompilationRan, vfContentOverride, objectEventsContentOverride);
		}
	}

	private static void ImportCodeEntriesDirectCore(GameMakerData data, Dictionary<string, string> gmlEntries, Dictionary<string, string> asmEntries, Dictionary<string, string>? asmEntryPaths, Dictionary<string, string>? codeEntryLogicalNames, string helpersDir, out bool gmlCompilationRan, string? vfContentOverride = null, string? objectEventsContentOverride = null)
	{
		gmlCompilationRan = false;
		Stopwatch sw = Stopwatch.StartNew();
		Stopwatch phaseSw = new Stopwatch();
		bool shouldAppend;
		LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(66, 2, out shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[ImportCodeEntries] Starting hybrid import: ");
			handler.AppendFormatted(gmlEntries.Count);
			handler.AppendLiteral(" GML + ");
			handler.AppendFormatted(asmEntries.Count);
			handler.AppendLiteral(" ASM entries...");
		}
		LogService.Log(ref handler);
		LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(55, 3, out shouldAppend);
		if (shouldAppend)
		{
			handler2.AppendLiteral("[ImportCodeEntries] Current state: ");
			handler2.AppendFormatted(data.Code.Count);
			handler2.AppendLiteral(" code, ");
			handler2.AppendFormatted(data.Variables.Count);
			handler2.AppendLiteral(" vars, ");
			handler2.AppendFormatted(data.Functions.Count);
			handler2.AppendLiteral(" funcs");
		}
		LogService.Log(ref handler2);
		string vfContent;
		if (vfContentOverride != null)
		{
			vfContent = vfContentOverride;
		}
		else
		{
			string varFuncPath = Path.Combine(helpersDir, "variables_functions.json");
			if (!File.Exists(varFuncPath))
			{
				throw new FileNotFoundException("variables_functions.json was not found", varFuncPath);
			}
			vfContent = File.ReadAllText(varFuncPath, Encoding.UTF8);
		}
		using JsonDocument vfDoc = JsonDocument.Parse(vfContent);
		JsonElement root = vfDoc.RootElement;
		Dictionary<string, (string, int, string)> targetCodeEntriesByKey = ParseTargetCodeEntryMetadata(root, codeEntryLogicalNames);
		bool hasCompleteCodeEntryMetadata = HasCompleteCodeEntryMetadata(root);
		JsonElement functionsElement;
		JsonElement variablesElement;
		bool hasFunctionVariableTables = root.TryGetProperty("functions", out functionsElement) && functionsElement.ValueKind == JsonValueKind.Array && root.TryGetProperty("variables", out variablesElement) && variablesElement.ValueKind == JsonValueKind.Array;
		Dictionary<string, string> targetTopLevelNamesByKey = new Dictionary<string, string>(StringComparer.Ordinal);
		HashSet<string> targetEntryNames = new HashSet<string>(StringComparer.Ordinal);
		Dictionary<string, int> targetEntryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
		Dictionary<string, List<(string, string)>> targetChildEntriesByParent = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);
		bool isAssetShiftRecompileOnly = asmEntries.Count == 0 && asmEntryPaths == null && codeEntryLogicalNames == null && objectEventsContentOverride == null;
		string key;
		(string, int, string) value;
		foreach (KeyValuePair<string, (string, int, string)> item in targetCodeEntriesByKey)
		{
			item.Deconstruct(out key, out value);
			string entryKey = key;
			(string, int, string) meta = value;
			if (meta.Item3 != null)
			{
				if (!targetChildEntriesByParent.TryGetValue(meta.Item3, out var childList))
				{
					childList = new List<(string, string)>();
					targetChildEntriesByParent[meta.Item3] = childList;
				}
				childList.Add((entryKey, meta.Item1));
			}
			else
			{
				(targetTopLevelNamesByKey[entryKey], _, _) = meta;
				targetEntryNames.Add(meta.Item1);
				targetEntryCounts[meta.Item1] = targetEntryCounts.GetValueOrDefault(meta.Item1) + 1;
			}
		}
		phaseSw.Restart();
		bool allGmlCoveredByAsm = !isAssetShiftRecompileOnly && asmEntries.Count > 0 && gmlEntries.Keys.All(asmEntries.ContainsKey);
		HashSet<string> preCompileEntries = new HashSet<string>(data.Code.Count);
		if (!allGmlCoveredByAsm)
		{
			foreach (GameMakerCode c in data.Code)
			{
				if (c != null && c.Name?.Content != null)
				{
					preCompileEntries.Add(c.Name.Content);
				}
			}
		}
		GlobalDecompileContext ctx = null;
		CodeImportGroup importGroup = null;
		Dictionary<string, GameMakerCode> codeLookup = new Dictionary<string, GameMakerCode>();
		Dictionary<string, GameMakerCode> archiveCodeLookup = new Dictionary<string, GameMakerCode>();
		Dictionary<string, (GameMakerGameObject, int)> goLookup = new Dictionary<string, (GameMakerGameObject, int)>();
		Dictionary<string, string> collisionTargetsByCode = new Dictionary<string, string>();
		if (!allGmlCoveredByAsm)
		{
			if (hasFunctionVariableTables)
			{
				ApplyTargetFunctionVariableTables(data, root, "precompile");
			}
			ctx = new GlobalDecompileContext(data);
			ctx.PrepareForCompilation();
			importGroup = new CodeImportGroup(data, ctx)
			{
				AutoCreateGlobalInitScripts = false,
				AutoCreateAssets = true
			};
			codeLookup = new Dictionary<string, GameMakerCode>(data.Code.Count);
			foreach (GameMakerCode item2 in data.Code)
			{
				foreach (GameMakerCode nested in EnumerateCodeTreeRecursive(item2))
				{
					if (nested != null && nested.Name?.Content != null)
					{
						codeLookup.TryAdd(nested.Name.Content, nested);
					}
				}
			}
			archiveCodeLookup = BuildArchiveCodeLookup(data);
			goLookup = new Dictionary<string, (GameMakerGameObject, int)>(data.GameObjects.Count);
			for (int i = 0; i < data.GameObjects.Count; i++)
			{
				GameMakerGameObject go = data.GameObjects[i];
				if (go != null && go.Name?.Content != null)
				{
					goLookup.TryAdd(go.Name.Content, (go, i));
				}
			}
			collisionTargetsByCode = LoadCollisionTargetsByCode(helpersDir, objectEventsContentOverride);
		}
		int regularCount = 0;
		int collisionCount = 0;
		int asmBackedCompileSkippedCount = (allGmlCoveredByAsm ? gmlEntries.Count : 0);
		List<(string, string, bool)> gmlOnlyEntries = new List<(string, string, bool)>();
		string value2;
		if (!allGmlCoveredByAsm)
		{
			foreach (KeyValuePair<string, string> gmlEntry in gmlEntries)
			{
				gmlEntry.Deconstruct(out key, out value2);
				string entryKey2 = key;
				string gmlCode = value2;
				string codeName = targetTopLevelNamesByKey.GetValueOrDefault(entryKey2, codeEntryLogicalNames?.GetValueOrDefault(entryKey2) ?? entryKey2);
				if (!isAssetShiftRecompileOnly && asmEntries.ContainsKey(entryKey2))
				{
					asmBackedCompileSkippedCount++;
					continue;
				}
				if (codeName.Contains("_Collision_"))
				{
					try
					{
						ImportCollisionEvent(data, codeName, gmlCode, importGroup, codeLookup, goLookup, collisionTargetsByCode);
						if (!asmEntries.ContainsKey(entryKey2))
						{
							gmlOnlyEntries.Add((codeName, gmlCode, true));
						}
						collisionCount++;
					}
					catch (Exception ex)
					{
						LogInterpolatedStringHandler handler3 = new LogInterpolatedStringHandler(38, 2, out shouldAppend);
						if (shouldAppend)
						{
							handler3.AppendLiteral("[ImportCodeEntries] Collision error ");
							handler3.AppendFormatted(codeName);
							handler3.AppendLiteral(": ");
							handler3.AppendFormatted(ex.Message);
						}
						LogService.Log(ref handler3);
					}
					continue;
				}
				try
				{
					EnsureObjectEventForCode(data, codeName, codeLookup, goLookup);
					if (archiveCodeLookup.TryGetValue(entryKey2, out var existing) && existing.ParentEntry == null)
					{
						importGroup.QueueReplace(existing, gmlCode);
					}
					else if (codeLookup.TryGetValue(codeName, out existing) && existing.ParentEntry == null)
					{
						importGroup.QueueReplace(existing, gmlCode);
					}
					else
					{
						importGroup.QueueReplace(codeName, gmlCode);
					}
					if (!asmEntries.ContainsKey(entryKey2))
					{
						gmlOnlyEntries.Add((codeName, gmlCode, false));
					}
					regularCount++;
				}
				catch (Exception ex2)
				{
					LogInterpolatedStringHandler handler4 = new LogInterpolatedStringHandler(31, 2, out shouldAppend);
					if (shouldAppend)
					{
						handler4.AppendLiteral("[ImportCodeEntries] Skipping ");
						handler4.AppendFormatted(codeName);
						handler4.AppendLiteral(": ");
						handler4.AppendFormatted(ex2.Message);
					}
					LogService.Log(ref handler4);
				}
			}
		}
		if (asmBackedCompileSkippedCount > 0)
		{
			LogInterpolatedStringHandler handler5 = new LogInterpolatedStringHandler(65, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler5.AppendLiteral("[ImportCodeEntries] Skipped GML compile for ");
				handler5.AppendFormatted(asmBackedCompileSkippedCount);
				handler5.AppendLiteral(" ASM-backed entrie(s)");
			}
			LogService.Log(ref handler5);
		}
		LogInterpolatedStringHandler handler6 = new LogInterpolatedStringHandler(61, 3, out shouldAppend);
		if (shouldAppend)
		{
			handler6.AppendLiteral("[ImportCodeEntries] Queued ");
			handler6.AppendFormatted(regularCount);
			handler6.AppendLiteral(" regular + ");
			handler6.AppendFormatted(collisionCount);
			handler6.AppendLiteral(" collision entries in ");
			handler6.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
			handler6.AppendLiteral("s");
		}
		LogService.Log(ref handler6);
		phaseSw.Restart();
		if (hasCompleteCodeEntryMetadata && targetEntryNames.Count > 0)
		{
			List<GameMakerCode> entriesToDelete = new List<GameMakerCode>();
			Dictionary<string, int> seenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (GameMakerCode c2 in data.Code)
			{
				if (c2 == null || c2.Name?.Content == null || c2.ParentEntry != null)
				{
					continue;
				}
				if (!targetEntryNames.Contains(c2.Name.Content))
				{
					entriesToDelete.Add(c2);
					continue;
				}
				int seen = seenCounts.GetValueOrDefault(c2.Name.Content) + 1;
				seenCounts[c2.Name.Content] = seen;
				if (seen > targetEntryCounts.GetValueOrDefault(c2.Name.Content))
				{
					entriesToDelete.Add(c2);
				}
			}
			if (entriesToDelete.Count > 0)
			{
				LogInterpolatedStringHandler handler7 = new LogInterpolatedStringHandler(51, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler7.AppendLiteral("[ImportCodeEntries] Removing ");
					handler7.AppendFormatted(entriesToDelete.Count);
					handler7.AppendLiteral(" stale code entries...");
				}
				LogService.Log(ref handler7);
				int deletedEvents = 0;
				foreach (GameMakerCode code in entriesToDelete)
				{
					List<GameMakerCode> children = new List<GameMakerCode>();
					foreach (GameMakerCode c3 in data.Code)
					{
						if (c3?.ParentEntry == code)
						{
							children.Add(c3);
						}
					}
					foreach (GameMakerCode child in children)
					{
						data.Code.Remove(child);
						for (int i2 = data.CodeLocals.Count - 1; i2 >= 0; i2--)
						{
							if (data.CodeLocals[i2].Name == child.Name)
							{
								data.CodeLocals.RemoveAt(i2);
								break;
							}
						}
					}
					foreach (GameMakerGameObject obj in data.GameObjects)
					{
						if (obj == null)
						{
							continue;
						}
						for (int evtType = 0; evtType < obj.Events.Count; evtType++)
						{
							GameMakerPointerList<GameMakerGameObject.Event> evtList = obj.Events[evtType];
							for (int i3 = evtList.Count - 1; i3 >= 0; i3--)
							{
								if (evtList[i3].Actions.Count > 0 && evtList[i3].Actions[0].CodeId == code)
								{
									evtList.RemoveAt(i3);
									deletedEvents++;
								}
							}
						}
					}
					foreach (GameMakerScript script in data.Scripts)
					{
						if (script?.Code == code)
						{
							script.Code = null;
						}
					}
					for (int i4 = data.GlobalInitScripts.Count - 1; i4 >= 0; i4--)
					{
						if (data.GlobalInitScripts[i4].Code == code)
						{
							data.GlobalInitScripts.RemoveAt(i4);
						}
					}
					for (int i5 = data.CodeLocals.Count - 1; i5 >= 0; i5--)
					{
						if (data.CodeLocals[i5].Name == code.Name)
						{
							data.CodeLocals.RemoveAt(i5);
							break;
						}
					}
					data.Code.Remove(code);
				}
				LogInterpolatedStringHandler handler8 = new LogInterpolatedStringHandler(51, 3, out shouldAppend);
				if (shouldAppend)
				{
					handler8.AppendLiteral("[ImportCodeEntries] Deleted ");
					handler8.AppendFormatted(entriesToDelete.Count);
					handler8.AppendLiteral(" entries + ");
					handler8.AppendFormatted(deletedEvents);
					handler8.AppendLiteral(" events in ");
					handler8.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
					handler8.AppendLiteral("s");
				}
				LogService.Log(ref handler8);
			}
		}
		phaseSw.Restart();
		bool touchesObjectEventCode = false;
		foreach (string entryKey3 in gmlEntries.Keys)
		{
			string codeName2 = targetTopLevelNamesByKey.GetValueOrDefault(entryKey3, codeEntryLogicalNames?.GetValueOrDefault(entryKey3) ?? entryKey3);
			if (codeName2.Contains("_Collision_", StringComparison.Ordinal) || codeName2.StartsWith("gml_Object_", StringComparison.Ordinal))
			{
				touchesObjectEventCode = true;
				break;
			}
		}
		if (!touchesObjectEventCode)
		{
			foreach (string entryKey4 in asmEntries.Keys)
			{
				string codeName3 = codeEntryLogicalNames?.GetValueOrDefault(entryKey4) ?? targetTopLevelNamesByKey.GetValueOrDefault(entryKey4, entryKey4);
				if (codeName3.Contains("_Collision_", StringComparison.Ordinal) || codeName3.StartsWith("gml_Object_", StringComparison.Ordinal))
				{
					touchesObjectEventCode = true;
					break;
				}
			}
		}
		bool hasQueuedGmlCompilation = regularCount + collisionCount > 0;
		if (touchesObjectEventCode && hasQueuedGmlCompilation)
		{
			LogService.Log("[ImportCodeEntries] Taking event snapshot before compilation...");
		}
		Dictionary<string, (GameMakerCode, GameMakerCodeLocals, GameMakerScript)> topLevelEntrySnapshot = new Dictionary<string, (GameMakerCode, GameMakerCodeLocals, GameMakerScript)>(StringComparer.Ordinal);
		Dictionary<string, (GameMakerCode, GameMakerCodeLocals, GameMakerScript, string)> childEntrySnapshot = new Dictionary<string, (GameMakerCode, GameMakerCodeLocals, GameMakerScript, string)>(StringComparer.Ordinal);
		List<(GameMakerCode, string)> pendingChildReattachments = new List<(GameMakerCode, string)>();
		if (hasQueuedGmlCompilation)
		{
			Dictionary<GameMakerCode, GameMakerScript> scriptByCode = new Dictionary<GameMakerCode, GameMakerScript>(data.Scripts.Count);
			foreach (GameMakerScript s in data.Scripts)
			{
				if (s?.Code != null)
				{
					scriptByCode.TryAdd(s.Code, s);
				}
			}
			Dictionary<GameMakerString, GameMakerCodeLocals> codeLocalsByName = new Dictionary<GameMakerString, GameMakerCodeLocals>(data.CodeLocals.Count);
			foreach (GameMakerCodeLocals cl in data.CodeLocals)
			{
				if (cl.Name != null)
				{
					codeLocalsByName.TryAdd(cl.Name, cl);
				}
			}
			Dictionary<string, GameMakerCode> snapshotArchiveLookup = BuildArchiveCodeLookup(data);
			foreach (CodeArchiveEntryDescriptor descriptor in CodeEntryArchiveIdentity.DescribeEntries(data))
			{
				if (descriptor.ParentArchiveKey == null && snapshotArchiveLookup.TryGetValue(descriptor.ArchiveKey, out var top) && top != null && top.Name?.Content != null)
				{
					scriptByCode.TryGetValue(top, out var script2);
					codeLocalsByName.TryGetValue(top.Name, out var locals);
					topLevelEntrySnapshot[descriptor.ArchiveKey] = (top, locals, script2);
				}
			}
			foreach (CodeArchiveEntryDescriptor descriptor2 in CodeEntryArchiveIdentity.DescribeEntries(data))
			{
				if (descriptor2.ParentArchiveKey != null && snapshotArchiveLookup.TryGetValue(descriptor2.ArchiveKey, out var nested2) && nested2 != null && nested2.Name?.Content != null)
				{
					scriptByCode.TryGetValue(nested2, out var script3);
					codeLocalsByName.TryGetValue(nested2.Name, out var locals2);
					childEntrySnapshot[descriptor2.ArchiveKey] = (nested2, locals2, script3, descriptor2.ParentArchiveKey);
				}
			}
		}
		Dictionary<string, List<(int, uint, string)>> eventSnapshot = new Dictionary<string, List<(int, uint, string)>>();
		if (touchesObjectEventCode && hasQueuedGmlCompilation)
		{
			Dictionary<string, int> objectNameCountsForEvents = BuildObjectNameCounts(data);
			for (int objIndex = 0; objIndex < data.GameObjects.Count; objIndex++)
			{
				GameMakerGameObject obj2 = data.GameObjects[objIndex];
				if (obj2 == null || obj2.Name?.Content == null)
				{
					continue;
				}
				List<(int, uint, string)> events = new List<(int, uint, string)>();
				for (int j = 0; j < obj2.Events.Count; j++)
				{
					foreach (GameMakerGameObject.Event evt in obj2.Events[j])
					{
						string evtCodeName = ((evt.Actions.Count > 0) ? (evt.Actions[0].CodeId?.Name?.Content ?? "") : "");
						events.Add((j, evt.EventSubtype, evtCodeName));
					}
				}
				eventSnapshot[GetObjectEventKey(obj2.Name.Content, objIndex, objectNameCountsForEvents)] = events;
			}
		}
		if (hasQueuedGmlCompilation)
		{
			LogInterpolatedStringHandler handler9 = new LogInterpolatedStringHandler(46, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler9.AppendLiteral("[ImportCodeEntries] Compiling ");
				handler9.AppendFormatted(regularCount + collisionCount);
				handler9.AppendLiteral(" code entries...");
			}
			LogService.Log(ref handler9);
			gmlCompilationRan = true;
			CompileResult compileResult = importGroup.Import(throwOnFailedCompile: false);
			if (!compileResult.Successful)
			{
				LogInterpolatedStringHandler handler10 = new LogInterpolatedStringHandler(41, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler10.AppendLiteral("[ImportCodeEntries] Compilation warning: ");
					handler10.AppendFormatted(compileResult.PrintAllErrors(codeEntryNames: true));
				}
				LogService.Log(ref handler10);
				int retryFailures = RetryGmlOnlyCodeEntriesIndividually(data, gmlOnlyEntries, collisionTargetsByCode);
				if (retryFailures > 0)
				{
					throw new InvalidDataException($"{retryFailures} GML code entr{((retryFailures == 1) ? "y" : "ies")} could not be compiled");
				}
			}
		}
		else
		{
			LogService.Log("[ImportCodeEntries] GML compilation skipped: ASM payload covers all queued entries");
		}
		LogInterpolatedStringHandler handler11 = new LogInterpolatedStringHandler(51, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler11.AppendLiteral("[ImportCodeEntries] Compilation phase complete in ");
			handler11.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F2");
			handler11.AppendLiteral("s");
		}
		LogService.Log(ref handler11);
		(GameMakerCode, GameMakerCodeLocals, GameMakerScript) value3;
		(GameMakerCode, GameMakerCodeLocals, GameMakerScript, string) value4;
		if (hasQueuedGmlCompilation)
		{
			Dictionary<string, GameMakerCode> currentArchiveLookupAfterCompile = BuildArchiveCodeLookup(data);
			LogWatchedArchiveCodeState("post-compile", currentArchiveLookupAfterCompile);
			HashSet<string> currentEntries = new HashSet<string>(BuildArchiveCodeLookup(data).Keys, StringComparer.Ordinal);
			HashSet<GameMakerCode> currentCodeObjects = new HashSet<GameMakerCode>(data.Code);
			HashSet<string> currentScripts = new HashSet<string>(data.Scripts.Count);
			foreach (GameMakerScript s2 in data.Scripts)
			{
				if (s2 != null && s2.Name?.Content != null)
				{
					currentScripts.Add(s2.Name.Content);
				}
			}
			int restoredTopLevel = 0;
			foreach (KeyValuePair<string, (GameMakerCode, GameMakerCodeLocals, GameMakerScript)> item3 in topLevelEntrySnapshot)
			{
				item3.Deconstruct(out value2, out value3);
				string entryKey5 = value2;
				(GameMakerCode, GameMakerCodeLocals, GameMakerScript) snapshot = value3;
				if ((targetCodeEntriesByKey.Count > 0 && !targetCodeEntriesByKey.ContainsKey(entryKey5)) || currentEntries.Contains(entryKey5))
				{
					continue;
				}
				(GameMakerCode, GameMakerCodeLocals, GameMakerScript) tuple2 = snapshot;
				GameMakerCode snapCode = tuple2.Item1;
				GameMakerCodeLocals snapLocals = tuple2.Item2;
				GameMakerScript snapScript = tuple2.Item3;
				snapCode.ParentEntry = null;
				if (!currentCodeObjects.Contains(snapCode))
				{
					data.Code.Add(snapCode);
					currentCodeObjects.Add(snapCode);
				}
				if (snapLocals != null)
				{
					bool localsExist = false;
					foreach (GameMakerCodeLocals codeLocal in data.CodeLocals)
					{
						if (codeLocal.Name == snapCode.Name)
						{
							localsExist = true;
							break;
						}
					}
					if (!localsExist)
					{
						data.CodeLocals.Add(snapLocals);
					}
				}
				if (snapScript != null && !currentScripts.Contains(snapScript.Name?.Content ?? ""))
				{
					snapScript.Code = snapCode;
					data.Scripts.Add(snapScript);
					currentScripts.Add(snapScript.Name?.Content ?? "");
				}
				restoredTopLevel++;
			}
			if (restoredTopLevel > 0)
			{
				LogInterpolatedStringHandler handler12 = new LogInterpolatedStringHandler(80, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler12.AppendLiteral("[ImportCodeEntries] Restored ");
					handler12.AppendFormatted(restoredTopLevel);
					handler12.AppendLiteral(" missing top-level code occurrence(s) from snapshot");
				}
				LogService.Log(ref handler12);
			}
			currentArchiveLookupAfterCompile = BuildArchiveCodeLookup(data);
			currentEntries = new HashSet<string>(currentArchiveLookupAfterCompile.Keys, StringComparer.Ordinal);
			int restored = 0;
			int repairedParenting = 0;
			foreach (KeyValuePair<string, (GameMakerCode, GameMakerCodeLocals, GameMakerScript, string)> item4 in childEntrySnapshot)
			{
				item4.Deconstruct(out value2, out value4);
				string entryKey6 = value2;
				(GameMakerCode, GameMakerCodeLocals, GameMakerScript, string) snapshot2 = value4;
				if (targetCodeEntriesByKey.Count > 0 && !targetCodeEntriesByKey.ContainsKey(entryKey6))
				{
					continue;
				}
				var (snapCode2, snapLocals2, snapScript2, parentArchiveKey) = snapshot2;
				if (currentCodeObjects.Contains(snapCode2) && currentArchiveLookupAfterCompile.TryGetValue(parentArchiveKey, out var desiredParent) && snapCode2.ParentEntry != desiredParent)
				{
					snapCode2.ParentEntry?.ChildEntries.Remove(snapCode2);
					snapCode2.ParentEntry = desiredParent;
					if (!desiredParent.ChildEntries.Contains(snapCode2))
					{
						desiredParent.ChildEntries.Add(snapCode2);
					}
					repairedParenting++;
					currentArchiveLookupAfterCompile = BuildArchiveCodeLookup(data);
					currentEntries = new HashSet<string>(currentArchiveLookupAfterCompile.Keys, StringComparer.Ordinal);
				}
				if (currentEntries.Contains(entryKey6))
				{
					continue;
				}
				snapCode2.ParentEntry = null;
				pendingChildReattachments.Add((snapCode2, parentArchiveKey));
				data.Code.Add(snapCode2);
				if (snapLocals2 != null)
				{
					bool localsExist2 = false;
					foreach (GameMakerCodeLocals codeLocal2 in data.CodeLocals)
					{
						if (codeLocal2.Name == snapCode2.Name)
						{
							localsExist2 = true;
							break;
						}
					}
					if (!localsExist2)
					{
						data.CodeLocals.Add(snapLocals2);
					}
				}
				if (snapScript2 != null && !currentScripts.Contains(snapScript2.Name?.Content ?? ""))
				{
					snapScript2.Code = snapCode2;
					data.Scripts.Add(snapScript2);
					LogInterpolatedStringHandler handler13 = new LogInterpolatedStringHandler(39, 2, out shouldAppend);
					if (shouldAppend)
					{
						handler13.AppendLiteral("  Restored detached child + script: ");
						handler13.AppendFormatted(snapCode2.Name?.Content);
						handler13.AppendLiteral(" [");
						handler13.AppendFormatted(entryKey6);
						handler13.AppendLiteral("]");
					}
					LogService.Log(ref handler13);
				}
				else
				{
					LogInterpolatedStringHandler handler14 = new LogInterpolatedStringHandler(30, 2, out shouldAppend);
					if (shouldAppend)
					{
						handler14.AppendLiteral("  Restored detached child: ");
						handler14.AppendFormatted(snapCode2.Name?.Content);
						handler14.AppendLiteral(" [");
						handler14.AppendFormatted(entryKey6);
						handler14.AppendLiteral("]");
					}
					LogService.Log(ref handler14);
				}
				restored++;
			}
			if (repairedParenting > 0)
			{
				LogInterpolatedStringHandler handler15 = new LogInterpolatedStringHandler(69, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler15.AppendLiteral("[ImportCodeEntries] Repaired parent linkage for ");
					handler15.AppendFormatted(repairedParenting);
					handler15.AppendLiteral(" child code entrie(s)");
				}
				LogService.Log(ref handler15);
			}
			if (restored > 0)
			{
				LogInterpolatedStringHandler handler16 = new LogInterpolatedStringHandler(92, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler16.AppendLiteral("[ImportCodeEntries] Restored ");
					handler16.AppendFormatted(restored);
					handler16.AppendLiteral(" child entries as standalone (detached from recompiled parents)");
				}
				LogService.Log(ref handler16);
			}
			currentArchiveLookupAfterCompile = BuildArchiveCodeLookup(data);
			LogWatchedArchiveCodeState("post-restore", currentArchiveLookupAfterCompile);
		}
		if (hasQueuedGmlCompilation)
		{
			List<GameMakerCode> spurious = new List<GameMakerCode>();
			foreach (GameMakerCode c4 in data.Code)
			{
				if (c4 != null && c4.Name?.Content != null && c4.ParentEntry == null && !preCompileEntries.Contains(c4.Name.Content) && !gmlEntries.ContainsKey(c4.Name.Content))
				{
					spurious.Add(c4);
				}
			}
			if (spurious.Count > 0)
			{
				LogInterpolatedStringHandler handler17 = new LogInterpolatedStringHandler(61, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler17.AppendLiteral("[ImportCodeEntries] Removing ");
					handler17.AppendFormatted(spurious.Count);
					handler17.AppendLiteral(" compiler side-effect entries...");
				}
				LogService.Log(ref handler17);
				foreach (GameMakerCode code2 in spurious)
				{
					List<GameMakerCode> spuriousChildren = new List<GameMakerCode>();
					foreach (GameMakerCode c5 in data.Code)
					{
						if (c5?.ParentEntry == code2)
						{
							spuriousChildren.Add(c5);
						}
					}
					foreach (GameMakerCode child2 in spuriousChildren)
					{
						data.Code.Remove(child2);
						for (int i6 = data.CodeLocals.Count - 1; i6 >= 0; i6--)
						{
							if (data.CodeLocals[i6].Name == child2.Name)
							{
								data.CodeLocals.RemoveAt(i6);
								break;
							}
						}
					}
					for (int i7 = data.Scripts.Count - 1; i7 >= 0; i7--)
					{
						if (data.Scripts[i7]?.Code == code2)
						{
							data.Scripts.RemoveAt(i7);
						}
					}
					for (int i8 = data.CodeLocals.Count - 1; i8 >= 0; i8--)
					{
						if (data.CodeLocals[i8].Name == code2.Name)
						{
							data.CodeLocals.RemoveAt(i8);
							break;
						}
					}
					data.Code.Remove(code2);
					LogInterpolatedStringHandler handler18 = new LogInterpolatedStringHandler(11, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler18.AppendLiteral("  Removed: ");
						handler18.AppendFormatted(code2.Name?.Content);
					}
					LogService.Log(ref handler18);
				}
			}
		}
		if (hasQueuedGmlCompilation)
		{
			int linkedScripts = 0;
			foreach (GameMakerScript script4 in data.Scripts)
			{
				if (script4 != null && script4.Name?.Content != null && script4.Code == null)
				{
					GameMakerCode code3 = ScriptCodeResolver.Resolve(data, script4.Name.Content);
					if (code3 != null)
					{
						script4.Code = code3;
						linkedScripts++;
					}
				}
			}
			if (linkedScripts > 0)
			{
				LogInterpolatedStringHandler handler19 = new LogInterpolatedStringHandler(73, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler19.AppendLiteral("[ImportCodeEntries] Linked ");
					handler19.AppendFormatted(linkedScripts);
					handler19.AppendLiteral(" Script entries to their code (were Code=null)");
				}
				LogService.Log(ref handler19);
			}
		}
		LogInterpolatedStringHandler handler20 = new LogInterpolatedStringHandler(41, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler20.AppendLiteral("[ImportCodeEntries] Compilation done in ");
			handler20.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
			handler20.AppendLiteral("s");
		}
		LogService.Log(ref handler20);
		phaseSw.Restart();
		int restoredEvents = 0;
		if (touchesObjectEventCode && hasQueuedGmlCompilation)
		{
			Dictionary<string, int> objectNameCountsForEvents2 = BuildObjectNameCounts(data);
			for (int k = 0; k < data.GameObjects.Count; k++)
			{
				GameMakerGameObject obj3 = data.GameObjects[k];
				if (obj3 == null || obj3.Name?.Content == null || !eventSnapshot.TryGetValue(GetObjectEventKey(obj3.Name.Content, k, objectNameCountsForEvents2), out var snapshot3))
				{
					continue;
				}
				HashSet<(int, uint)> expectedPairs = new HashSet<(int, uint)>();
				foreach (var (evtType2, subtype, _) in snapshot3)
				{
					expectedPairs.Add((evtType2, subtype));
				}
				for (int l = 0; l < obj3.Events.Count; l++)
				{
					GameMakerPointerList<GameMakerGameObject.Event> evtList2 = obj3.Events[l];
					for (int i9 = evtList2.Count - 1; i9 >= 0; i9--)
					{
						if (!expectedPairs.Contains((l, evtList2[i9].EventSubtype)))
						{
							evtList2.RemoveAt(i9);
							restoredEvents++;
						}
					}
				}
			}
		}
		if (restoredEvents > 0)
		{
			LogInterpolatedStringHandler handler21 = new LogInterpolatedStringHandler(69, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler21.AppendLiteral("[ImportCodeEntries] Removed ");
				handler21.AppendFormatted(restoredEvents);
				handler21.AppendLiteral(" spurious events added during compilation");
			}
			LogService.Log(ref handler21);
		}
		LogInterpolatedStringHandler handler22 = new LogInterpolatedStringHandler(53, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler22.AppendLiteral("[ImportCodeEntries] Event restore phase complete in ");
			handler22.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F2");
			handler22.AppendLiteral("s");
		}
		LogService.Log(ref handler22);
		Stopwatch eventCleanupSw = Stopwatch.StartNew();
		string objEventsContent = objectEventsContentOverride;
		string objEventsPath = Path.Combine(helpersDir, "object_events.json");
		if (objEventsContent == null && File.Exists(objEventsPath))
		{
			objEventsContent = File.ReadAllText(objEventsPath, Encoding.UTF8);
		}
		if (touchesObjectEventCode && hasQueuedGmlCompilation && objEventsContent != null)
		{
			JsonElement targetEventsRoot = JsonSerializer.Deserialize<JsonElement>(objEventsContent);
			int authRemoved = 0;
			Dictionary<string, int> objectNameCountsForCleanup = BuildObjectNameCounts(data);
			for (int m = 0; m < data.GameObjects.Count; m++)
			{
				GameMakerGameObject obj4 = data.GameObjects[m];
				if (obj4 == null || obj4.Name?.Content == null)
				{
					continue;
				}
				string eventKey = ResolveObjectEventsKey(targetEventsRoot, data, obj4.Name.Content, m, objectNameCountsForCleanup);
				if (eventKey == null || !targetEventsRoot.TryGetProperty(eventKey, out var targetEvents))
				{
					continue;
				}
				HashSet<string> targetEventKeys = new HashSet<string>();
				HashSet<string> targetCollisionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (JsonElement evt2 in targetEvents.EnumerateArray())
				{
					int t = evt2.GetProperty("t").GetInt32();
					if (t == 4 && evt2.TryGetProperty("cn", out var cnElem))
					{
						string cn = cnElem.GetString();
						if (!string.IsNullOrEmpty(cn))
						{
							targetCollisionNames.Add(cn);
						}
					}
					uint subtype2 = evt2.GetProperty("s").GetUInt32();
					if (t == 4 && evt2.TryGetProperty("cn", out var collNameElem))
					{
						string collName = collNameElem.GetString();
						JsonElement coElem;
						int occurrence = (evt2.TryGetProperty("co", out coElem) ? coElem.GetInt32() : 0);
						GameMakerGameObject collObj = ((!string.IsNullOrEmpty(collName)) ? ResolveGameObjectByNameOccurrence(data, collName, occurrence) : null);
						if (collObj == null)
						{
							continue;
						}
						subtype2 = (uint)data.GameObjects.IndexOf(collObj);
					}
					if (t < 0 || t >= obj4.Events.Count)
					{
						continue;
					}
					string eventKeepKey = $"{t}_{subtype2}";
					GameMakerGameObject.Event targetEvent = null;
					foreach (GameMakerGameObject.Event existingEvt in obj4.Events[t])
					{
						if (existingEvt.EventSubtype == subtype2)
						{
							targetEvent = existingEvt;
							break;
						}
					}
					if (targetEvent == null)
					{
						targetEvent = new GameMakerGameObject.Event
						{
							EventSubtype = subtype2
						};
						obj4.Events[t].Add(targetEvent);
					}
					bool hasLiveAction = false;
					JsonElement codeNameElem;
					if (evt2.TryGetProperty("actions", out var actionsElem) && actionsElem.ValueKind == JsonValueKind.Array)
					{
						targetEvent.Actions.Clear();
						foreach (JsonElement actionElm in actionsElem.EnumerateArray())
						{
							targetEvent.Actions.Add(CreateEventActionFromJson(data, actionElm));
						}
						hasLiveAction = targetEvent.Actions.Any((GameMakerGameObject.EventAction action) => action?.CodeId != null);
					}
					else if (evt2.TryGetProperty("c", out codeNameElem))
					{
						string codeName4 = codeNameElem.GetString();
						if (!string.IsNullOrEmpty(codeName4))
						{
							GameMakerCode code4 = data.Code.ByName(codeName4);
							if (code4 != null)
							{
								if (targetEvent.Actions.Count == 0)
								{
									targetEvent.Actions.Add(new GameMakerGameObject.EventAction
									{
										CodeId = code4
									});
								}
								else if (targetEvent.Actions[0].CodeId?.Name?.Content != codeName4)
								{
									targetEvent.Actions[0].CodeId = code4;
								}
								hasLiveAction = true;
							}
						}
					}
					if (hasLiveAction)
					{
						targetEventKeys.Add(eventKeepKey);
					}
					else
					{
						obj4.Events[t].Remove(targetEvent);
					}
				}
				for (int evtType3 = 0; evtType3 < obj4.Events.Count; evtType3++)
				{
					for (int j2 = obj4.Events[evtType3].Count - 1; j2 >= 0; j2--)
					{
						bool shouldKeep;
						if (evtType3 == 4)
						{
							int collObjIdx = (int)obj4.Events[evtType3][j2].EventSubtype;
							string collObjName = ((collObjIdx < 0 || collObjIdx >= data.GameObjects.Count) ? null : data.GameObjects[collObjIdx]?.Name?.Content);
							shouldKeep = !string.IsNullOrEmpty(collObjName) && targetCollisionNames.Contains(collObjName);
						}
						else
						{
							shouldKeep = targetEventKeys.Contains($"{evtType3}_{obj4.Events[evtType3][j2].EventSubtype}");
						}
						if (!shouldKeep)
						{
							obj4.Events[evtType3].RemoveAt(j2);
							authRemoved++;
						}
					}
				}
			}
			if (authRemoved > 0)
			{
				LogInterpolatedStringHandler handler23 = new LogInterpolatedStringHandler(73, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler23.AppendLiteral("[ImportCodeEntries] Removed ");
					handler23.AppendFormatted(authRemoved);
					handler23.AppendLiteral(" events not in TARGET (authoritative cleanup)");
				}
				LogService.Log(ref handler23);
			}
		}
		eventCleanupSw.Stop();
		LogInterpolatedStringHandler handler24 = new LogInterpolatedStringHandler(43, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler24.AppendLiteral("[ImportCodeEntries] Event cleanup done in ");
			handler24.AppendFormatted(eventCleanupSw.Elapsed.TotalSeconds, "F2");
			handler24.AppendLiteral("s");
		}
		LogService.Log(ref handler24);
		if (isAssetShiftRecompileOnly)
		{
			LogService.Log("[ImportCodeEntries] Function/variable reconciliation skipped for asset-shift recompile");
			LogInterpolatedStringHandler handler25 = new LogInterpolatedStringHandler(43, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler25.AppendLiteral("[ImportCodeEntries] Hybrid import done in ");
				handler25.AppendFormatted(sw.Elapsed.TotalSeconds, "F1");
				handler25.AppendLiteral("s");
			}
			LogService.Log(ref handler25);
			return;
		}
		Stopwatch functionReconcileSw = Stopwatch.StartNew();
		if (hasFunctionVariableTables && !gmlCompilationRan)
		{
			ApplyTargetFunctionVariableTables(data, root, "postcompile");
		}
		else if (hasFunctionVariableTables)
		{
			LogService.Log("[ImportCodeEntries] Function/variable reconciliation skipped after GML compilation");
		}
		functionReconcileSw.Stop();
		LogInterpolatedStringHandler handler26 = new LogInterpolatedStringHandler(63, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler26.AppendLiteral("[ImportCodeEntries] Function reconciliation phase complete in ");
			handler26.AppendFormatted(functionReconcileSw.Elapsed.TotalSeconds, "F2");
			handler26.AppendLiteral("s");
		}
		LogService.Log(ref handler26);
		Dictionary<string, GameMakerString> codeImportStringLookup = null;
		Stopwatch variableReconcileSw = Stopwatch.StartNew();
		variableReconcileSw.Stop();
		LogInterpolatedStringHandler handler27 = new LogInterpolatedStringHandler(63, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler27.AppendLiteral("[ImportCodeEntries] Variable reconciliation phase complete in ");
			handler27.AppendFormatted(variableReconcileSw.Elapsed.TotalSeconds, "F2");
			handler27.AppendLiteral("s");
		}
		LogService.Log(ref handler27);
		if (asmEntries.Count == 0)
		{
			LogService.Log("[ImportCodeEntries] ASM reassembly skipped: no ASM entries supplied");
			LogInterpolatedStringHandler handler28 = new LogInterpolatedStringHandler(43, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler28.AppendLiteral("[ImportCodeEntries] Hybrid import done in ");
				handler28.AppendFormatted(sw.Elapsed.TotalSeconds, "F1");
				handler28.AppendLiteral("s");
			}
			LogService.Log(ref handler28);
			return;
		}
		phaseSw.Restart();
		LogService.Log("[ImportCodeEntries] Reassembling from ASM for byte-perfect bytecode...");
		Stopwatch localVarLookupSw = Stopwatch.StartNew();
		Dictionary<string, int> localVarLookup = new Dictionary<string, int>();
		for (int vi = 0; vi < data.Variables.Count; vi++)
		{
			GameMakerVariable v = data.Variables[vi];
			string name = v?.Name?.Content;
			if (name != null && v.InstanceType == GameMakerInstruction.InstanceType.Local)
			{
				localVarLookup.TryAdd(name, vi);
			}
		}
		localVarLookupSw.Stop();
		Regex stripStringIdxRegex = StripStringIdxRegex();
		int reassembled = 0;
		int asmFailed = 0;
		List<(string, string, bool)> asmGmlFallbackEntries = new List<(string, string, bool)>();
		Stopwatch archiveLookupSw = Stopwatch.StartNew();
		archiveCodeLookup = BuildArchiveCodeLookup(data);
		LogWatchedArchiveCodeState("pre-asm", archiveCodeLookup);
		archiveLookupSw.Stop();
		Stopwatch skeletonSw = Stopwatch.StartNew();
		if (codeImportStringLookup == null)
		{
			codeImportStringLookup = BuildStringLookup(data);
		}
		int createdAsmSkeletons = 0;
		foreach (string entryKey7 in asmEntries.Keys)
		{
			if (archiveCodeLookup.ContainsKey(entryKey7))
			{
				continue;
			}
			string codeName5 = codeEntryLogicalNames?.GetValueOrDefault(entryKey7) ?? targetTopLevelNamesByKey.GetValueOrDefault(entryKey7) ?? Path.GetFileName(entryKey7);
			GameMakerCode newCode = new GameMakerCode
			{
				Name = MakeStringCached(data, codeImportStringLookup, codeName5)
			};
			if (asmEntryPaths != null && asmEntryPaths.TryGetValue(entryKey7, out string asmPath))
			{
				string[] parts = asmPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length >= 3)
				{
					string parentKey = parts[^2];
					if (!parentKey.Equals(entryKey7, StringComparison.OrdinalIgnoreCase) && archiveCodeLookup.TryGetValue(parentKey, out var parent))
					{
						newCode.ParentEntry = parent;
						if (!parent.ChildEntries.Contains(newCode))
						{
							parent.ChildEntries.Add(newCode);
						}
					}
				}
			}
			data.Code.Add(newCode);
			archiveCodeLookup[entryKey7] = newCode;
			createdAsmSkeletons++;
		}
		if (createdAsmSkeletons > 0)
		{
			LogInterpolatedStringHandler handler29 = new LogInterpolatedStringHandler(75, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler29.AppendLiteral("[ImportCodeEntries] Created ");
				handler29.AppendFormatted(createdAsmSkeletons);
				handler29.AppendLiteral(" missing ASM code skeletons from patch metadata");
			}
			LogService.Log(ref handler29);
		}
		skeletonSw.Stop();
		Stopwatch assemblerCacheSw = Stopwatch.StartNew();
		Assembler.SetLookupCaches(data);
		assemblerCacheSw.Stop();
		Stopwatch asmLoopSw = Stopwatch.StartNew();
		Stopwatch childDirectiveSw = new Stopwatch();
		Stopwatch asmPreprocessSw = new Stopwatch();
		Stopwatch assembleOnlySw = new Stopwatch();
		Stopwatch replaceInstructionsSw = new Stopwatch();
		HashSet<GameMakerCode> currentCodeSet = new HashSet<GameMakerCode>(data.Code);
		Dictionary<GameMakerCode, List<GameMakerScript>> scriptsByCode = new Dictionary<GameMakerCode, List<GameMakerScript>>();
		foreach (GameMakerScript script5 in data.Scripts)
		{
			if (script5?.Code != null)
			{
				if (!scriptsByCode.TryGetValue(script5.Code, out var scriptList))
				{
					scriptList = new List<GameMakerScript>();
					scriptsByCode[script5.Code] = scriptList;
				}
				scriptList.Add(script5);
			}
		}
		int preprocessedAsmEntries = 0;
		int childDirectiveEntries = 0;
		try
		{
			foreach (KeyValuePair<string, string> asmEntry in asmEntries)
			{
				asmEntry.Deconstruct(out value2, out key);
				string entryKey8 = value2;
				string asmText = key;
				if (!archiveCodeLookup.TryGetValue(entryKey8, out var code5))
				{
					continue;
				}
				string codeName6 = codeEntryLogicalNames?.GetValueOrDefault(entryKey8) ?? targetTopLevelNamesByKey.GetValueOrDefault(entryKey8) ?? code5.Name?.Content ?? entryKey8;
				try
				{
					List<string> targetChildNames = new List<string>();
					if (asmText.Contains("\n> ", StringComparison.Ordinal) || asmText.StartsWith("> ", StringComparison.Ordinal))
					{
						childDirectiveSw.Start();
						using StringReader sr = new StringReader(asmText);
						string ln;
						while ((ln = sr.ReadLine()) != null)
						{
							ln = ln.Trim();
							if (ln.StartsWith("> "))
							{
								string rest = ln.Substring(2).Trim();
								int sp = rest.IndexOf(' ');
								if (sp > 0)
								{
									targetChildNames.Add(rest.Substring(0, sp));
								}
							}
						}
						childDirectiveSw.Stop();
					}
					Dictionary<string, string> nameMap = new Dictionary<string, string>();
					if (targetChildNames.Count > 0)
					{
						childDirectiveEntries++;
						childDirectiveSw.Start();
						targetChildEntriesByParent.TryGetValue(entryKey8, out var explicitChildEntries);
						bool hasExplicitChildEntries = false;
						if (explicitChildEntries != null && explicitChildEntries.Count > 0)
						{
							List<(string, string)> explicitChildren = explicitChildEntries;
							hasExplicitChildEntries = true;
							foreach (GameMakerCode existingChild in code5.ChildEntries.ToList())
							{
								code5.ChildEntries.Remove(existingChild);
								if (existingChild.ParentEntry == code5)
								{
									existingChild.ParentEntry = null;
								}
								data.Code.Remove(existingChild);
								currentCodeSet.Remove(existingChild);
								for (int i10 = data.CodeLocals.Count - 1; i10 >= 0; i10--)
								{
									if (data.CodeLocals[i10].Name == existingChild.Name)
									{
										data.CodeLocals.RemoveAt(i10);
										break;
									}
								}
								if (!scriptsByCode.TryGetValue(existingChild, out var scriptsForChild))
								{
									continue;
								}
								foreach (GameMakerScript item5 in scriptsForChild)
								{
									item5.Code = null;
								}
							}
							foreach (var (childEntryKey, childLogicalName) in explicitChildren)
							{
								if (!archiveCodeLookup.TryGetValue(childEntryKey, out var desiredChild))
								{
									desiredChild = new GameMakerCode
									{
										Name = MakeStringCached(data, codeImportStringLookup, childLogicalName),
										ParentEntry = code5
									};
									data.Code.Add(desiredChild);
									currentCodeSet.Add(desiredChild);
									archiveCodeLookup[childEntryKey] = desiredChild;
								}
								else if (currentCodeSet.Add(desiredChild))
								{
									data.Code.Add(desiredChild);
								}
								desiredChild.ParentEntry = code5;
								if (!code5.ChildEntries.Contains(desiredChild))
								{
									code5.ChildEntries.Add(desiredChild);
								}
							}
						}
						HashSet<string> targetChildSet = new HashSet<string>(targetChildNames, StringComparer.Ordinal);
						foreach (GameMakerCode compiledChild in code5.ChildEntries.ToList())
						{
							string compiledName = compiledChild.Name?.Content;
							if (compiledName == null || targetChildSet.Contains(compiledName))
							{
								continue;
							}
							code5.ChildEntries.Remove(compiledChild);
							if (compiledChild.ParentEntry == code5)
							{
								compiledChild.ParentEntry = null;
							}
							data.Code.Remove(compiledChild);
							currentCodeSet.Remove(compiledChild);
							for (int i11 = data.CodeLocals.Count - 1; i11 >= 0; i11--)
							{
								if (data.CodeLocals[i11].Name == compiledChild.Name)
								{
									data.CodeLocals.RemoveAt(i11);
									break;
								}
							}
							if (!scriptsByCode.TryGetValue(compiledChild, out var scriptsForChild2))
							{
								continue;
							}
							foreach (GameMakerScript item6 in scriptsForChild2)
							{
								item6.Code = null;
							}
						}
						List<GameMakerCode> oursChildren = code5.ChildEntries.OrderBy((GameMakerCode gameMakerCode) => gameMakerCode.Offset).ToList();
						if (targetChildNames.Count != oursChildren.Count)
						{
							LogInterpolatedStringHandler handler30 = new LogInterpolatedStringHandler(103, 3, out shouldAppend);
							if (shouldAppend)
							{
								handler30.AppendLiteral("[ImportCodeEntries] Child topology differs for ");
								handler30.AppendFormatted(codeName6);
								handler30.AppendLiteral(": ASM has ");
								handler30.AppendFormatted(targetChildNames.Count);
								handler30.AppendLiteral(", compiled has ");
								handler30.AppendFormatted(oursChildren.Count);
								handler30.AppendLiteral("; continuing with partial remap");
							}
							LogService.Log(ref handler30);
						}
						int remapCount = ((!hasExplicitChildEntries) ? Math.Min(targetChildNames.Count, oursChildren.Count) : 0);
						for (int ci = 0; ci < remapCount; ci++)
						{
							string tName = targetChildNames[ci];
							string oName = oursChildren[ci].Name?.Content;
							if (!string.IsNullOrEmpty(oName) && tName != oName)
							{
								nameMap[tName] = oName;
							}
						}
						childDirectiveSw.Stop();
					}
					bool needsLocalVarRemap = asmText.Contains(".localvar", StringComparison.Ordinal);
					bool needsStringSuffixStrip = asmText.Contains("push.s ", StringComparison.Ordinal);
					bool needsChildNameRemap = nameMap.Count > 0;
					string assemblySource = asmText;
					if (needsLocalVarRemap || needsStringSuffixStrip || needsChildNameRemap)
					{
						preprocessedAsmEntries++;
						asmPreprocessSw.Start();
						StringBuilder sb = new StringBuilder(asmText.Length);
						using StringReader sr2 = new StringReader(asmText);
						string ln2;
						while ((ln2 = sr2.ReadLine()) != null)
						{
							string trimmed = ln2.Trim();
							if (needsLocalVarRemap && trimmed.StartsWith(".localvar", StringComparison.Ordinal))
							{
								string[] parts2 = trimmed.Split(' ');
								if (parts2.Length >= 4)
								{
									string varName = parts2[2];
									if (localVarLookup.TryGetValue(varName, out var newIdx))
									{
										parts2[3] = newIdx.ToString();
									}
									ln2 = string.Join(" ", parts2);
								}
							}
							if (needsStringSuffixStrip && trimmed.StartsWith("push.s ", StringComparison.Ordinal))
							{
								ln2 = stripStringIdxRegex.Replace(ln2, "\"");
							}
							if (needsChildNameRemap)
							{
								foreach (KeyValuePair<string, string> kvp in nameMap)
								{
									if (ln2.Contains(kvp.Key))
									{
										ln2 = ln2.Replace(kvp.Key, kvp.Value);
									}
								}
							}
							sb.AppendLine(ln2);
						}
						assemblySource = sb.ToString();
						asmPreprocessSw.Stop();
					}
					assembleOnlySw.Start();
					List<GameMakerInstruction> newInstructions = Assembler.Assemble(assemblySource, data, delegate(Action action)
					{
						action();
					}, code5);
					assembleOnlySw.Stop();
					replaceInstructionsSw.Start();
					code5.Instructions.Clear();
					foreach (GameMakerInstruction instr in newInstructions)
					{
						code5.Instructions.Add(instr);
					}
					uint totalWords = 0u;
					foreach (GameMakerInstruction instr2 in newInstructions)
					{
						totalWords += instr2.CalculateInstructionSize();
					}
					code5.Length = totalWords * 4;
					replaceInstructionsSw.Stop();
					reassembled++;
				}
				catch (Exception ex3)
				{
					asmFailed++;
					if (gmlEntries.TryGetValue(entryKey8, out string fallbackGml))
					{
						asmGmlFallbackEntries.Add((codeName6, fallbackGml, codeName6.Contains("_Collision_", StringComparison.Ordinal)));
					}
					if (asmFailed <= 5)
					{
						LogInterpolatedStringHandler handler31 = new LogInterpolatedStringHandler(36, 2, out shouldAppend);
						if (shouldAppend)
						{
							handler31.AppendLiteral("[ImportCodeEntries] ASM error for ");
							handler31.AppendFormatted(codeName6);
							handler31.AppendLiteral(": ");
							handler31.AppendFormatted(ex3.Message);
						}
						LogService.Log(ref handler31);
					}
				}
			}
		}
		finally
		{
			Assembler.ClearLookupCaches();
		}
		asmLoopSw.Stop();
		if (asmGmlFallbackEntries.Count > 0)
		{
			gmlCompilationRan = true;
			int fallbackFailed = RetryGmlOnlyCodeEntriesIndividually(data, asmGmlFallbackEntries, collisionTargetsByCode);
			int recovered = asmGmlFallbackEntries.Count - fallbackFailed;
			asmFailed -= recovered;
			reassembled += recovered;
			LogInterpolatedStringHandler handler32 = new LogInterpolatedStringHandler(59, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler32.AppendLiteral("[ImportCodeEntries] Recovered ");
				handler32.AppendFormatted(recovered);
				handler32.AppendLiteral("/");
				handler32.AppendFormatted(asmGmlFallbackEntries.Count);
				handler32.AppendLiteral(" failed ASM entries from GML");
			}
			LogService.Log(ref handler32);
		}
		Stopwatch postAsmTopologySw = Stopwatch.StartNew();
		if (pendingChildReattachments.Count > 0)
		{
			int reattachedChildren = 0;
			archiveCodeLookup = BuildArchiveCodeLookup(data);
			foreach (var (child3, parentArchiveKey2) in pendingChildReattachments)
			{
				if (archiveCodeLookup.TryGetValue(parentArchiveKey2, out var parent2))
				{
					child3.ParentEntry = parent2;
					if (!parent2.ChildEntries.Contains(child3))
					{
						parent2.ChildEntries.Add(child3);
					}
					reattachedChildren++;
				}
			}
			if (reattachedChildren > 0)
			{
				LogInterpolatedStringHandler handler33 = new LogInterpolatedStringHandler(75, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler33.AppendLiteral("[ImportCodeEntries] Reattached ");
					handler33.AppendFormatted(reattachedChildren);
					handler33.AppendLiteral(" restored child entries after ASM reassembly");
				}
				LogService.Log(ref handler33);
			}
		}
		if (hasCompleteCodeEntryMetadata && targetCodeEntriesByKey.Count > 0)
		{
			int canonicalParents = 0;
			int canonicalChildren = 0;
			Dictionary<string, GameMakerCode> allTrackedCodes = new Dictionary<string, GameMakerCode>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, (GameMakerCode, GameMakerCodeLocals, GameMakerScript)> item7 in topLevelEntrySnapshot)
			{
				item7.Deconstruct(out key, out value3);
				(allTrackedCodes[key], _, _) = value3;
			}
			foreach (KeyValuePair<string, (GameMakerCode, GameMakerCodeLocals, GameMakerScript, string)> item8 in childEntrySnapshot)
			{
				item8.Deconstruct(out key, out value4);
				(allTrackedCodes[key], _, _, _) = value4;
			}
			archiveCodeLookup = BuildArchiveCodeLookup(data);
			foreach (KeyValuePair<string, GameMakerCode> item9 in archiveCodeLookup)
			{
				item9.Deconstruct(out key, out var value5);
				string entryKey11 = key;
				GameMakerCode code6 = value5;
				allTrackedCodes[entryKey11] = code6;
			}
			foreach (KeyValuePair<string, (string, int, string)> item10 in targetCodeEntriesByKey)
			{
				item10.Deconstruct(out key, out value);
				string entryKey12 = key;
				(string, int, string) meta2 = value;
				if (allTrackedCodes.TryGetValue(entryKey12, out var code7))
				{
					if (currentCodeSet.Add(code7))
					{
						data.Code.Add(code7);
					}
					if (meta2.Item3 == null)
					{
						code7.ParentEntry?.ChildEntries.Remove(code7);
						code7.ParentEntry = null;
						code7.ChildEntries.Clear();
						canonicalParents++;
					}
				}
			}
			foreach (KeyValuePair<string, (string, int, string)> item11 in targetCodeEntriesByKey)
			{
				item11.Deconstruct(out key, out value);
				string entryKey13 = key;
				(string, int, string) meta3 = value;
				if (meta3.Item3 != null && allTrackedCodes.TryGetValue(entryKey13, out var child4) && allTrackedCodes.TryGetValue(meta3.Item3, out var parent3))
				{
					if (currentCodeSet.Add(child4))
					{
						data.Code.Add(child4);
					}
					if (currentCodeSet.Add(parent3))
					{
						data.Code.Add(parent3);
					}
					if (child4.ParentEntry != null && child4.ParentEntry != parent3)
					{
						child4.ParentEntry.ChildEntries.Remove(child4);
					}
					child4.ParentEntry = parent3;
					if (!parent3.ChildEntries.Contains(child4))
					{
						parent3.ChildEntries.Add(child4);
					}
					canonicalChildren++;
				}
			}
			LogInterpolatedStringHandler handler34 = new LogInterpolatedStringHandler(92, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler34.AppendLiteral("[ImportCodeEntries] Canonicalized code topology from helper metadata: ");
				handler34.AppendFormatted(canonicalParents);
				handler34.AppendLiteral(" parents, ");
				handler34.AppendFormatted(canonicalChildren);
				handler34.AppendLiteral(" child links");
			}
			LogService.Log(ref handler34);
		}
		if (hasCompleteCodeEntryMetadata)
		{
			ReorderCodeEntriesFromTargetMetadata(data, targetCodeEntriesByKey);
			if (!gmlCompilationRan)
			{
				PruneCodeEntriesOutsideTargetMetadata(data, targetCodeEntriesByKey);
			}
		}
		archiveCodeLookup = BuildArchiveCodeLookup(data);
		LogWatchedArchiveCodeState("post-asm", archiveCodeLookup);
		postAsmTopologySw.Stop();
		if (asmFailed > 5)
		{
			LogInterpolatedStringHandler handler35 = new LogInterpolatedStringHandler(57, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler35.AppendLiteral("[ImportCodeEntries] ... and ");
				handler35.AppendFormatted(asmFailed - 5);
				handler35.AppendLiteral(" more ASM errors (suppressed)");
			}
			LogService.Log(ref handler35);
		}
		LogInterpolatedStringHandler handler36 = new LogInterpolatedStringHandler(165, 14, out shouldAppend);
		if (shouldAppend)
		{
			handler36.AppendLiteral("[ImportCodeEntries] ASM timings: funcs=");
			handler36.AppendFormatted(functionReconcileSw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s, vars=");
			handler36.AppendFormatted(variableReconcileSw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s, localvars=");
			handler36.AppendFormatted(localVarLookupSw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s, codeLookup=");
			handler36.AppendFormatted(archiveLookupSw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s, skeletons=");
			handler36.AppendFormatted(skeletonSw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s, cache=");
			handler36.AppendFormatted(assemblerCacheSw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s, loop=");
			handler36.AppendFormatted(asmLoopSw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s, child=");
			handler36.AppendFormatted(childDirectiveSw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s/");
			handler36.AppendFormatted(childDirectiveEntries);
			handler36.AppendLiteral(", preprocess=");
			handler36.AppendFormatted(asmPreprocessSw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s/");
			handler36.AppendFormatted(preprocessedAsmEntries);
			handler36.AppendLiteral(", assemble=");
			handler36.AppendFormatted(assembleOnlySw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s, replace=");
			handler36.AppendFormatted(replaceInstructionsSw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s, topology=");
			handler36.AppendFormatted(postAsmTopologySw.Elapsed.TotalSeconds, "F2");
			handler36.AppendLiteral("s");
		}
		LogService.Log(ref handler36);
		LogInterpolatedStringHandler handler37 = new LogInterpolatedStringHandler(46, 3, out shouldAppend);
		if (shouldAppend)
		{
			handler37.AppendLiteral("[ImportCodeEntries] Reassembled ");
			handler37.AppendFormatted(reassembled);
			handler37.AppendLiteral("/");
			handler37.AppendFormatted(reassembled + asmFailed);
			handler37.AppendLiteral(" entries in ");
			handler37.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
			handler37.AppendLiteral("s");
		}
		LogService.Log(ref handler37);
		if (asmFailed > 0)
		{
			throw new InvalidDataException($"{asmFailed} code entr{((asmFailed == 1) ? "y" : "ies")} could not be imported from ASM or GML");
		}
		sw.Stop();
		LogInterpolatedStringHandler handler38 = new LogInterpolatedStringHandler(47, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler38.AppendLiteral("[ImportCodeEntries] Hybrid import complete in ");
			handler38.AppendFormatted(sw.Elapsed.TotalSeconds, "F1");
			handler38.AppendLiteral("s");
		}
		LogService.Log(ref handler38);
	}

	private static int RetryGmlOnlyCodeEntriesIndividually(GameMakerData data, List<(string CodeName, string GmlCode, bool IsCollision)> gmlOnlyEntries, Dictionary<string, string> collisionTargetsByCode)
	{
		if (gmlOnlyEntries.Count == 0)
		{
			return 0;
		}
		Stopwatch phaseSw = Stopwatch.StartNew();
		bool shouldAppend;
		LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(90, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[ImportCodeEntries] Retrying ");
			handler.AppendFormatted(gmlOnlyEntries.Count);
			handler.AppendLiteral(" GML-only entries individually after group compile failure...");
		}
		LogService.Log(ref handler);
		GlobalDecompileContext ctx = new GlobalDecompileContext(data);
		ctx.PrepareForCompilation();
		int applied = 0;
		int failed = 0;
		foreach (var (CodeName, GmlCode, IsCollision) in gmlOnlyEntries)
		{
			try
			{
				Dictionary<string, GameMakerCode> codeLookup = new Dictionary<string, GameMakerCode>(data.Code.Count);
				foreach (GameMakerCode item in data.Code)
				{
					foreach (GameMakerCode nested in EnumerateCodeTreeRecursive(item))
					{
						if (nested != null && nested.Name?.Content != null)
						{
							codeLookup.TryAdd(nested.Name.Content, nested);
						}
					}
				}
				Dictionary<string, (GameMakerGameObject, int)> goLookup = new Dictionary<string, (GameMakerGameObject, int)>(data.GameObjects.Count);
				for (int i = 0; i < data.GameObjects.Count; i++)
				{
					GameMakerGameObject go = data.GameObjects[i];
					if (go != null && go.Name?.Content != null)
					{
						goLookup.TryAdd(go.Name.Content, (go, i));
					}
				}
				CodeImportGroup singleGroup = new CodeImportGroup(data, ctx)
				{
					AutoCreateGlobalInitScripts = false,
					AutoCreateAssets = true
				};
				if (IsCollision)
				{
					ImportCollisionEvent(data, CodeName, GmlCode, singleGroup, codeLookup, goLookup, collisionTargetsByCode);
				}
				else
				{
					EnsureObjectEventForCode(data, CodeName, codeLookup, goLookup);
					if (codeLookup.TryGetValue(CodeName, out var existing) && existing.ParentEntry == null)
					{
						singleGroup.QueueReplace(existing, GmlCode);
					}
					else
					{
						singleGroup.QueueReplace(CodeName, GmlCode);
					}
				}
				CompileResult result = singleGroup.Import(throwOnFailedCompile: false);
				if (result.Successful)
				{
					applied++;
					continue;
				}
				failed++;
				LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(48, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler2.AppendLiteral("[ImportCodeEntries] GML-only retry failed for ");
					handler2.AppendFormatted(CodeName);
					handler2.AppendLiteral(": ");
					handler2.AppendFormatted(result.PrintAllErrors(codeEntryNames: true));
				}
				LogService.Log(ref handler2);
			}
			catch (Exception ex)
			{
				failed++;
				LogInterpolatedStringHandler handler3 = new LogInterpolatedStringHandler(51, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler3.AppendLiteral("[ImportCodeEntries] GML-only retry exception for ");
					handler3.AppendFormatted(CodeName);
					handler3.AppendLiteral(": ");
					handler3.AppendFormatted(ex.Message);
				}
				LogService.Log(ref handler3);
			}
		}
		LogInterpolatedStringHandler handler4 = new LogInterpolatedStringHandler(59, 4, out shouldAppend);
		if (shouldAppend)
		{
			handler4.AppendLiteral("[ImportCodeEntries] GML-only retry applied ");
			handler4.AppendFormatted(applied);
			handler4.AppendLiteral("/");
			handler4.AppendFormatted(gmlOnlyEntries.Count);
			handler4.AppendLiteral(", failed ");
			handler4.AppendFormatted(failed);
			handler4.AppendLiteral(", in ");
			handler4.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
			handler4.AppendLiteral("s");
		}
		LogService.Log(ref handler4);
		return failed;
	}

	private static void ApplyTargetFunctionVariableTables(GameMakerData data, JsonElement root, string phase)
	{
		ApplyTargetFunctionsExact(data, root, phase);
		ApplyTargetVariablesExact(data, root, phase);
	}

	private static void ApplyTargetFunctionsExact(GameMakerData data, JsonElement root, string phase)
	{
		if (!root.TryGetProperty("functions", out var funcArray))
		{
			return;
		}
		Dictionary<string, GameMakerFunction> oldByName = new Dictionary<string, GameMakerFunction>(StringComparer.Ordinal);
		foreach (GameMakerFunction function in data.Functions)
		{
			string name = function?.Name?.Content;
			if (!string.IsNullOrEmpty(name) && function != null)
			{
				oldByName.TryAdd(name, function);
			}
		}
		Dictionary<string, GameMakerString> stringLookup = BuildStringLookup(data);
		List<GameMakerFunction> newList = new List<GameMakerFunction>(funcArray.GetArrayLength());
		Dictionary<string, GameMakerFunction> newByName = new Dictionary<string, GameMakerFunction>(StringComparer.Ordinal);
		foreach (JsonElement item in funcArray.EnumerateArray())
		{
			string name2 = item.GetString();
			if (!string.IsNullOrEmpty(name2))
			{
				name2 = FunctionNameUtil.CanonicalizeFunctionName(name2);
				GameMakerFunction existing;
				GameMakerFunction function2 = (oldByName.TryGetValue(name2, out existing) ? existing : new GameMakerFunction
				{
					Name = MakeStringCached(data, stringLookup, name2)
				});
				function2.Name = MakeStringCached(data, stringLookup, name2);
				newList.Add(function2);
				newByName.TryAdd(name2, function2);
			}
		}
		data.Functions.Clear();
		foreach (GameMakerFunction function3 in newList)
		{
			data.Functions.Add(function3);
		}
		int remapped = 0;
		foreach (GameMakerCode item2 in data.Code)
		{
			foreach (GameMakerCode item3 in EnumerateCodeTreeRecursive(item2))
			{
				foreach (GameMakerInstruction instruction in item3.Instructions ?? new List<GameMakerInstruction>())
				{
					string name3 = instruction.ValueFunction?.Name?.Content;
					if (!string.IsNullOrEmpty(name3) && newByName.TryGetValue(FunctionNameUtil.CanonicalizeFunctionName(name3), out var replacement) && instruction.ValueFunction != replacement)
					{
						instruction.ValueFunction = replacement;
						remapped++;
					}
				}
			}
		}
		bool shouldAppend;
		LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(49, 3, out shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[ImportCodeEntries] Functions ");
			handler.AppendFormatted(phase);
			handler.AppendLiteral(": exact=");
			handler.AppendFormatted(data.Functions.Count);
			handler.AppendLiteral(", remapped=");
			handler.AppendFormatted(remapped);
		}
		LogService.Log(ref handler);
	}

	private static void ApplyTargetVariablesExact(GameMakerData data, JsonElement root, string phase)
	{
		if (!root.TryGetProperty("variables", out var varArray))
		{
			return;
		}
		List<GameMakerVariable> newList = new List<GameMakerVariable>(varArray.GetArrayLength());
		Dictionary<(string, GameMakerInstruction.InstanceType, int), GameMakerVariable> newExact = new Dictionary<(string, GameMakerInstruction.InstanceType, int), GameMakerVariable>();
		Dictionary<(string, GameMakerInstruction.InstanceType), GameMakerVariable> newByNameType = new Dictionary<(string, GameMakerInstruction.InstanceType), GameMakerVariable>();
		foreach (JsonElement vElem in varArray.EnumerateArray())
		{
			string name = vElem.GetProperty("n").GetString() ?? "";
			int instType = vElem.GetProperty("t").GetInt32();
			int varId = vElem.GetProperty("id").GetInt32();
			if (!string.IsNullOrEmpty(name))
			{
				GameMakerInstruction.InstanceType instanceType = (GameMakerInstruction.InstanceType)instType;
				GameMakerVariable variable = new GameMakerVariable
				{
					Name = MakeDistinctString(data, name),
					InstanceType = instanceType,
					VarID = varId
				};
				newList.Add(variable);
				newExact.TryAdd((name, instanceType, varId), variable);
				newByNameType.TryAdd((name, instanceType), variable);
			}
		}
		data.Variables.Clear();
		foreach (GameMakerVariable variable2 in newList)
		{
			data.Variables.Add(variable2);
		}
		int remapped = 0;
		foreach (GameMakerCode item in data.Code)
		{
			foreach (GameMakerCode item2 in EnumerateCodeTreeRecursive(item))
			{
				foreach (GameMakerInstruction instruction in item2.Instructions ?? new List<GameMakerInstruction>())
				{
					GameMakerVariable variable3 = instruction.ValueVariable;
					string name2 = variable3?.Name?.Content;
					if (!string.IsNullOrEmpty(name2) && variable3 != null)
					{
						if (!newExact.TryGetValue((name2, variable3.InstanceType, variable3.VarID), out var replacement))
						{
							newByNameType.TryGetValue((name2, variable3.InstanceType), out replacement);
						}
						if (replacement != null && variable3 != replacement)
						{
							instruction.ValueVariable = replacement;
							remapped++;
						}
					}
				}
			}
		}
		bool shouldAppend;
		LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(49, 3, out shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[ImportCodeEntries] Variables ");
			handler.AppendFormatted(phase);
			handler.AppendLiteral(": exact=");
			handler.AppendFormatted(data.Variables.Count);
			handler.AppendLiteral(", remapped=");
			handler.AppendFormatted(remapped);
		}
		LogService.Log(ref handler);
	}

	private static GameMakerString MakeDistinctString(GameMakerData data, string content)
	{
		GameMakerString created = new GameMakerString(content);
		if (data.Strings is GameMakerObservableList<GameMakerString> stringList)
		{
			stringList.InternalAdd(created);
		}
		else
		{
			data.Strings.Add(created);
		}
		return created;
	}

	private static GameMakerString MakeStringCached(GameMakerData data, Dictionary<string, GameMakerString> stringLookup, string content)
	{
		if (stringLookup.TryGetValue(content, out GameMakerString existing))
		{
			return existing;
		}
		GameMakerString created = new GameMakerString(content);
		if (data.Strings is GameMakerObservableList<GameMakerString> stringList)
		{
			stringList.InternalAdd(created);
		}
		else
		{
			data.Strings.Add(created);
		}
		stringLookup[content] = created;
		return created;
	}

	private static Dictionary<string, GameMakerString> BuildStringLookup(GameMakerData data)
	{
		Dictionary<string, GameMakerString> stringLookup = new Dictionary<string, GameMakerString>(data.Strings.Count, StringComparer.Ordinal);
		foreach (GameMakerString str in data.Strings)
		{
			if (str != null && str.Content != null)
			{
				stringLookup.TryAdd(str.Content, str);
			}
		}
		return stringLookup;
	}

	private static void EnsureObjectEventForCode(GameMakerData data, string codeName, Dictionary<string, GameMakerCode> codeLookup, Dictionary<string, (GameMakerGameObject obj, int idx)> goLookup)
	{
		if (!TryParseObjectEventCodeName(codeName, out string objectName, out int eventType, out uint eventSubtype) || !goLookup.TryGetValue(objectName, out (GameMakerGameObject, int) objEntry))
		{
			return;
		}
		var (obj, _) = objEntry;
		if (eventType < 0 || eventType >= obj.Events.Count)
		{
			return;
		}
		if (!codeLookup.TryGetValue(codeName, out GameMakerCode codeEntry))
		{
			codeEntry = (codeLookup[codeName] = GameMakerCode.CreateEmptyEntry(data, codeName));
		}
		GameMakerPointerList<GameMakerGameObject.Event> eventList = obj.Events[eventType];
		GameMakerGameObject.Event targetEvent = null;
		foreach (GameMakerGameObject.Event evt in eventList)
		{
			if (evt.EventSubtype == eventSubtype)
			{
				targetEvent = evt;
				break;
			}
		}
		if (targetEvent == null)
		{
			targetEvent = new GameMakerGameObject.Event
			{
				EventSubtype = eventSubtype
			};
			eventList.Add(targetEvent);
		}
		if (targetEvent.Actions.Count == 0)
		{
			targetEvent.Actions.Add(new GameMakerGameObject.EventAction
			{
				CodeId = codeEntry
			});
		}
		else if (targetEvent.Actions[0].CodeId?.Name?.Content != codeName)
		{
			targetEvent.Actions[0].CodeId = codeEntry;
		}
	}

	private static Dictionary<string, int> BuildObjectNameCounts(GameMakerData data)
	{
		Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (GameMakerGameObject gameObject in data.GameObjects)
		{
			string name = gameObject?.Name?.Content;
			if (name != null)
			{
				counts[name] = counts.GetValueOrDefault(name) + 1;
			}
		}
		return counts;
	}

	private static string GetObjectEventKey(string objectName, int objectIndex, Dictionary<string, int> objectNameCounts)
	{
		if (objectNameCounts.GetValueOrDefault(objectName) <= 1)
		{
			return objectName;
		}
		return $"{objectName}__idx{objectIndex:D4}";
	}

	private static string? ResolveObjectEventsKey(JsonElement targetEventsRoot, GameMakerData data, string objectName, int objectIndex, Dictionary<string, int> objectNameCounts)
	{
		if (objectNameCounts.GetValueOrDefault(objectName) <= 1)
		{
			string exactKey = GetObjectEventKey(objectName, objectIndex, objectNameCounts);
			if (targetEventsRoot.TryGetProperty(exactKey, out var value))
			{
				return exactKey;
			}
			if (!targetEventsRoot.TryGetProperty(objectName, out value))
			{
				return null;
			}
			return objectName;
		}
		int occurrence = 0;
		for (int i = 0; i < objectIndex; i++)
		{
			if (string.Equals(data.GameObjects[i]?.Name?.Content, objectName, StringComparison.Ordinal))
			{
				occurrence++;
			}
		}
		List<(int, string)> duplicateKeys = new List<(int, string)>();
		string prefix = objectName + "__idx";
		foreach (JsonProperty prop in targetEventsRoot.EnumerateObject())
		{
			if (prop.Name.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(prop.Name.AsSpan(prefix.Length), out var idx))
			{
				duplicateKeys.Add((idx, prop.Name));
			}
		}
		if (duplicateKeys.Count == 0)
		{
			return null;
		}
		duplicateKeys.Sort(((int idx, string key) a, (int idx, string key) b) => a.idx.CompareTo(b.idx));
		if (occurrence < 0 || occurrence >= duplicateKeys.Count)
		{
			return null;
		}
		return duplicateKeys[occurrence].Item2;
	}

	private static void ApplyAuthoritativeObjectEvents(GameMakerData data, string objectEventsJson, HashSet<string>? changedCodeNames = null)
	{
		using JsonDocument doc = JsonDocument.Parse(objectEventsJson);
		JsonElement root = doc.RootElement;
		Dictionary<string, int> objectNameCounts = BuildObjectNameCounts(data);
		for (int objIndex = 0; objIndex < data.GameObjects.Count; objIndex++)
		{
			GameMakerGameObject obj = data.GameObjects[objIndex];
			if (obj == null || obj.Name?.Content == null)
			{
				continue;
			}
			string eventKey = ResolveObjectEventsKey(root, data, obj.Name.Content, objIndex, objectNameCounts);
			if (eventKey == null || !root.TryGetProperty(eventKey, out var targetEvents))
			{
				continue;
			}
			List<(int, uint, JsonElement?, string, string)> desired = new List<(int, uint, JsonElement?, string, string)>();
			bool objectUsesChangedCode = changedCodeNames == null;
			foreach (JsonElement evt in targetEvents.EnumerateArray())
			{
				int type = evt.GetProperty("t").GetInt32();
				uint subtype = evt.GetProperty("s").GetUInt32();
				JsonElement actionsElem;
				JsonElement? actions = ((evt.TryGetProperty("actions", out actionsElem) && actionsElem.ValueKind == JsonValueKind.Array) ? new JsonElement?(actionsElem) : ((JsonElement?)null));
				JsonElement c;
				string codeName = (evt.TryGetProperty("c", out c) ? (c.GetString() ?? "") : "");
				JsonElement cn;
				string collisionName = (evt.TryGetProperty("cn", out cn) ? cn.GetString() : null);
				if (!objectUsesChangedCode)
				{
					if (!string.IsNullOrEmpty(codeName) && changedCodeNames.Contains(codeName))
					{
						objectUsesChangedCode = true;
					}
					else if (actions.HasValue)
					{
						foreach (JsonElement item in actions.Value.EnumerateArray())
						{
							if (item.TryGetProperty("codeId", out var codeIdElm) && changedCodeNames.Contains(codeIdElm.GetString() ?? ""))
							{
								objectUsesChangedCode = true;
								break;
							}
						}
					}
				}
				JsonElement co;
				int collisionOccurrence = (evt.TryGetProperty("co", out co) ? co.GetInt32() : 0);
				if (type == 4 && !string.IsNullOrEmpty(collisionName))
				{
					GameMakerGameObject collObj = ResolveGameObjectByNameOccurrence(data, collisionName, collisionOccurrence);
					if (collObj == null)
					{
						continue;
					}
					subtype = (uint)data.GameObjects.IndexOf(collObj);
				}
				desired.Add((type, subtype, actions, codeName, collisionName));
			}
			if (!objectUsesChangedCode)
			{
				continue;
			}
			for (int eventType = 0; eventType < obj.Events.Count; eventType++)
			{
				obj.Events[eventType].Clear();
			}
			foreach (var evt2 in desired)
			{
				if (evt2.Item1 < 0 || evt2.Item1 >= obj.Events.Count)
				{
					continue;
				}
				GameMakerGameObject.Event targetEvent = new GameMakerGameObject.Event
				{
					EventSubtype = evt2.Item2
				};
				if (evt2.Item3.HasValue)
				{
					foreach (JsonElement actionElm in evt2.Item3.Value.EnumerateArray())
					{
						targetEvent.Actions.Add(CreateEventActionFromJson(data, actionElm));
					}
				}
				else if (!string.IsNullOrEmpty(evt2.Item4))
				{
					GameMakerCode code = data.Code.ByName(evt2.Item4);
					if (code != null)
					{
						targetEvent.Actions.Add(new GameMakerGameObject.EventAction
						{
							CodeId = code
						});
					}
				}
				if (targetEvent.Actions.Any((GameMakerGameObject.EventAction action) => action?.CodeId != null))
				{
					obj.Events[evt2.Item1].Add(targetEvent);
				}
			}
		}
	}

	private static GameMakerGameObject? ResolveGameObjectByNameOccurrence(GameMakerData data, string objectName, int occurrence)
	{
		int targetOccurrence = Math.Max(occurrence, 0);
		int seen = 0;
		foreach (GameMakerGameObject obj in data.GameObjects)
		{
			if (string.Equals(obj?.Name?.Content, objectName, StringComparison.Ordinal))
			{
				if (seen == targetOccurrence)
				{
					return obj;
				}
				seen++;
			}
		}
		return data.GameObjects.ByName(objectName);
	}

	private static bool TryParseObjectEventCodeName(string codeName, out string objectName, out int eventType, out uint eventSubtype)
	{
		objectName = string.Empty;
		eventType = -1;
		eventSubtype = 0u;
		if (!codeName.StartsWith("gml_Object_", StringComparison.Ordinal))
		{
			return false;
		}
		if (codeName.Contains("_Collision_", StringComparison.Ordinal))
		{
			return false;
		}
		(string, int)[] array = new(string, int)[11]
		{
			("_Create_", 0),
			("_Destroy_", 1),
			("_Alarm_", 2),
			("_Step_", 3),
			("_Mouse_", 6),
			("_Other_", 7),
			("_Draw_", 8),
			("_KeyPress_", 9),
			("_KeyRelease_", 10),
			("_CleanUp_", 12),
			("_PreCreate_", 14)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, int) tuple = array[i];
			string suffix = tuple.Item1;
			int type = tuple.Item2;
			int idx = codeName.LastIndexOf(suffix, StringComparison.Ordinal);
			if (idx > "gml_Object_".Length && uint.TryParse(codeName.Substring(idx + suffix.Length), out eventSubtype))
			{
				int length = "gml_Object_".Length;
				objectName = codeName.Substring(length, idx - length);
				eventType = type;
				return true;
			}
		}
		return false;
	}

	private static Regex StripStringIdxRegex()
	{
		return new Regex("\"@\\d+", RegexOptions.Compiled);
	}

	private static void ImportCollisionEvent(GameMakerData data, string codeName, string gmlCode, CodeImportGroup importGroup, Dictionary<string, GameMakerCode> codeLookup, Dictionary<string, (GameMakerGameObject obj, int idx)> goLookup, Dictionary<string, string> collisionTargetsByCode)
	{
		if (!codeName.StartsWith("gml_Object_"))
		{
			return;
		}
		int collisionIdx = codeName.LastIndexOf("_Collision_");
		if (collisionIdx < 0)
		{
			return;
		}
		int length = "gml_Object_".Length;
		string objectName = codeName.Substring(length, collisionIdx - length);
		string identifier = codeName.Substring(collisionIdx + "_Collision_".Length);
		if (!goLookup.TryGetValue(objectName, out (GameMakerGameObject, int) objEntry))
		{
			return;
		}
		var (obj, _) = objEntry;
		uint collisionIndex;
		if (collisionTargetsByCode.TryGetValue(codeName, out string collisionObjectName))
		{
			if (!goLookup.TryGetValue(collisionObjectName, out (GameMakerGameObject, int) collisionEntry))
			{
				return;
			}
			collisionIndex = (uint)collisionEntry.Item2;
		}
		else if (!uint.TryParse(identifier, out collisionIndex))
		{
			if (!goLookup.TryGetValue(identifier, out (GameMakerGameObject, int) collisionEntry2))
			{
				return;
			}
			collisionIndex = (uint)collisionEntry2.Item2;
		}
		GameMakerPointerList<GameMakerGameObject.Event> collisionEvents = obj.Events[4];
		GameMakerCode codeEntry = null;
		foreach (GameMakerGameObject.Event evt in collisionEvents)
		{
			if (evt.EventSubtype != collisionIndex)
			{
				continue;
			}
			if (evt.Actions.Count > 0 && evt.Actions[0].CodeId != null)
			{
				codeEntry = evt.Actions[0].CodeId;
				break;
			}
			codeLookup.TryGetValue(codeName, out codeEntry);
			if (codeEntry == null)
			{
				codeEntry = GameMakerCode.CreateEmptyEntry(data, codeName);
			}
			if (evt.Actions.Count == 0)
			{
				evt.Actions.Add(new GameMakerGameObject.EventAction
				{
					CodeId = codeEntry
				});
			}
			else
			{
				evt.Actions[0].CodeId = codeEntry;
			}
			break;
		}
		if (codeEntry == null)
		{
			codeLookup.TryGetValue(codeName, out codeEntry);
			if (codeEntry == null)
			{
				codeEntry = GameMakerCode.CreateEmptyEntry(data, codeName);
			}
			GameMakerGameObject.Event newEvent = new GameMakerGameObject.Event
			{
				EventSubtype = collisionIndex
			};
			newEvent.Actions.Add(new GameMakerGameObject.EventAction
			{
				CodeId = codeEntry
			});
			collisionEvents.Add(newEvent);
		}
		importGroup.QueueReplace(codeEntry, gmlCode);
	}

	private static Dictionary<string, string> LoadCollisionTargetsByCode(string helpersDir, string? objectEventsContentOverride)
	{
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
		string objectEventsContent = objectEventsContentOverride;
		string objEventsPath = Path.Combine(helpersDir, "object_events.json");
		if (objectEventsContent == null && File.Exists(objEventsPath))
		{
			objectEventsContent = File.ReadAllText(objEventsPath, Encoding.UTF8);
		}
		if (objectEventsContent == null)
		{
			return result;
		}
		using JsonDocument doc = JsonDocument.Parse(objectEventsContent);
		foreach (JsonProperty item in doc.RootElement.EnumerateObject())
		{
			foreach (JsonElement evt in item.Value.EnumerateArray())
			{
				if (evt.TryGetProperty("t", out var typeElem) && typeElem.GetInt32() == 4 && evt.TryGetProperty("c", out var codeElem) && codeElem.ValueKind == JsonValueKind.String && evt.TryGetProperty("cn", out var collisionNameElem) && collisionNameElem.ValueKind == JsonValueKind.String)
				{
					string codeName = codeElem.GetString();
					string collisionName = collisionNameElem.GetString();
					if (!string.IsNullOrEmpty(codeName) && !string.IsNullOrEmpty(collisionName))
					{
						result[codeName] = collisionName;
					}
				}
			}
		}
		return result;
	}

	private static string FormatPatchStats(PatchStatistics s)
	{
		List<string> parts = new List<string>();
		if (s.TotalChanged > 0)
		{
			parts.Add((s.TotalChangedFiles > 0) ? $"Changed: {s.TotalChanged} ({s.TotalChangedFiles} files)" : $"Changed: {s.TotalChanged}");
		}
		if (s.TotalNew > 0)
		{
			parts.Add((s.TotalNewFiles > 0) ? $"New: {s.TotalNew} ({s.TotalNewFiles} files)" : $"New: {s.TotalNew}");
		}
		if (s.TotalDeleted > 0)
		{
			parts.Add($"Deleted: {s.TotalDeleted}");
		}
		if (parts.Count <= 0)
		{
			return "No changes";
		}
		return string.Join(", ", parts);
	}

	public static async Task<PatchValidateResult> ValidatePatchAsync(string patchPath, string? dataPath = null, G3MCacheOptions? cacheOptions = null)
	{
		if (!File.Exists(patchPath))
		{
			return new PatchValidateResult
			{
				Success = false,
				Error = "Patch file not found: " + patchPath
			};
		}
		try
		{
			using FileStream zipStream = new FileStream(patchPath, FileMode.Open, FileAccess.Read);
			using ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
			ZipArchiveEntry manifestEntry = archive.GetEntry("g3mpatch.json");
			if (manifestEntry == null)
			{
				return new PatchValidateResult
				{
					Success = false,
					Error = "Invalid patch: g3mpatch.json not found"
				};
			}
			using StreamReader reader = new StreamReader(await manifestEntry.OpenAsync());
			G3MPatchManifest manifest = JsonSerializer.Deserialize<G3MPatchManifest>(await reader.ReadToEndAsync());
			if (manifest == null)
			{
				return new PatchValidateResult
				{
					Success = false,
					Error = "Invalid patch: failed to parse manifest"
				};
			}
			if (dataPath != null && File.Exists(dataPath))
			{
				string text = G3MCacheService.TryReadDataFileInfo(dataPath, cacheOptions)?.Md5;
				if (text == null)
				{
					text = await HashService.ComputeFileHashAsync(dataPath);
				}
				string dataHash = text;
				if (manifest.Original?.Md5 != null && manifest.Original.Md5 != dataHash)
				{
					LogService.Warning(" Data file hash doesn't match original - patch may not apply correctly");
				}
			}
			return new PatchValidateResult
			{
				Success = true,
				Manifest = manifest
			};
		}
		catch (Exception ex)
		{
			return new PatchValidateResult
			{
				Success = false,
				Error = "Failed to read patch: " + ex.Message
			};
		}
	}

	public static async Task<string> EnsureG3MPatchAsync(string originalPath, string inputPath, string? tempDir = null, Dictionary<string, Dictionary<string, string>>? precomputedOriginalHashes = null, DataFileInfo? precomputedOriginalInfo = null, Dictionary<string, Dictionary<string, int>>? precomputedOriginalNameCounts = null, Dictionary<string, IReadOnlyList<string>>? precomputedOriginalOrderedNames = null, G3MCacheOptions? cacheOptions = null)
	{
		string ext = Path.GetExtension(inputPath).ToLowerInvariant();
		string text = ext;
		if ((text == ".g3mpatch" || text == ".zip") ? true : false)
		{
			return inputPath;
		}
		string dataFilePath;
		if (PatchInputService.IsXDelta(inputPath))
		{
			LogService.Warning("Input '" + Path.GetFileName(inputPath) + "' is xdelta, converting to .g3mpatch...");
			XDeltaService xDeltaService = new XDeltaService();
			dataFilePath = Path.Combine(tempDir ?? Path.GetTempPath(), $"g3mtool_conv_{Guid.NewGuid():N}.win");
			XDeltaResult xResult = await xDeltaService.ApplyPatchAsync(originalPath, inputPath, dataFilePath);
			if (!xResult.Success)
			{
				throw new InvalidOperationException("Failed to apply xdelta '" + Path.GetFileName(inputPath) + "': " + xResult.Error);
			}
		}
		else if (DataFileExtensionUtil.IsDataFile(inputPath))
		{
			LogService.Warning("Input '" + Path.GetFileName(inputPath) + "' is a data file, converting to .g3mpatch...");
			dataFilePath = inputPath;
		}
		else
		{
			if (!(ext == ".csx"))
			{
				throw new NotSupportedException("Unsupported patch input '" + Path.GetFileName(inputPath) + "'.");
			}
			throw new NotSupportedException("CSX inputs require a scripting host; materialize the data file before creating a patch.");
		}
		string outputZip = Path.Combine(tempDir ?? Path.GetTempPath(), $"g3mtool_conv_{Guid.NewGuid():N}.g3mpatch");
		PatchCreateResult result = await CreatePatchAsync(originalPath, dataFilePath, outputZip, precomputedOriginalHashes, precomputedOriginalInfo, precomputedOriginalNameCounts, precomputedOriginalOrderedNames, null, includeXdeltaFallback: false, cacheOptions);
		if (!result.Success)
		{
			throw new InvalidOperationException("Failed to create .g3mpatch from '" + Path.GetFileName(inputPath) + "': " + result.Error);
		}
		if (PatchInputService.IsXDelta(inputPath) && dataFilePath != inputPath)
		{
			try
			{
				File.Delete(dataFilePath);
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				LogService.Warning("Could not delete temporary data file '" + dataFilePath + "': " + ex.Message);
			}
		}
		return outputZip;
	}

	public static void LogCreateCompatibilityWarnings(DataFileInfo original, DataFileInfo modified)
	{
		if (original.BytecodeVersion != modified.BytecodeVersion)
		{
			LogService.Warning($"Data file bytecode version differs ({original.BytecodeVersion} vs {modified.BytecodeVersion}). " + "The patch will still be created.");
		}
		GeneralInfoData originalGeneralInfo = original.GeneralInfo;
		GeneralInfoData modifiedGeneralInfo = modified.GeneralInfo;
		if (originalGeneralInfo == null || modifiedGeneralInfo == null)
		{
			return;
		}
		if (!SameText(originalGeneralInfo.DisplayName, modifiedGeneralInfo.DisplayName))
		{
			LogService.Warning($"Data file display name differs ('{originalGeneralInfo.DisplayName}' vs '{modifiedGeneralInfo.DisplayName}'). " + "The patch will still be created.");
		}
		if (originalGeneralInfo.RoomOrderCount > 0 && modifiedGeneralInfo.RoomOrderCount > 0)
		{
			int smaller = Math.Min(originalGeneralInfo.RoomOrderCount, modifiedGeneralInfo.RoomOrderCount);
			if (Math.Max(originalGeneralInfo.RoomOrderCount, modifiedGeneralInfo.RoomOrderCount) > smaller * 3)
			{
				LogService.Warning($"Data file room order count differs substantially ({originalGeneralInfo.RoomOrderCount} vs {modifiedGeneralInfo.RoomOrderCount}). " + "The patch will still be created.");
			}
		}
	}

	private static bool SameText(string? left, string? right)
	{
		return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	private static HashSet<string> BuildRelevantAssetOrderSections(IEnumerable<string> resourceTypes, bool includeScriptsForCodeEntries)
	{
		HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string resourceType2 in resourceTypes)
		{
			switch (resourceType2)
			{
			case "Sounds":
				keep.Add("sounds");
				break;
			case "Sprites":
				keep.Add("sprites");
				break;
			case "Backgrounds":
			case "Tilesets":
				keep.Add("backgrounds");
				break;
			case "Paths":
				keep.Add("paths");
				break;
			case "Scripts":
				keep.Add("scripts");
				break;
			case "Fonts":
				keep.Add("fonts");
				break;
			case "GameObjects":
				keep.Add("objects");
				break;
			case "Timelines":
				keep.Add("timelines");
				break;
			case "Rooms":
				keep.Add("rooms");
				break;
			case "Shaders":
				keep.Add("shaders");
				break;
			case "Extensions":
				keep.Add("extensions");
				break;
			case "AudioGroups":
				keep.Add("audiogroups");
				break;
			case "CodeEntries":
				if (includeScriptsForCodeEntries)
				{
					keep.Add("scripts");
				}
				break;
			}
		}
		return keep;
	}

	private static bool RequiresHeavyFinalize(IEnumerable<string> resourceTypesToProcess)
	{
		bool sawAny = false;
		bool sawOnlyScriptMetadata = true;
		foreach (string resourceType in resourceTypesToProcess)
		{
			sawAny = true;
			switch (resourceType)
			{
			case "Options":
			case "Scripts":
			case "FilterEffects":
			case "GlobalScripts":
			case "GeneralInfo":
			case "Language":
			case "FeatureFlags":
			case "Tags":
				continue;
			}
			sawOnlyScriptMetadata = false;
			return true;
		}
		if (!sawAny)
		{
			return true;
		}
		return !sawOnlyScriptMetadata;
	}

	private static bool RequiresScriptEntryEnsure(IEnumerable<string> resourceTypesToProcess)
	{
		foreach (string resourceType in resourceTypesToProcess)
		{
			if (resourceType.Equals("Scripts", StringComparison.OrdinalIgnoreCase) || resourceType.Equals("GlobalScripts", StringComparison.OrdinalIgnoreCase) || resourceType.Equals("CodeEntries", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool RequiresObjectEventsHelper(IReadOnlyDictionary<string, HashSet<string>> changedNamesPerType)
	{
		if (!changedNamesPerType.TryGetValue("CodeEntries", out HashSet<string> codeEntries))
		{
			return false;
		}
		foreach (string codeName in codeEntries)
		{
			if (codeName.StartsWith("gml_Object_", StringComparison.Ordinal) || codeName.Contains("_Collision_", StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static bool ShouldApplyAuthoritativeObjectEvents(List<string> resourceTypesToProcess, G3MPatchManifest? manifest, byte[] objectEventsBytes)
	{
		if (resourceTypesToProcess.Contains<string>("GameObjects", StringComparer.OrdinalIgnoreCase))
		{
			return true;
		}
		if (manifest?.Resources == null || !manifest.Resources.TryGetValue("CodeEntries", out ResourceTypeChanges codeChanges))
		{
			return false;
		}
		HashSet<string> changedCodeNames = GetChangedCodeEntryNames(codeChanges);
		if (changedCodeNames.Count == 0)
		{
			return true;
		}
		string objectEventsText = Encoding.UTF8.GetString(objectEventsBytes);
		foreach (string codeName in changedCodeNames)
		{
			if (objectEventsText.Contains(codeName, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static HashSet<string> GetChangedCodeEntryNames(G3MPatchManifest? manifest)
	{
		if (manifest?.Resources == null || !manifest.Resources.TryGetValue("CodeEntries", out ResourceTypeChanges codeChanges))
		{
			return new HashSet<string>();
		}
		return GetChangedCodeEntryNames(codeChanges);
	}

	private static HashSet<string> GetChangedCodeEntryNames(ResourceTypeChanges codeChanges)
	{
		HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
		if (codeChanges.Changed != null)
		{
			foreach (ResourceChange change in codeChanges.Changed)
			{
				if (!string.IsNullOrWhiteSpace(change?.Name))
				{
					names.Add(change.Name);
				}
			}
		}
		if (codeChanges.New != null)
		{
			foreach (ResourceChange change2 in codeChanges.New)
			{
				if (!string.IsNullOrWhiteSpace(change2?.Name))
				{
					names.Add(change2.Name);
				}
			}
		}
		return names;
	}

	private static bool RequiresVariableFunctionsHelper(IEnumerable<string> changedResourceTypes)
	{
		foreach (string changedResourceType in changedResourceTypes)
		{
			if (changedResourceType.Equals("CodeEntries", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool RequiresTextureMappingHelpers(IEnumerable<string> changedResourceTypes)
	{
		foreach (string resourceType in changedResourceTypes)
		{
			if (resourceType.Equals("Sprites", StringComparison.OrdinalIgnoreCase) || resourceType.Equals("Backgrounds", StringComparison.OrdinalIgnoreCase) || resourceType.Equals("Fonts", StringComparison.OrdinalIgnoreCase) || resourceType.Equals("Tilesets", StringComparison.OrdinalIgnoreCase) || resourceType.Equals("EmbeddedImages", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsDirectApplyFriendlyResourceType(string resourceType)
	{
		switch (resourceType)
		{
		case "AudioGroups":
		case "GeneralInfo":
		case "Options":
		case "Shaders":
		case "EmbeddedAudio":
		case "FilterEffects":
		case "Language":
		case "FeatureFlags":
		case "Tags":
		case "TextureGroupInfo":
		case "EmbeddedImages":
		case "AnimationCurves":
		case "Extensions":
			return true;
		default:
			return false;
		}
	}

	private static bool CanUseMinimalStandardApply(PatchApplyPlan applyPlan, IReadOnlyCollection<string> resourceTypesToProcess)
	{
		if (applyPlan.SupportsDirectResourceApply && !applyPlan.RequiresCodePipeline && !applyPlan.RequiresTexturePipeline && !applyPlan.RequiresAssetReorder)
		{
			return resourceTypesToProcess.All(IsDirectApplyFriendlyResourceType);
		}
		return false;
	}

	internal static PatchApplyPlan BuildPatchApplyPlan(IReadOnlyDictionary<string, ResourceTypeChanges> resources)
	{
		string[] resourceTypes = resources.Keys.ToArray();
		bool requiresCodePipeline = resourceTypes.Contains<string>("CodeEntries", StringComparer.OrdinalIgnoreCase);
		bool requiresTexturePipeline = RequiresTextureMappingHelpers(resourceTypes) || resourceTypes.Contains<string>("EmbeddedTextures", StringComparer.OrdinalIgnoreCase) || resourceTypes.Contains<string>("TexturePageItems", StringComparer.OrdinalIgnoreCase);
		bool requiresAssetReorder = RequiresFinalAssetReorder(resourceTypes);
		bool requiresHeavyFinalize = RequiresHeavyFinalize(resourceTypes);
		List<string> simpleTypes = resourceTypes.Where(IsDirectApplyFriendlyResourceType).OrderBy<string, string>((string x) => x, StringComparer.OrdinalIgnoreCase).ToList();
		List<string> heavyTypes = resourceTypes.Where((string x) => !IsDirectApplyFriendlyResourceType(x)).OrderBy<string, string>((string x) => x, StringComparer.OrdinalIgnoreCase).ToList();
		bool supportsDirectResourceApply = heavyTypes.Count == 0 && resources.Values.All((ResourceTypeChanges changes) => (changes.Deleted?.Count ?? 0) == 0);
		return new PatchApplyPlan
		{
			Mode = (supportsDirectResourceApply ? "direct" : "standard"),
			RequiresCodePipeline = requiresCodePipeline,
			RequiresTexturePipeline = requiresTexturePipeline,
			RequiresAssetReorder = requiresAssetReorder,
			RequiresHeavyFinalize = requiresHeavyFinalize,
			SupportsDirectResourceApply = supportsDirectResourceApply,
			SimpleResourceTypes = simpleTypes,
			HeavyResourceTypes = heavyTypes
		};
	}

	private static bool RequiresPatchHelpers(IEnumerable<string> changedResourceTypes, IEnumerable<string>? helperForcedResourceTypes = null)
	{
		if (helperForcedResourceTypes != null)
		{
			foreach (string helperForcedResourceType in helperForcedResourceTypes)
			{
				if (!string.IsNullOrWhiteSpace(helperForcedResourceType))
				{
					return true;
				}
			}
		}
		bool sawAny = false;
		foreach (string changedResourceType in changedResourceTypes)
		{
			sawAny = true;
			if (!IsHelperFreeResourceType(changedResourceType))
			{
				return true;
			}
		}
		if (!sawAny)
		{
			return true;
		}
		return false;
	}

	private static bool IsHelperFreeResourceType(string resourceType)
	{
		if (!resourceType.Equals("GameObjects", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("Fonts", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("Sounds", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("Paths", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("Rooms", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("Options", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("FeatureFlags", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("Tags", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("Language", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("GeneralInfo", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("FilterEffects", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("EmbeddedImages", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("AnimationCurves", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("TextureGroupInfo", StringComparison.OrdinalIgnoreCase) && !resourceType.Equals("Shaders", StringComparison.OrdinalIgnoreCase))
		{
			return resourceType.Equals("Extensions", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static HashSet<string> GetHelperRelevantResourceTypes(Dictionary<string, Dictionary<string, string>> originalHashes, Dictionary<string, Dictionary<string, string>> modifiedHashes, Dictionary<string, HashSet<string>> changedNamesPerType)
	{
		HashSet<string> resourceTypes = new HashSet<string>(changedNamesPerType.Keys, StringComparer.OrdinalIgnoreCase);
		string[] allTypes = ResourceTypeRegistry.AllTypes;
		foreach (string resourceType in allTypes)
		{
			Dictionary<string, string>? obj = originalHashes.GetValueOrDefault(resourceType) ?? new Dictionary<string, string>();
			Dictionary<string, string> modTypeHashes = modifiedHashes.GetValueOrDefault(resourceType) ?? new Dictionary<string, string>();
			foreach (string name in obj.Keys)
			{
				if (!modTypeHashes.ContainsKey(name))
				{
					resourceTypes.Add(resourceType);
					break;
				}
			}
		}
		return resourceTypes;
	}

	private static bool RequiresDuplicateGameObjectRepair(IEnumerable<string> changedResourceTypes, G3MPatchManifest? manifest)
	{
		bool hasGameObjects = false;
		foreach (string changedResourceType in changedResourceTypes)
		{
			if (changedResourceType.Equals("GameObjects", StringComparison.OrdinalIgnoreCase))
			{
				hasGameObjects = true;
				break;
			}
		}
		if (!hasGameObjects)
		{
			return false;
		}
		ResourceTypeChanges changes = manifest?.Resources?.GetValueOrDefault("GameObjects");
		if (changes == null)
		{
			return true;
		}
		List<string> names = new List<string>();
		if (changes.Changed != null)
		{
			names.AddRange(from c in changes.Changed
				select c.Name into n
				where !string.IsNullOrWhiteSpace(n)
				select n);
		}
		if (changes.New != null)
		{
			names.AddRange(from c in changes.New
				select c.Name into n
				where !string.IsNullOrWhiteSpace(n)
				select n);
		}
		if (names.Count == 0)
		{
			return true;
		}
		Dictionary<string, int> baseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (string item in names)
		{
			string baseName = StripIdxSuffix(item);
			baseCounts[baseName] = baseCounts.GetValueOrDefault(baseName) + 1;
			if (baseCounts[baseName] > 1)
			{
				return true;
			}
		}
		return false;
	}

	private static bool RequiresFinalAssetReorder(IEnumerable<string> resourceTypesToProcess)
	{
		bool sawAny = false;
		foreach (string resourceType in resourceTypesToProcess)
		{
			sawAny = true;
			switch (resourceType)
			{
			case "GeneralInfo":
			case "CodeEntries":
			case "FilterEffects":
			case "EmbeddedAudio":
			case "Options":
			case "Language":
			case "FeatureFlags":
			case "Tags":
			case "TextureGroupInfo":
			case "EmbeddedImages":
				continue;
			}
			return true;
		}
		if (!sawAny)
		{
			return true;
		}
		return false;
	}

	private static bool RequiresCodeRemapPreparation(IEnumerable<string> resourceTypesToProcess)
	{
		foreach (string item in resourceTypesToProcess)
		{
			switch (item)
			{
			case "Shaders":
			case "Sprites":
			case "Backgrounds":
			case "GameObjects":
			case "Fonts":
			case "Paths":
			case "Rooms":
			case "Tilesets":
			case "Sounds":
			case "Timelines":
				return true;
			}
		}
		return false;
	}

	private static bool AssetOrderAlreadyMatches(GameMakerData data, string[] filteredAssetOrderLines)
	{
		foreach (var (section, targetNames) in ParseAssetOrderSections(filteredAssetOrderLines))
		{
			if (section.Equals("counts", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			List<string> currentNames = GetCurrentAssetOrderSection(data, section);
			if (currentNames == null || currentNames.Count != targetNames.Count)
			{
				return false;
			}
			for (int i = 0; i < targetNames.Count; i++)
			{
				if (!string.Equals(currentNames[i], targetNames[i], StringComparison.Ordinal))
				{
					return false;
				}
			}
		}
		return true;
	}

	private static Dictionary<string, List<string>> ParseAssetOrderSections(string[] lines)
	{
		Dictionary<string, List<string>> sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		string currentSection = null;
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].TrimEnd();
			if (line.StartsWith("@@") && line.EndsWith("@@") && line.Length > 4)
			{
				string text = line;
				currentSection = text.Substring(2, text.Length - 2 - 2);
				sections[currentSection] = new List<string>();
			}
			else if (currentSection != null && !currentSection.Equals("counts", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(line))
			{
				sections[currentSection].Add(line);
			}
		}
		return sections;
	}

	private static List<string>? GetCurrentAssetOrderSection(GameMakerData data, string section)
	{
		return section.ToLowerInvariant() switch
		{
			"sounds" => data.Sounds.Select((GameMakerSound s) => s?.Name?.Content ?? "").ToList(),
			"sprites" => data.Sprites.Select((GameMakerSprite s) => s?.Name?.Content ?? "").ToList(),
			"backgrounds" => data.Backgrounds.Select((GameMakerBackground b) => b?.Name?.Content ?? "").ToList(),
			"paths" => data.Paths.Select((GameMakerPath p) => p?.Name?.Content ?? "").ToList(),
			"scripts" => data.Scripts.Select((GameMakerScript s) => s?.Name?.Content ?? "").ToList(),
			"fonts" => data.Fonts.Select((GameMakerFont f) => f?.Name?.Content ?? "").ToList(),
			"objects" => data.GameObjects.Select((GameMakerGameObject o) => o?.Name?.Content ?? "").ToList(),
			"timelines" => data.Timelines.Select((GameMakerTimeline t) => t?.Name?.Content ?? "").ToList(),
			"rooms" => data.Rooms.Select((GameMakerRoom r) => r?.Name?.Content ?? "").ToList(),
			"shaders" => data.Shaders.Select((GameMakerShader s) => s?.Name?.Content ?? "").ToList(),
			"extensions" => data.Extensions.Select((GameMakerExtension e) => e?.Name?.Content ?? "").ToList(),
			"audiogroups" => data.AudioGroups.Select((GameMakerAudioGroup a) => a?.Name?.Content ?? "").ToList(),
			_ => null,
		};
	}

	private static string[] SelectAssetOrderSections(string[] lines, HashSet<string> keepSections, params string[] skipSections)
	{
		HashSet<string> skip = new HashSet<string>(skipSections, StringComparer.OrdinalIgnoreCase);
		List<string> result = new List<string>(lines.Length);
		bool inSelected = false;
		bool inCounts = false;
		foreach (string line in lines)
		{
			if (line.StartsWith("@@") && line.EndsWith("@@") && line.Length > 4)
			{
				string text = line;
				string sect = text.Substring(2, text.Length - 2 - 2);
				inCounts = sect.Equals("counts", StringComparison.OrdinalIgnoreCase);
				inSelected = !skip.Contains(sect) && (inCounts || keepSections.Contains(sect));
				if (inSelected)
				{
					result.Add(line);
				}
			}
			else
			{
				if (!inSelected)
				{
					continue;
				}
				if (inCounts)
				{
					int eq = line.IndexOf('=');
					if (eq > 0)
					{
						string countName = line.Substring(0, eq);
						if (skip.Contains(countName) || (!keepSections.Contains(countName) && !countName.Equals("EmbeddedTextures", StringComparison.OrdinalIgnoreCase) && !countName.Equals("TexturePageItems", StringComparison.OrdinalIgnoreCase)))
						{
							continue;
						}
					}
				}
				result.Add(line);
			}
		}
		return result.ToArray();
	}

	private static void CopyEmbeddedTexturesForAssetOrder(PatchFileSystem pfs, string assetOrderDir)
	{
		string embeddedDir = Path.Combine(assetOrderDir, "EmbeddedTextures");
		foreach (string path in pfs.GetAllFilePaths())
		{
			string sourcePrefix = null;
			if (path.StartsWith("EmbeddedTextures/", StringComparison.OrdinalIgnoreCase))
			{
				sourcePrefix = "EmbeddedTextures/";
			}
			else if (path.StartsWith(pfs.HelpersPrefix + "/EmbeddedTextures/", StringComparison.OrdinalIgnoreCase))
			{
				sourcePrefix = pfs.HelpersPrefix + "/EmbeddedTextures/";
			}
			if (sourcePrefix == null)
			{
				continue;
			}
			string relative = path.Substring(sourcePrefix.Length).Replace('/', Path.DirectorySeparatorChar);
			if (!string.IsNullOrWhiteSpace(relative))
			{
				string path2 = Path.Combine(embeddedDir, relative);
				string outputDir = Path.GetDirectoryName(path2);
				if (!string.IsNullOrEmpty(outputDir))
				{
					Directory.CreateDirectory(outputDir);
				}
				File.WriteAllBytes(path2, pfs.ReadAllBytes(path));
			}
		}
	}

	private static string? StageEmbeddedTexturesForAssetOrder(GameMakerData data, PatchFileSystem pfs)
	{
		bool hasEmbeddedTextures = false;
		foreach (string path in pfs.GetAllFilePaths())
		{
			if (path.StartsWith("EmbeddedTextures/", StringComparison.OrdinalIgnoreCase) || path.StartsWith(pfs.HelpersPrefix + "/EmbeddedTextures/", StringComparison.OrdinalIgnoreCase))
			{
				hasEmbeddedTextures = true;
				break;
			}
		}
		if (!hasEmbeddedTextures)
		{
			return null;
		}
		string stageDir = Path.Combine(Path.GetTempPath(), $"g3mtool_embedded_stage_{Guid.NewGuid():N}");
		Directory.CreateDirectory(stageDir);
		CopyEmbeddedTexturesForAssetOrder(pfs, stageDir);
		ExportMissingEmbeddedTexturesForAssetOrder(data, pfs, stageDir);
		return stageDir;
	}

	private static void ExportMissingEmbeddedTexturesForAssetOrder(GameMakerData data, PatchFileSystem pfs, string stageDir)
	{
		string embeddedDir = Path.Combine(stageDir, "EmbeddedTextures");
		Directory.CreateDirectory(embeddedDir);
		HashSet<int> touchedIndices = GetTouchedEmbeddedTextureIndices(pfs);
		bool gm2022_5 = data.IsVersionAtLeast(2022u, 5u);
		for (int i = 0; i < data.EmbeddedTextures.Count; i++)
		{
			if (touchedIndices.Contains(i))
			{
				continue;
			}
			GameMakerEmbeddedTexture texture = data.EmbeddedTextures[i];
			if (texture == null)
			{
				continue;
			}
			GMImage image = texture.TextureData?.Image;
			if (image != null)
			{
				string textureName = $"texture_{i:D4}";
				string textureDir = Path.Combine(embeddedDir, textureName);
				string jsonPath = Path.Combine(textureDir, textureName + ".json");
				string binPath = Path.Combine(textureDir, textureName + ".bin");
				if (!File.Exists(jsonPath) && !File.Exists(binPath))
				{
					Directory.CreateDirectory(textureDir);
					File.WriteAllBytes(binPath, image.ToSpan(gm2022_5).ToArray());
					Dictionary<string, object> meta = new Dictionary<string, object>
					{
						["index"] = i,
						["name"] = texture.Name?.Content ?? "",
						["scaled"] = texture.Scaled,
						["generatedMips"] = texture.GeneratedMips,
						["format"] = image.Format.ToString()
					};
					File.WriteAllText(jsonPath, JsonSerializer.Serialize(meta, s_jsonOptions), Encoding.UTF8);
				}
			}
		}
	}

	private static void CopyStagedEmbeddedTexturesForAssetOrder(string stagedDir, string assetOrderDir)
	{
		string sourceRoot = Path.Combine(stagedDir, "EmbeddedTextures");
		if (!Directory.Exists(sourceRoot))
		{
			return;
		}
		string targetRoot = Path.Combine(assetOrderDir, "EmbeddedTextures");
		string[] files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
		foreach (string file in files)
		{
			string relative = Path.GetRelativePath(sourceRoot, file);
			string outputPath = Path.Combine(targetRoot, relative);
			string outputDir = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrEmpty(outputDir))
			{
				Directory.CreateDirectory(outputDir);
			}
			File.Copy(file, outputPath, overwrite: true);
		}
	}
}
