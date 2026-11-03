using System.Globalization;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;

namespace G3MLib.Modding.Patching;

public partial class PatchService
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

		public Dictionary<string, List<string>> AssetNamesAtCodeImportStart { get; set; } = [];

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
				var byExplicit = FindCodeByName(data, explicitCodeName);
				if (byExplicit != null)
				{
					return byExplicit;
				}
			}
			if (string.IsNullOrWhiteSpace(scriptName))
			{
				return null;
			}
			var byExact = FindCodeByName(data, scriptName);
			if (byExact != null)
			{
				return byExact;
			}
			var alreadyScriptPrefixed = scriptName.StartsWith("gml_Script_", StringComparison.Ordinal);
			var alreadyGlobalPrefixed = scriptName.StartsWith("gml_GlobalScript_", StringComparison.Ordinal);
			if (scriptName.StartsWith("gml_Script_gml_GlobalScript_", StringComparison.Ordinal))
			{
				var byCanonicalScript = FindCodeByName(data, "gml_Script_" + scriptName["gml_Script_gml_GlobalScript_".Length..]);
				if (byCanonicalScript != null)
				{
					return byCanonicalScript;
				}
			}
			if (!alreadyScriptPrefixed)
			{
				var byScriptPrefix = FindCodeByName(data, "gml_Script_" + scriptName);
				if (byScriptPrefix != null)
				{
					return byScriptPrefix;
				}
			}
			if (!alreadyGlobalPrefixed)
			{
				var byGlobalPrefix = FindCodeByName(data, "gml_GlobalScript_" + scriptName);
				if (byGlobalPrefix != null)
				{
					return byGlobalPrefix;
				}
			}
			if (alreadyScriptPrefixed)
			{
				return FindCodeByName(data, "gml_GlobalScript_" + scriptName["gml_Script_".Length..]);
			}
			if (alreadyGlobalPrefixed)
			{
				return FindCodeByName(data, "gml_Script_" + scriptName["gml_GlobalScript_".Length..]);
			}
			return null;
		}

		private static GameMakerCode? FindCodeByName(GameMakerData data, string? codeName)
		{
			if (string.IsNullOrWhiteSpace(codeName) || data.Code == null)
			{
				return null;
			}
			var topLevel = data.Code.ByName(codeName);
			if (topLevel != null)
			{
				return topLevel;
			}
			foreach (var item in data.Code)
			{
				var found = FindCodeByNameRecursive(item, codeName);
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
			foreach (var childEntry in code.ChildEntries)
			{
				var found = FindCodeByNameRecursive(childEntry, codeName);
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
		public static readonly ReferenceEqualityComparer Instance = new();

		public new bool Equals(object? x, object? y)
		{
			return x == y;
		}

		public int GetHashCode(object obj)
		{
			return RuntimeHelpers.GetHashCode(obj);
		}
	}

	private static readonly JsonSerializerOptions s_jsonOptions = new()
	{
		WriteIndented = true
	};

	private static readonly Lock s_codeImportLock = new();

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
			using var stream = OpenDataReadStream(path);
			using var savedData = GameMakerIO.Read(stream);
			var integrity = DataIntegrityService.ValidateOnly(savedData);
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
		var importOrder = ResourceTypeRegistry.ImportOrder;
		var existingFolders = pfs.GetResourceTypes();
		var resourceTypesToProcess = new List<string>(importOrder.Length);
		var array = importOrder;
		foreach (var rt in array)
		{
			if (existingFolders.Contains(rt))
			{
				resourceTypesToProcess.Add(rt);
			}
		}
		foreach (var rt2 in manifest?.Resources?.Where(kvp => (kvp.Value.Deleted?.Count ?? 0) > 0).Select(kvp => kvp.Key) ?? [])
		{
			if (!resourceTypesToProcess.Contains(rt2, StringComparer.OrdinalIgnoreCase))
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
		var assetOrderPath = Path.Combine(pfs.HelpersPrefix, "asset_order.txt");
		if (pfs.FileExists(assetOrderPath))
		{
			return pfs.ReadAllLines(assetOrderPath).Any(line => string.Equals(line, "@@scripts@@", StringComparison.OrdinalIgnoreCase));
		}
		return false;
	}

	private static (Dictionary<string, int> Weights, int TotalNonCodeWeight, bool HasCodeEntries, int NonCodeRangeStart, int NonCodeRange) CreateImportProgressState(IReadOnlyList<string> resourceTypes)
	{
		var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
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
		var totalNonCodeWeight = resourceTypes.Where(resourceType => resourceType != "CodeEntries").Sum(resourceType => weights.GetValueOrDefault(resourceType, 1));
		var hasCodeEntries = resourceTypes.Contains("CodeEntries");
		var nonCodeRangeEnd = hasCodeEntries ? 40 : 90;
		return (Weights: weights, TotalNonCodeWeight: totalNonCodeWeight, HasCodeEntries: hasCodeEntries, NonCodeRangeStart: 5, NonCodeRange: nonCodeRangeEnd - 5);
	}

	private static PatchHelperSnapshot CapturePatchHelpers(PatchFileSystem pfs, bool includeAssetOrder, bool includeObjectEvents, bool includeTextureMappingHelpers)
	{
		var helpersDir = pfs.HelpersPrefix;
		var capturedAssetOrderPath = Path.Combine(helpersDir, "asset_order.txt");
		var capturedObjectEventsPath = Path.Combine(helpersDir, "object_events.json");
		var capturedTexturePageItemsPath = Path.Combine(helpersDir, "texture_page_items.json");
		var capturedSpriteFrameMapPath = Path.Combine(helpersDir, "sprite_frame_map.json");
		return new PatchHelperSnapshot
		{
			HelpersDir = helpersDir,
			AssetOrderLines = (includeAssetOrder && pfs.FileExists(capturedAssetOrderPath)) ? pfs.ReadAllLines(capturedAssetOrderPath) : null,
			ObjectEventsBytes = (includeObjectEvents && pfs.FileExists(capturedObjectEventsPath)) ? pfs.ReadAllBytes(capturedObjectEventsPath) : null,
			TexturePageItemsBytes = (includeTextureMappingHelpers && pfs.FileExists(capturedTexturePageItemsPath)) ? pfs.ReadAllBytes(capturedTexturePageItemsPath) : null,
			SpriteFrameMapBytes = (includeTextureMappingHelpers && pfs.FileExists(capturedSpriteFrameMapPath)) ? pfs.ReadAllBytes(capturedSpriteFrameMapPath) : null
		};
	}

	private static PatchApplyPreparation PreparePatchApply(GameMakerData data, PatchFileSystem pfs, IReadOnlyList<string> resourceTypes, PatchApplyPlan applyPlan, bool hasCodeEntries, bool scriptOrderOnly)
	{
		var requiresFinalAssetReorder = applyPlan.RequiresAssetReorder || scriptOrderOnly;
		var touchesTextureWorld = RequiresTextureMappingHelpers(resourceTypes) || resourceTypes.Contains("EmbeddedTextures", StringComparer.OrdinalIgnoreCase) || resourceTypes.Contains("TexturePageItems", StringComparer.OrdinalIgnoreCase);
		var needsCodeRemapPreparation = hasCodeEntries && RequiresCodeRemapPreparation(resourceTypes);
		var needsTexturePreservation = requiresFinalAssetReorder && touchesTextureWorld;
		var helpers = CapturePatchHelpers(pfs, hasCodeEntries || requiresFinalAssetReorder, hasCodeEntries || requiresFinalAssetReorder, needsTexturePreservation);
		var variablesFunctionsPath = Path.Combine(helpers.HelpersDir, "variables_functions.json");
		var variablesFunctionsContent = (hasCodeEntries && pfs.FileExists(variablesFunctionsPath)) ? pfs.ReadAllText(variablesFunctionsPath) : null;
		return new PatchApplyPreparation
		{
			NeedsFinalAssetReorder = requiresFinalAssetReorder,
			NeedsHeavyFinalize = applyPlan.RequiresHeavyFinalize,
			NeedsCodeRemapPreparation = needsCodeRemapPreparation,
			Helpers = helpers,
			VariablesFunctionsContent = variablesFunctionsContent,
			OriginalGmlForCodeRemap = needsCodeRemapPreparation ? CaptureOriginalCodeGmlForAssetRemap(data, resourceTypes) : [],
			PatchCodeEntryNames = needsCodeRemapPreparation ? CapturePatchCodeEntryNames(pfs) : [],
			UntouchedEmbeddedTextureSnapshot = needsTexturePreservation ? CaptureUntouchedEmbeddedTextures(data, pfs) : [],
			StagedEmbeddedTexturesDir = needsTexturePreservation ? StageEmbeddedTexturesForAssetOrder(data, pfs) : null,
			CodeMetadataForFinalCleanup = variablesFunctionsContent
		};
	}

	private static (int AppliedCount, int FailedCount) ImportMinimalResources(GameMakerData data, PatchFileSystem pfs, List<string> resourceTypes, string? dataPath, string? outputPath, IProgress<BatchProgress>? progress = null)
	{
		var appliedCount = 0;
		var failedCount = 0;
		var progressIndex = 0;
		var progressTotal = resourceTypes.Count;
		foreach (var resourceType in resourceTypes)
		{
			if (!pfs.DirectoryExists(resourceType))
			{
				progressIndex++;
				progress?.Report(new BatchProgress(progressIndex, progressTotal, resourceType));
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
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				LogService.Warning("ERROR applying " + resourceType + ": " + ex.Message);
				failedCount++;
			}
			finally
			{
				ResourceImportService.SetDataPaths(null, null);
			}
			progressIndex++;
			progress?.Report(new BatchProgress(progressIndex, progressTotal, resourceType));
		}
		return (appliedCount, failedCount);
	}

	private static (int AppliedCount, int FailedCount) ImportPatchResources(GameMakerData data, PatchFileSystem pfs, G3MPatchManifest? manifest, List<string> resourceTypes, PatchApplyPreparation preparation, bool hasCodeEntries, string? dataPath, string? outputPath, Stopwatch nonCodeImportSw, Stopwatch codeImportSw, Stopwatch phaseSw, Stopwatch preCodeReorderSw, Stopwatch postSpriteReorderSw, bool logDetailedCodeImport, IProgress<BatchProgress>? progress = null)
	{
		(var Weights, var TotalNonCodeWeight, _, var NonCodeRangeStart, var NonCodeRange) = CreateImportProgressState(resourceTypes);
		var resourceWeights = Weights;
		var totalNonCodeWeight = TotalNonCodeWeight;
		var nonCodeRangeStart = NonCodeRangeStart;
		var nonCodeRange = NonCodeRange;
		var nonCodeProgress = 0;
		var appliedCount = 0;
		var failedCount = 0;
		var progressIndex = 0;
		var progressTotal = resourceTypes.Count;
		nonCodeImportSw.Start();
		foreach (var resourceType in resourceTypes)
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
					var text = preparation.CodeMetadataForFinalCleanup = codeImportResult.Item3;
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
					var handler = new LogInterpolatedStringHandler(46, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler.AppendLiteral("[PatchService] Released PFS file data, RAM: ");
						handler.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
						handler.AppendLiteral("MB");
					}
					LogService.Log(ref handler);
					var handler2 = new LogInterpolatedStringHandler(39, 2, out shouldAppend);
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
				progressIndex++;
				progress?.Report(new BatchProgress(progressIndex, progressTotal, resourceType));
				nonCodeImportSw.Start();
				continue;
			}
			if (hasCodeEntries && resourceType == "Scripts")
			{
				LogService.Log("[PatchService] Deferring Scripts import until after CodeEntries...");
				nonCodeProgress += resourceWeights.GetValueOrDefault(resourceType, 1);
				LogService.Progress(nonCodeRangeStart + nonCodeRange * nonCodeProgress / Math.Max(totalNonCodeWeight, 1), 100);
				progressIndex++;
				progress?.Report(new BatchProgress(progressIndex, progressTotal, resourceType));
				continue;
			}
			if (!pfs.DirectoryExists(resourceType))
			{
				nonCodeProgress += resourceWeights.GetValueOrDefault(resourceType, 1);
				LogService.Progress(nonCodeRangeStart + nonCodeRange * nonCodeProgress / Math.Max(totalNonCodeWeight, 1), 100);
				progressIndex++;
				progress?.Report(new BatchProgress(progressIndex, progressTotal, resourceType));
				continue;
			}
			var handler3 = new LogInterpolatedStringHandler(27, 1, out shouldAppend);
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
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
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
				var handler4 = new LogInterpolatedStringHandler(28, 3, out shouldAppend);
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
			progressIndex++;
			progress?.Report(new BatchProgress(progressIndex, progressTotal, resourceType));
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
		string? reorderDir = null;
		try
		{
			if (helpers.AssetOrderLines == null)
			{
				return false;
			}
			reorderDir = Path.Combine(TempRoot.Path, $"g3mlib_final_reorder_{Guid.NewGuid():N}");
			Directory.CreateDirectory(reorderDir);
			var sections = BuildRelevantAssetOrderSections(resourceTypes, includeScriptsForCodeEntries: true);
			sections.Add("counts");
			var filteredLines = SelectAssetOrderSections(helpers.AssetOrderLines, sections, "TexturePageItems", "EmbeddedTextures");
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
			var handler = new LogInterpolatedStringHandler(44, 1, out var shouldAppend);
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
		var assetOrderPath = Path.Combine(helpersDir, "asset_order.txt");
		if (!pfs.FileExists(assetOrderPath))
		{
			return;
		}
		var reorderDir = Path.Combine(TempRoot.Path, $"g3mlib_reorder_{Guid.NewGuid():N}");
		Directory.CreateDirectory(reorderDir);
		var filteredLines = SelectAssetOrderSections(pfs.ReadAllLines(assetOrderPath), BuildRelevantAssetOrderSections(resourceTypes, includeScriptsForCodeEntries: false), "TexturePageItems", "EmbeddedTextures");
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
			var handler = new LogInterpolatedStringHandler(44, 1, out var shouldAppend);
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
		var assetOrderPath = Path.Combine(helpersDir, "asset_order.txt");
		if (!pfs.FileExists(assetOrderPath))
		{
			return false;
		}
		var orderLines = capturedAssetOrderLines ?? pfs.ReadAllLines(assetOrderPath);
		Dictionary<string, int> targetObjectCounts = [];
		var inObjects = false;
		var array = orderLines;
		int i;
		for (i = 0; i < array.Length; i++)
		{
			var line = array[i];
			if (line.StartsWith("@@", StringComparison.Ordinal))
			{
				inObjects = line == "@@objects@@";
			}
			else if (inObjects && !string.IsNullOrWhiteSpace(line))
			{
				var name = line.Trim();
				if (!(name == "(null)") && !int.TryParse(name, out var _))
				{
					targetObjectCounts[name] = targetObjectCounts.GetValueOrDefault(name) + 1;
				}
			}
		}
		Dictionary<string, int> existingObjectCounts = [];
		foreach (var gameObject in data.GameObjects)
		{
			var name2 = gameObject?.Name?.Content;
			if (name2 != null)
			{
				existingObjectCounts[name2] = existingObjectCounts.GetValueOrDefault(name2) + 1;
			}
		}
		var createdObjects = 0;
		foreach (var item in targetObjectCounts)
		{
			item.Deconstruct(out var key, out i);
			var name3 = key;
			var targetCount = i;
			for (var count = existingObjectCounts.GetValueOrDefault(name3); count < targetCount; count++)
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
			var handler = new LogInterpolatedStringHandler(61, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Created ");
				handler.AppendFormatted(createdObjects);
				handler.AppendLiteral(" missing GameObjects from TARGET order");
			}
			LogService.Log(ref handler);
		}
		var reorderDir = Path.Combine(TempRoot.Path, $"g3mlib_aoreorder_{Guid.NewGuid():N}");
		try
		{
			LogService.Log("[PatchService] Reordering assets to match TARGET order...");
			reorderStopwatch.Start();
			Directory.CreateDirectory(reorderDir);
			var sections = BuildRelevantAssetOrderSections(resourceTypes, includeScriptsForCodeEntries: true);
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
			var handler2 = new LogInterpolatedStringHandler(38, 1, out var shouldAppend2);
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
		var codeMetadata = ReadCodeMetadataContent(pfs, helpersDir);
		if (codeMetadata != null && pfs.FileExists(Path.Combine(helpersDir, "variables_functions.json")))
		{
			codeMetadata = capturedVariablesFunctionsContent ?? codeMetadata;
		}
		string? objectEvents = null;
		var objectEventsPath = Path.Combine(helpersDir, "object_events.json");
		if (pfs.FileExists(objectEventsPath))
		{
			objectEvents = pfs.ReadAllText(objectEventsPath);
		}
		string? globalScripts = null;
		if (pfs.FileExists("GlobalScripts/global_scripts.json"))
		{
			globalScripts = pfs.ReadAllText("GlobalScripts/global_scripts.json");
		}
		var hasScripts = pfs.DirectoryExists("Scripts");
		try
		{
			var handler = new LogInterpolatedStringHandler(49, 2, out var shouldAppend);
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

	public static Task<PatchCreateResult> CreatePatchAsync(string originalPath, string modifiedPath, string outputPath, Dictionary<string, Dictionary<string, string>>? precomputedOriginalHashes, DataFileInfo? precomputedOriginalInfo, Dictionary<string, Dictionary<string, int>>? precomputedOriginalNameCounts, Dictionary<string, IReadOnlyList<string>>? precomputedOriginalOrderedNames, GameMakerData? precomputedModifiedData, bool includeXdeltaFallback, G3MCacheOptions? cacheOptions)
		=> CreatePatchAsync(originalPath, modifiedPath, outputPath, precomputedOriginalHashes, precomputedOriginalInfo, precomputedOriginalNameCounts, precomputedOriginalOrderedNames, precomputedModifiedData, includeXdeltaFallback, cacheOptions, CancellationToken.None);

	public static async Task<PatchCreateResult> CreatePatchAsync(string originalPath, string modifiedPath, string outputPath, Dictionary<string, Dictionary<string, string>>? precomputedOriginalHashes = null, DataFileInfo? precomputedOriginalInfo = null, Dictionary<string, Dictionary<string, int>>? precomputedOriginalNameCounts = null, Dictionary<string, IReadOnlyList<string>>? precomputedOriginalOrderedNames = null, GameMakerData? precomputedModifiedData = null, bool includeXdeltaFallback = false, G3MCacheOptions? cacheOptions = null, CancellationToken cancellationToken = default)
	{
		using var cancellationScope = ModdingCancellation.Begin(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
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
		string? tempOutputPath = null;
		string? tempExactPatch = null;
		string? exactPatchSourcePath = null;
		string? materializationDirectory = null;
		string? tempDir2 = null;
		try
		{
			if (PatchInputService.IsXDelta(modifiedPath))
			{
				materializationDirectory = Directory.CreateTempSubdirectory("g3mlib-xdelta-").FullName;
				modifiedPath = await PatchInputService.MaterializeDataAsync(originalPath, modifiedPath, materializationDirectory).ConfigureAwait(false);
			}
			var totalSw = Stopwatch.StartNew();
			var phaseSw = new Stopwatch();
			tempDir2 = Path.Combine(TempRoot.Path, $"g3mlib_{Guid.NewGuid():N}");
			var modifiedExportDir = Path.Combine(tempDir2, "modified");
			Directory.CreateDirectory(modifiedExportDir);
			LogService.SetOperation("Creating patch");
			LogService.Progress(0, 100);
			var originalCodeEntryCount = -1;
			var originalCodeEntryCountRecovered = false;
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
				var cachedOriginal = G3MCacheService.TryReadDataCache(originalPath, cacheOptions);
				if (cachedOriginal != null)
				{
					originalInfo = cachedOriginal.DataInfo;
					originalHashes = cachedOriginal.ResourceHashes;
					originalNameCounts = cachedOriginal.ResourceNameCounts;
					originalOrderedNames = G3MCacheService.ToReadOnlyOrderNames(cachedOriginal.OrderSensitiveNames);
					var handler = new LogInterpolatedStringHandler(25, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler.AppendLiteral("[PatchService] Original: ");
						handler.AppendFormatted(originalInfo.GeneralInfo?.DisplayName ?? "Unknown");
					}
					LogService.Log(ref handler);
					var handler2 = new LogInterpolatedStringHandler(41, 2, out shouldAppend);
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
					using var stream = OpenDataReadStream(originalPath);
					using var originalData = await GameMakerIO.ReadAsync(stream).ConfigureAwait(false);
					originalCodeEntryCount = originalData.Code?.Count ?? 0;
					var originalLoadTime = phaseSw.Elapsed;
					var handler3 = new LogInterpolatedStringHandler(25, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler3.AppendLiteral("[PatchService] Original: ");
						handler3.AppendFormatted(originalData.GeneralInfo?.DisplayName?.Content ?? "Unknown");
					}
					LogService.Log(ref handler3);
					var dataFileInfo = new DataFileInfo
					{
						Filename = Path.GetFileName(originalPath),
						Size = new FileInfo(originalPath).Length
					};
					var dataFileInfo2 = dataFileInfo;
					key = dataFileInfo2.Md5 = await HashService.ComputeFileHashAsync(originalPath).ConfigureAwait(false);
					dataFileInfo.BytecodeVersion = originalData.GeneralInfo?.BytecodeVersion ?? 0;
					dataFileInfo.GmsVersion = GeneralInfoUtil.GetVersionDisplay(originalData.GeneralInfo);
					dataFileInfo.GeneralInfo = GeneralInfoUtil.ExtractGeneralInfo(originalData);
					originalInfo = dataFileInfo;
					phaseSw.Restart();
					LogService.Log("[PatchService] Hashing original resources in memory...");
					originalHashes = ResourceHashService.HashAll(originalData, originalPath);
					originalNameCounts = GetResourceNameCounts(originalData);
					originalOrderedNames = GetOrderSensitiveResourceNames(originalData);
					await G3MCacheService.WriteDataCacheAsync(originalPath, originalInfo, originalHashes, originalNameCounts, originalOrderedNames, cacheOptions).ConfigureAwait(false);
					var handler4 = new LogInterpolatedStringHandler(48, 3, out shouldAppend);
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
				var handler5 = new LogInterpolatedStringHandler(48, 2, out shouldAppend);
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
#pragma warning disable CA1062 // originalHashes is assigned from a non-null source in every branch above
			if (originalCodeEntryCount < 0 && originalHashes.TryGetValue("CodeEntries", out var originalCodeHashes))
#pragma warning restore CA1062
			{
				originalCodeEntryCount = originalCodeHashes.Count;
				originalCodeEntryCountRecovered = true;
			}
			LogService.Progress(20, 100);
			phaseSw.Restart();
			LogService.Log((precomputedModifiedData != null) ? "[PatchService] Using preloaded modified data..." : "[PatchService] Loading modified data file...");
			var modifiedCacheOptions = materializationDirectory is null ? cacheOptions : null;
			var helperForcedResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			DataFileInfo modifiedInfo;
			Dictionary<string, Dictionary<string, string>> modifiedHashes;
			string key2;
			Dictionary<string, HashSet<string>> manifestChangedNamesPerType;
			Dictionary<string, (string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms)> codeEntriesInMemory;
			using (var stream = (precomputedModifiedData == null) ? OpenDataReadStream(modifiedPath) : null)
			{
				using var originalData = (stream == null) ? null : (await GameMakerIO.ReadAsync(stream).ConfigureAwait(false));
				var modifiedData = precomputedModifiedData ?? originalData;
				var modifiedCodeEntryCount = modifiedData.Code?.Count ?? 0;
				var originalLoadTime = phaseSw.Elapsed;
				var handler6 = new LogInterpolatedStringHandler(25, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler6.AppendLiteral("[PatchService] Modified: ");
					handler6.AppendFormatted(modifiedData.GeneralInfo?.DisplayName?.Content ?? "Unknown");
				}
				LogService.Log(ref handler6);
				var cachedModified = G3MCacheService.TryReadDataCache(modifiedPath, modifiedCacheOptions);
				if (cachedModified != null)
				{
					modifiedInfo = cachedModified.DataInfo;
					modifiedHashes = cachedModified.ResourceHashes;
				}
				else
				{
					var dataFileInfo2 = new DataFileInfo
					{
						Filename = Path.GetFileName(modifiedPath),
						Size = new FileInfo(modifiedPath).Length
					};
					var dataFileInfo = dataFileInfo2;
					key = dataFileInfo.Md5 = await HashService.ComputeFileHashAsync(modifiedPath).ConfigureAwait(false);
					dataFileInfo2.BytecodeVersion = modifiedData.GeneralInfo?.BytecodeVersion ?? 0;
					dataFileInfo2.GmsVersion = GeneralInfoUtil.GetVersionDisplay(modifiedData.GeneralInfo);
					dataFileInfo2.GeneralInfo = GeneralInfoUtil.ExtractGeneralInfo(modifiedData);
					modifiedInfo = dataFileInfo2;
					phaseSw.Restart();
					LogService.Log("[PatchService] Hashing modified resources in memory...");
					modifiedHashes = ResourceHashService.HashAll(modifiedData, modifiedPath);
					await G3MCacheService.WriteDataCacheAsync(modifiedPath, modifiedInfo, modifiedHashes, GetResourceNameCounts(modifiedData), GetOrderSensitiveResourceNames(modifiedData), modifiedCacheOptions).ConfigureAwait(false);
				}
				LogCreateCompatibilityWarnings(originalInfo, modifiedInfo);
				LogService.Progress(40, 100);
				var hashTime = phaseSw.Elapsed;
				phaseSw.Restart();
				LogService.Log("[PatchService] Comparing hashes to build selective export filter...");
				Dictionary<string, HashSet<string>> changedNamesPerType = [];
				var files = ResourceTypeRegistry.AllTypes;
				foreach (var resourceType in files)
				{
					var origTypeHashes = originalHashes.GetValueOrDefault(resourceType) ?? [];
					var obj = modifiedHashes.GetValueOrDefault(resourceType) ?? [];
					HashSet<string> changedNames = [];
					foreach (var item in obj)
					{
						item.Deconstruct(out key, out key2);
						var name = key;
						var hash = key2;
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
				var handler7 = new LogInterpolatedStringHandler(41, 1, out shouldAppend);
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
				ResourceExportService.ExportSelectiveExceptCode(modifiedData, modifiedExportDir, modifiedPath, changedNamesPerType, cancellationToken);
				var changedCodeNames = changedNamesPerType.GetValueOrDefault("CodeEntries") ?? [];
				var codeExportSw = Stopwatch.StartNew();
				codeEntriesInMemory = ResourceExportService.ExportCodeEntriesToMemory(modifiedData, changedCodeNames);
				codeExportSw.Stop();
				var handler8 = new LogInterpolatedStringHandler(43, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler8.AppendLiteral("[Export Timing] CodeEntries (in-memory ");
					handler8.AppendFormatted(changedCodeNames.Count);
					handler8.AppendLiteral("): ");
					handler8.AppendFormatted(codeExportSw.Elapsed.TotalSeconds, "F1");
					handler8.AppendLiteral("s");
				}
				LogService.Log(ref handler8);
				var exportTime = phaseSw.Elapsed;
				phaseSw.Restart();
				var helpersExportDir = Path.Combine(tempDir2, "Helpers");
				ExportPatchHelpers(modifiedData, helpersExportDir, originalHashes, modifiedHashes, changedNamesPerType, helperForcedResourceTypes, originalCodeEntryCount, originalCodeEntryCountRecovered, modifiedCodeEntryCount, codeEntriesInMemory);
				var handler9 = new LogInterpolatedStringHandler(71, 5, out shouldAppend);
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
				var handler10 = new LogInterpolatedStringHandler(48, 2, out shouldAppend);
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
			var manifest = new G3MPatchManifest
			{
				CreatedAt = DateTime.UtcNow.ToString("o"),
				Library = new ToolInfo { Name = "G3MLib", Version = ModdingMetadata.LibraryVersion },
				Tool = new ToolInfo
				{
					Name = ModdingMetadata.ToolName,
					Version = ModdingMetadata.ToolVersion
				},
				Original = originalInfo,
				Modified = modifiedInfo,
				Resources = [],
				Statistics = new PatchStatistics()
			};
			try
			{
				phaseSw.Restart();
				LogService.Log("[PatchService] Comparing resources by in-memory hash...");
				var files = ResourceTypeRegistry.AllTypes;
				foreach (var resourceType2 in files)
				{
					var originalHashes2 = originalHashes.GetValueOrDefault(resourceType2) ?? [];
					var modTypeHashes = modifiedHashes.GetValueOrDefault(resourceType2) ?? [];
					var changes = BuildResourceTypeChangesFromKnownDifferences(originalHashes2, modTypeHashes, modifiedExportDir, resourceType2, manifestChangedNamesPerType.GetValueOrDefault(resourceType2));
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
				var handler11 = new LogInterpolatedStringHandler(27, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler11.AppendLiteral("[Timing] Hash comparison: ");
					handler11.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
					handler11.AppendLiteral("s");
				}
				LogService.Log(ref handler11);
				ResourceTypeChanges value;
				foreach (var resource in manifest.Resources)
				{
					resource.Deconstruct(out key2, out value);
					var changes2 = value;
					manifest.Statistics.TotalChanged += changes2.Changed?.Count ?? 0;
					manifest.Statistics.TotalNew += changes2.New?.Count ?? 0;
					manifest.Statistics.TotalDeleted += changes2.Deleted?.Count ?? 0;
					IEnumerable<ResourceChange> changed = changes2.Changed;
					foreach (var c in changed ?? [])
					{
						manifest.Statistics.TotalChangedFiles += c.Files?.Count ?? 0;
					}
					changed = changes2.New;
					foreach (var n in changed ?? [])
					{
						manifest.Statistics.TotalNewFiles += n.Files?.Count ?? 0;
					}
				}
				manifest.ApplyPlan = BuildPatchApplyPlan(manifest.Resources);
				var outputDir = Path.GetDirectoryName(outputPath);
				if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
				{
					Directory.CreateDirectory(outputDir);
				}
				phaseSw.Restart();
				LogService.Log("[PatchService] Creating .g3mpatch file...");
				tempOutputPath = Path.GetFullPath(outputPath) + $".{Guid.NewGuid():N}.tmp";
				using (var stream = new FileStream(tempOutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1048576, FileOptions.SequentialScan))
				{
					using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
					using (var writer = new StreamWriter(await archive.CreateEntry("g3mpatch.json", ArchiveCompressionUtil.GetLevel("g3mpatch.json")).OpenAsync(cancellationToken).ConfigureAwait(false)))
					{
						var json = JsonSerializer.Serialize(manifest, s_jsonOptions);
						await writer.WriteAsync(json).ConfigureAwait(false);
					}
					foreach (var resource2 in manifest.Resources)
					{
						resource2.Deconstruct(out key2, out value);
						var resourceType3 = key2;
						var changes3 = value;
						if (resourceType3 == "CodeEntries")
						{
							continue;
						}
						var modifiedResDir = Path.Combine(modifiedExportDir, resourceType3);
						IEnumerable<ResourceChange> changed = changes3.Changed;
						foreach (var changed2 in changed ?? [])
						{
							await AddResourceToArchiveAsync(archive, modifiedResDir, resourceType3, changed2.Name).ConfigureAwait(false);
						}
						changed = changes3.New;
						foreach (var newRes in changed ?? [])
						{
							await AddResourceToArchiveAsync(archive, modifiedResDir, resourceType3, newRes.Name).ConfigureAwait(false);
						}
					}
					if (codeEntriesInMemory.Count > 0)
					{
						foreach (var item2 in codeEntriesInMemory)
						{
							item2.Deconstruct(out key2, out var value2);
							var resourceType3 = key2;
							(var LogicalName, var gml, var asm, var childAsms) = value2;
							if (gml != null)
							{
								var gmlPath = $"CodeEntries/{resourceType3}/{resourceType3}.gml";
								using var writer = new StreamWriter(await archive.CreateEntry(gmlPath, ArchiveCompressionUtil.GetLevel(gmlPath)).OpenAsync(cancellationToken).ConfigureAwait(false), Encoding.UTF8);
								await writer.WriteAsync(gml).ConfigureAwait(false);
							}
							if (asm != null)
							{
								var asmPath = $"CodeEntries/{resourceType3}/{resourceType3}.asm";
								using var writer = new StreamWriter(await archive.CreateEntry(asmPath, ArchiveCompressionUtil.GetLevel(asmPath)).OpenAsync(cancellationToken).ConfigureAwait(false), Encoding.UTF8);
								await writer.WriteAsync(asm).ConfigureAwait(false);
							}
							if (childAsms == null)
							{
								continue;
							}
							foreach (var item3 in childAsms)
							{
								item3.Deconstruct(out key2, out var value3);
								var childLeafKey = key2;
								var childEntry = value3;
								var childPath = $"CodeEntries/{resourceType3}/{childLeafKey}.asm";
								using var writer = new StreamWriter(await archive.CreateEntry(childPath, ArchiveCompressionUtil.GetLevel(childPath)).OpenAsync(cancellationToken).ConfigureAwait(false), Encoding.UTF8);
								await writer.WriteAsync(childEntry.Asm).ConfigureAwait(false);
							}
						}
						var handler12 = new LogInterpolatedStringHandler(54, 1, out shouldAppend);
						if (shouldAppend)
						{
							handler12.AppendLiteral("[PatchService] CodeEntries: ");
							handler12.AppendFormatted(codeEntriesInMemory.Count);
							handler12.AppendLiteral(" written directly to patch");
						}
						LogService.Log(ref handler12);
					}
					var helpersDir = Path.Combine(tempDir2, "Helpers");
					if (Directory.Exists(helpersDir))
					{
						var files2 = Directory.GetFiles(helpersDir, "*", SearchOption.AllDirectories);
						foreach (var resourceType3 in files2)
						{
							var relative = Path.GetRelativePath(helpersDir, resourceType3).Replace('\\', '/');
							if (relative.StartsWith("EmbeddedTextures/", StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}
							var helperEntryPath = "Helpers/" + relative;
							using var entryStream = await archive.CreateEntry(helperEntryPath, ArchiveCompressionUtil.GetLevel(helperEntryPath)).OpenAsync(cancellationToken).ConfigureAwait(false);
							using var fileStream = File.OpenRead(resourceType3);
							await fileStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
						}
						LogService.Log("[PatchService] Helpers data added to patch");
					}
					if (includeXdeltaFallback && exactPatchSourcePath == null)
					{
						var xDeltaService2 = new XDeltaService();
						tempExactPatch = Path.Combine(TempRoot.Path, $"g3mlib_xdelta_fallback_{Guid.NewGuid():N}.xdelta");
						var exactResult = await xDeltaService2.CreatePatchAsync(originalPath, modifiedPath, tempExactPatch).ConfigureAwait(false);
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
						using var entryStream = await archive.CreateEntry("Xdelta/" + Path.GetFileName(exactPatchSourcePath), CompressionLevel.Fastest).OpenAsync(cancellationToken).ConfigureAwait(false);
						using var fileStream = File.OpenRead(exactPatchSourcePath);
						await fileStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
					}
				}
				LogService.Progress(100, 100);
				File.Move(tempOutputPath, outputPath, overwrite: true);
				var handler13 = new LogInterpolatedStringHandler(23, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler13.AppendLiteral("[Timing] Patch write: ");
					handler13.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
					handler13.AppendLiteral("s");
				}
				LogService.Log(ref handler13);
				LogService.ProgressComplete();
				totalSw.Stop();
				var handler14 = new LogInterpolatedStringHandler(46, 2, out shouldAppend);
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
					if (tempOutputPath is not null) File.Delete(tempOutputPath);
				}
				catch
				{
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
		catch (Exception ex) when (ModdingCancellation.IsCancellation(ex))
		{
			if (tempExactPatch != null)
			{
				try { File.Delete(tempExactPatch); }
				catch { }
			}
			ModdingCancellation.Rethrow(ex, cancellationToken);
			throw;
		}
		catch (Exception ex)
		{
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
		finally
		{
			if (tempDir2 is not null && Directory.Exists(tempDir2))
			{
				try { Directory.Delete(tempDir2, recursive: true); }
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{ LogService.Warning("Could not delete temporary export directory: " + ex.Message); }
			}
			if (materializationDirectory is not null)
			{
				try { Directory.Delete(materializationDirectory, recursive: true); }
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{ LogService.Warning("Could not delete temporary materialization: " + ex.Message); }
			}
		}

	}

	private static Dictionary<string, Dictionary<string, int>> ReadResourceNameCounts(string dataPath)
	{
		using var stream = OpenDataReadStream(dataPath);
		using var data = GameMakerIO.Read(stream);
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
		var counts = new Dictionary<string, int>(StringComparer.Ordinal);
		if (list == null)
		{
			return counts;
		}
		foreach (var item2 in list)
		{
			var name = item2?.Name?.Content;
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
		return source.ToDictionary(pair => pair.Key, pair => new HashSet<string>(pair.Value, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
	}

	private static void ExportPatchHelpers(GameMakerData modifiedData, string helpersExportDir, Dictionary<string, Dictionary<string, string>> originalHashes, Dictionary<string, Dictionary<string, string>> modifiedHashes, Dictionary<string, HashSet<string>> changedNamesPerType, HashSet<string> helperForcedResourceTypes, int originalCodeEntryCount, bool originalCodeEntryCountRecovered, int modifiedCodeEntryCount, Dictionary<string, (string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms)> codeEntriesInMemory)
	{
		var helperRelevantResourceTypes = GetHelperRelevantResourceTypes(originalHashes, modifiedHashes, changedNamesPerType);
		foreach (var forcedType in helperForcedResourceTypes)
		{
			helperRelevantResourceTypes.Add(forcedType);
		}
		if (!RequiresPatchHelpers(helperRelevantResourceTypes, helperForcedResourceTypes))
		{
			return;
		}
		var changedCodeNames = changedNamesPerType.GetValueOrDefault("CodeEntries") ?? [];
		if (CanUseCompactCodeMetadata(originalHashes, modifiedHashes, helperRelevantResourceTypes, helperForcedResourceTypes, originalCodeEntryCount, originalCodeEntryCountRecovered, modifiedCodeEntryCount, changedCodeNames, codeEntriesInMemory))
		{
			var includeAssetOrder = helperRelevantResourceTypes.Any(type => !type.Equals("CodeEntries", StringComparison.OrdinalIgnoreCase));
			ResourceExportService.ExportAssetOrder(modifiedData, helpersExportDir, RequiresObjectEventsHelper(changedNamesPerType), includeVariablesFunctions: false, RequiresTextureMappingHelpers(helperRelevantResourceTypes), includeAssetOrder ? BuildRelevantAssetOrderSections(helperRelevantResourceTypes, includeScriptsForCodeEntries: true) : null, includeAssetOrder, changedCodeNames);
			LogService.Log("[PatchService] Using compact code metadata for index-stable ASM code changes.");
		}
		else
		{
			var includeVariablesFunctions = RequiresVariableFunctionsHelper(helperRelevantResourceTypes);
			var includeTextureHelpers = RequiresTextureMappingHelpers(helperRelevantResourceTypes);
			var assetOrderSections = BuildRelevantAssetOrderSections(helperRelevantResourceTypes, changedCodeNames.Count > 0);
			ResourceExportService.ExportAssetOrder(modifiedData, helpersExportDir, RequiresObjectEventsHelper(changedNamesPerType), includeVariablesFunctions, includeTextureHelpers, assetOrderSections);
		}
	}

	private static bool CanUseCompactCodeMetadata(Dictionary<string, Dictionary<string, string>> originalHashes, Dictionary<string, Dictionary<string, string>> modifiedHashes, HashSet<string> helperRelevantResourceTypes, HashSet<string> helperForcedResourceTypes, int originalCodeEntryCount, bool originalCodeEntryCountRecovered, int modifiedCodeEntryCount, HashSet<string> changedCodeNames, Dictionary<string, (string LogicalName, string? gml, string? asm, Dictionary<string, (string LogicalName, string Asm)>? childAsms)> codeEntriesInMemory)
	{
		if (helperForcedResourceTypes.Count != 0 || !helperRelevantResourceTypes.Contains("CodeEntries") || originalCodeEntryCount < 0 || !originalCodeEntryCountRecovered && originalCodeEntryCount != modifiedCodeEntryCount || changedCodeNames.Count == 0 || codeEntriesInMemory.Count == 0 || codeEntriesInMemory.Values.Any<(string, string, string, Dictionary<string, (string, string)>)>(((string LogicalName, string gml, string asm, Dictionary<string, (string LogicalName, string Asm)> childAsms) entry) => entry.asm == null))
		{
			return false;
		}
		var allTypes = ResourceTypeRegistry.AllTypes;
		foreach (var resourceType in allTypes)
		{
			IEnumerable<string> enumerable = originalHashes.GetValueOrDefault(resourceType)?.Keys;
			var source = enumerable ?? [];
			enumerable = modifiedHashes.GetValueOrDefault(resourceType)?.Keys;
			var modifiedNames = enumerable ?? [];
			if (!source.ToHashSet(StringComparer.Ordinal).SetEquals(modifiedNames))
			{
				return false;
			}
		}
		return true;
	}

	private static void ExpandChangedCodeEntriesForTopology(GameMakerData modifiedData, Dictionary<string, HashSet<string>> changedNamesPerType)
	{
		if (!changedNamesPerType.TryGetValue("CodeEntries", out var changedCode) || changedCode.Count == 0)
		{
			return;
		}
		var addedParents = 0;
		var addedChildren = 0;
		bool changed;
		do
		{
			changed = false;
			foreach (var code in modifiedData.Code ?? [])
			{
				var codeName = code?.Name?.Content;
				if (code == null || string.IsNullOrEmpty(codeName))
				{
					continue;
				}
				var parentName = code.ParentEntry?.Name?.Content;
				if (!string.IsNullOrEmpty(parentName) && changedCode.Contains(codeName) && changedCode.Add(parentName))
				{
					addedParents++;
					changed = true;
				}
				if (!changedCode.Contains(codeName))
				{
					continue;
				}
				foreach (var item in code.ChildEntries ?? [])
				{
					var childName = item?.Name?.Content;
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
			var handler = new LogInterpolatedStringHandler(73, 2, out var shouldAppend);
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
		originalNameCounts.TryGetValue(resourceType, out var originalCounts);
		originalCounts ??= [];
		var modifiedCounts = CountResourceNames(modified);
		if (modifiedCounts.Any(pair => originalCounts.GetValueOrDefault(pair.Key) != pair.Value) && resourceType.Equals("GameObjects", StringComparison.OrdinalIgnoreCase))
		{
			HashSet<string> hashSet = [.. modifiedCounts.Keys];
			changedNamesPerType[resourceType] = hashSet;
			var handler = new LogInterpolatedStringHandler(66, 1, out var shouldAppend);
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
				if (!changedNamesPerType.TryGetValue(resourceType, out var changedNames))
				{
					changedNames = changedNamesPerType[resourceType] = [];
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
		PromoteOrderSensitiveExportIfNeeded("Rooms", originalOrderedNames, modifiedData.Rooms?.Select(r => r?.Name?.Content ?? "").ToList(), modifiedHashes, changedNamesPerType, helperForcedResourceTypes, forceHelpers: true);
		PromoteOrderSensitiveExportIfNeeded("Scripts", originalOrderedNames, modifiedData.Scripts?.Select(s => s?.Name?.Content ?? "").ToList(), modifiedHashes, changedNamesPerType, helperForcedResourceTypes, forceHelpers: false);
	}

	private static void PromoteOrderSensitiveExportIfNeeded(string resourceType, Dictionary<string, IReadOnlyList<string>> originalOrderedNames, IReadOnlyList<string>? modifiedOrderedNames, Dictionary<string, Dictionary<string, string>> modifiedHashes, Dictionary<string, HashSet<string>> changedNamesPerType, HashSet<string> helperForcedResourceTypes, bool forceHelpers)
	{
		if (!originalOrderedNames.TryGetValue(resourceType, out var originalNames) || modifiedOrderedNames == null || originalNames.SequenceEqual(modifiedOrderedNames, StringComparer.Ordinal))
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
			var handler = new LogInterpolatedStringHandler(83, 1, out shouldAppend);
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
			var handler2 = new LogInterpolatedStringHandler(51, 1, out shouldAppend);
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
		if (!modifiedHashes.TryGetValue(resourceType, out var typeHashes) || typeHashes.Count == 0)
		{
			return;
		}
		HashSet<string> hashSet = [.. typeHashes.Keys];
		changedNamesPerType[resourceType] = hashSet;
		var handler = new LogInterpolatedStringHandler(64, 1, out var shouldAppend);
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
		using var stream = OpenDataReadStream(dataPath);
		using var data = GameMakerIO.Read(stream);
		return GetOrderSensitiveResourceNames(data);
	}

	private static Dictionary<string, IReadOnlyList<string>> GetOrderSensitiveResourceNames(GameMakerData data)
	{
		return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
		{
			["Rooms"] = data.Rooms?.Select(r => r?.Name?.Content ?? "").ToList() ?? [],
			["Scripts"] = data.Scripts?.Select(s => s?.Name?.Content ?? "").ToList() ?? []
		};
	}

	public static Dictionary<string, Dictionary<string, int>> GetResourceNameCountsForReuse(GameMakerData data)
	{
		ArgumentNullException.ThrowIfNull(data);
		return GetResourceNameCounts(data);
	}

	public static Dictionary<string, IReadOnlyList<string>> GetOrderSensitiveResourceNamesForReuse(GameMakerData data)
	{
		ArgumentNullException.ThrowIfNull(data);
		return GetOrderSensitiveResourceNames(data);
	}

	private static ResourceTypeChanges BuildResourceTypeChangesFromKnownDifferences(Dictionary<string, string> originalHashes, Dictionary<string, string> modifiedHashes, string modifiedExportDir, string resourceType, HashSet<string>? knownChangedOrNewNames)
	{
		var changes = new ResourceTypeChanges
		{
			Changed = [],
			New = [],
			Deleted = []
		};
		var modifiedResDir = Path.Combine(modifiedExportDir, resourceType);
		var folderMap = BuildBaseNameMap(modifiedResDir);
		if (knownChangedOrNewNames != null)
		{
			foreach (var name in knownChangedOrNewNames)
			{
				List<string> folderNames = [];
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
				var isNew = !originalHashes.ContainsKey(name) && modifiedHashes.ContainsKey(name);
				foreach (var folderName in folderNames)
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
		foreach (var name2 in originalHashes.Keys.Intersect(modifiedHashes.Keys))
		{
			if (knownChangedOrNewNames != null && knownChangedOrNewNames.Contains(name2) || originalHashes[name2] == modifiedHashes[name2])
			{
				continue;
			}
			List<string> folderNames2 = [];
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
			foreach (var folderName2 in folderNames2)
			{
				changes.Changed.Add(CreateResourceChange(modifiedResDir, resourceType, folderName2));
			}
		}
		foreach (var name3 in originalHashes.Keys.Except(modifiedHashes.Keys))
		{
			changes.Deleted.Add(name3);
		}
		return changes;
	}

	private static List<string> GetFolderNamesForBaseName(string directory, string baseName)
	{
		if (!Directory.Exists(directory))
		{
			return [];
		}
		List<string> result = [];
		var directories = Directory.GetDirectories(directory);
		for (var i = 0; i < directories.Length; i++)
		{
			var folderName = Path.GetFileName(directories[i]);
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
		foreach (var change in (changes.Changed ?? []).Concat(changes.New ?? []))
		{
			if (string.IsNullOrWhiteSpace(change.Name) || !codeEntriesInMemory.TryGetValue(change.Name, out (string, string, string, Dictionary<string, (string, string)>) entry))
			{
				var resourceChange = change;
				if (resourceChange.Files == null)
				{
					_ = resourceChange.Files = [];
				}
				continue;
			}
			var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
		var resourcePath = Path.Combine(sourceDir, resourceName);
		var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string[] files2;
		if (!Directory.Exists(resourcePath) && resourceName == Path.GetFileName(sourceDir))
		{
			if (!Directory.Exists(sourceDir))
			{
				return files;
			}
			files2 = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
			foreach (var file in files2)
			{
				var relativeToResource = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
				var entryPath = Path.Combine(resourceType, relativeToResource).Replace('\\', '/');
				files[relativeToResource] = entryPath;
			}
			return files;
		}
		if (!Directory.Exists(resourcePath))
		{
			return files;
		}
		files2 = Directory.GetFiles(resourcePath, "*", SearchOption.AllDirectories);
		foreach (var file2 in files2)
		{
			var relativeToResource2 = Path.GetRelativePath(resourcePath, file2).Replace('\\', '/');
			var entryPath2 = Path.Combine(resourceType, resourceName, relativeToResource2).Replace('\\', '/');
			files[relativeToResource2] = entryPath2;
		}
		return files;
	}

	private static Dictionary<string, string> BuildBaseNameMap(string directory)
	{
		Dictionary<string, string> map = [];
		if (!Directory.Exists(directory))
		{
			return map;
		}
		Dictionary<string, int> baseNameCounts = [];
		List<(string, string)> entries = [];
		var directories = Directory.GetDirectories(directory);
		for (var i = 0; i < directories.Length; i++)
		{
			var folderName = Path.GetFileName(directories[i]);
			var baseName = StripIdxSuffix(folderName);
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
		var idxPos = folderName.LastIndexOf("__idx", StringComparison.Ordinal);
		if (idxPos <= 0)
		{
			return folderName;
		}
		var after = folderName[(idxPos + 5)..];
		if (after.Length > 0 && after.All(char.IsDigit))
		{
			return folderName[..idxPos];
		}
		return folderName;
	}

	private static async Task AddResourceToArchiveAsync(ZipArchive archive, string sourceDir, string resourceType, string resourceName, Dictionary<string, string>? filesToInclude = null)
	{
		var resourcePath = Path.Combine(sourceDir, resourceName);
		if (!Directory.Exists(resourcePath) && resourceName == Path.GetFileName(sourceDir))
		{
			if (!Directory.Exists(sourceDir))
			{
				return;
			}
			var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
			foreach (var file in files)
			{
				var relativeToResource = Path.GetRelativePath(sourceDir, file);
				if (filesToInclude != null && !filesToInclude.ContainsKey(relativeToResource))
				{
					continue;
				}
				var entryPath = Path.Combine(resourceType, relativeToResource).Replace('\\', '/');
				using var entryStream = await archive.CreateEntry(entryPath, ArchiveCompressionUtil.GetLevel(entryPath)).OpenAsync().ConfigureAwait(false);
				using var fileStream = File.OpenRead(file);
				await fileStream.CopyToAsync(entryStream).ConfigureAwait(false);
			}
		}
		else
		{
			if (!Directory.Exists(resourcePath))
			{
				return;
			}
			var files = Directory.GetFiles(resourcePath, "*", SearchOption.AllDirectories);
			foreach (var file in files)
			{
				var relativeToResource2 = Path.GetRelativePath(resourcePath, file);
				if (filesToInclude != null && !filesToInclude.ContainsKey(relativeToResource2))
				{
					continue;
				}
				var entryPath2 = Path.Combine(resourceType, resourceName, relativeToResource2).Replace('\\', '/');
				using var entryStream = await archive.CreateEntry(entryPath2, ArchiveCompressionUtil.GetLevel(entryPath2)).OpenAsync().ConfigureAwait(false);
				using var fileStream = File.OpenRead(file);
				await fileStream.CopyToAsync(entryStream).ConfigureAwait(false);
			}
		}
	}

	public static Task<PatchApplyResult> ApplyPatchAsync(string dataPath, string patchPath, string outputPath, bool allowXdeltaFallback, bool verifyModifiedHash)
		=> ApplyPatchAsync(dataPath, patchPath, outputPath, allowXdeltaFallback, verifyModifiedHash, CancellationToken.None);

	/// <summary>Applies a patch. A cancelled call throws <see cref="OperationCanceledException"/>, leaves the data file and any existing output untouched and removes its temporary files.</summary>
	public static async Task<PatchApplyResult> ApplyPatchAsync(string dataPath, string patchPath, string outputPath, bool allowXdeltaFallback = false, bool verifyModifiedHash = true, CancellationToken cancellationToken = default)
	{
		using var cancellationScope = ModdingCancellation.Begin(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		string? temporaryDirectory = null;
		try
		{
			var destinationPath = Path.GetFullPath(outputPath);
			var directory = Path.GetDirectoryName(destinationPath);
			Directory.CreateDirectory(directory);
			temporaryDirectory = Path.Combine(directory, $".g3mlib-apply-{Guid.NewGuid():N}");
			Directory.CreateDirectory(temporaryDirectory);
			var temporaryPath = Path.Combine(temporaryDirectory, Path.GetFileName(destinationPath));
			var result = await ApplyPatchCoreAsync(dataPath, patchPath, temporaryPath, allowXdeltaFallback, verifyModifiedHash).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
			if (result.Success)
			{
				PatchInputService.CopyExternalAudioGroups(dataPath, temporaryPath);
				PatchInputService.CommitDataFile(temporaryPath, destinationPath);
			}
			return result;
		}
		catch (Exception ex) when (ModdingCancellation.IsCancellation(ex))
		{
			ModdingCancellation.Rethrow(ex, cancellationToken);
			throw;
		}
		catch (Exception ex)
		{
			return new PatchApplyResult { Success = false, Error = "Failed to apply patch: " + ex.Message };
		}
		finally
		{
			if (temporaryDirectory is not null)
			{
				try { Directory.Delete(temporaryDirectory, recursive: true); }
				catch (IOException) { }
				catch (UnauthorizedAccessException) { }
			}
		}
	}

	/// <summary>
	/// Applies a <c>.g3mpatch</c> archive at <paramref name="patchPath"/> in place to an in-memory <see cref="GameMakerData"/> instance without reading or writing a data file on disk.
	/// </summary>
	/// <remarks>
	/// When <see cref="PatchApplyOptions.ValidateBeforeApply"/> is <see langword="true"/> (the default), the patch manifest, JSON payloads, resource types and required helpers are validated before <paramref name="data"/> is mutated so invalid patches are rejected cleanly.
	/// If patch application fails or is cancelled after resource import begins, <paramref name="data"/> may be left partially modified and should be discarded or reloaded by the caller.
	/// </remarks>
	public static async Task<PatchApplyResult> ApplyPatchAsync(
		GameMakerData data,
		string patchPath,
		PatchApplyOptions? options = null,
		IProgress<BatchProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(data);
		ArgumentNullException.ThrowIfNull(patchPath);
		using var cancellationScope = ModdingCancellation.Begin(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
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
			LogService.SetOperation("Applying patch");
			LogService.Progress(0, 100);
			var totalSw = Stopwatch.StartNew();
			var patchLoadSw = Stopwatch.StartNew();
			LogService.Log("[PatchService] Loading .g3mpatch file into memory...");
			var pfs = await Task.Run(() => PatchFileSystem.LoadFromZip(patchPath, loadExactPayloads: false), cancellationToken).ConfigureAwait(false);
			patchLoadSw.Stop();
			return ApplyLoadedPatchInMemory(data, pfs, options, progress, totalSw, patchLoadSw, cancellationToken);
		}
		catch (Exception ex) when (ModdingCancellation.IsCancellation(ex))
		{
			ModdingCancellation.Rethrow(ex, cancellationToken);
			throw;
		}
		catch (Exception ex)
		{
			return new PatchApplyResult { Success = false, Error = "Failed to apply patch: " + ex.Message };
		}
	}

	/// <summary>
	/// Applies a <c>.g3mpatch</c> archive from <paramref name="patchStream"/> in place to an in-memory <see cref="GameMakerData"/> instance without reading or writing a data file on disk or creating a temporary patch file.
	/// </summary>
	/// <remarks>
	/// The caller retains ownership of <paramref name="patchStream"/>; this method does not dispose it. Both seekable and non-seekable streams (such as a <see cref="ZipArchiveEntry"/> stream) are supported.
	/// When <see cref="PatchApplyOptions.ValidateBeforeApply"/> is <see langword="true"/> (the default), the patch manifest, JSON payloads, resource types and required helpers are validated before <paramref name="data"/> is mutated so invalid patches are rejected cleanly.
	/// If patch application fails or is cancelled after resource import begins, <paramref name="data"/> may be left partially modified and should be discarded or reloaded by the caller.
	/// </remarks>
	public static async Task<PatchApplyResult> ApplyPatchAsync(
		GameMakerData data,
		Stream patchStream,
		PatchApplyOptions? options = null,
		IProgress<BatchProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(data);
		ArgumentNullException.ThrowIfNull(patchStream);
		using var cancellationScope = ModdingCancellation.Begin(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			LogService.SetOperation("Applying patch");
			LogService.Progress(0, 100);
			var totalSw = Stopwatch.StartNew();
			var patchLoadSw = Stopwatch.StartNew();
			LogService.Log("[PatchService] Loading .g3mpatch stream into memory...");
			var pfs = await Task.Run(() => PatchFileSystem.LoadFromZip(patchStream, loadExactPayloads: false), cancellationToken).ConfigureAwait(false);
			patchLoadSw.Stop();
			return ApplyLoadedPatchInMemory(data, pfs, options, progress, totalSw, patchLoadSw, cancellationToken);
		}
		catch (Exception ex) when (ModdingCancellation.IsCancellation(ex))
		{
			ModdingCancellation.Rethrow(ex, cancellationToken);
			throw;
		}
		catch (Exception ex)
		{
			return new PatchApplyResult { Success = false, Error = "Failed to apply patch: " + ex.Message };
		}
	}

	private static PatchApplyResult ApplyLoadedPatchInMemory(
		GameMakerData data,
		PatchFileSystem pfs,
		PatchApplyOptions? options,
		IProgress<BatchProgress>? progress,
		Stopwatch totalSw,
		Stopwatch patchLoadSw,
		CancellationToken cancellationToken)
	{
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			LogService.Progress(5, 100);
			var validateBeforeApply = options?.ValidateBeforeApply ?? true;
			var dataFilePath = options?.DataFilePath;
			var result = ApplyPatchToDataCore(
				data,
				pfs,
				dataFilePath,
				null, // in memory: nothing is written to disk, so audio groups stored outside the data file are skipped
				logDetailedCodeImport: true,
				validateBeforeApply: validateBeforeApply,
				progress: progress,
				totalSw: totalSw,
				patchLoadSw: patchLoadSw,
				out _,
				out var usedMinimalApply);
			cancellationToken.ThrowIfCancellationRequested();
			if (result.Success && !usedMinimalApply)
			{
				LogService.Progress(100, 100);
				LogService.ProgressComplete();
			}
			return result;
		}
		finally
		{
			pfs.ReleaseFileData();
			pfs.ReleaseCodeEntries();
		}
	}

	private static string? FirstJsonError(ReadOnlySpan<byte> json)
	{
		try
		{
			var reader = new Utf8JsonReader(json);
			while (reader.Read())
			{
			}

			return null;
		}
		catch (JsonException ex)
		{
			return ex.Message;
		}
	}

	private static string? ValidatePatchBeforeApply(PatchFileSystem pfs, List<string> resourceTypesToProcess, bool hasCodeEntries)
	{
		if (!pfs.FileExists("g3mpatch.json") || pfs.Manifest == null)
		{
			return "Invalid patch: g3mpatch.json not found or failed to parse manifest";
		}
		foreach (var filePath in pfs.GetAllFiles().Keys)
		{
			if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			// A reader pass checks the syntax without building a document, so the check costs no allocation.
			var json = pfs.ReadAllBytes(filePath).AsSpan();
			if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF)
			{
				json = json[3..];
			}

			var error = FirstJsonError(json);
			if (error is not null)
			{
				return $"Invalid patch JSON in '{filePath}': {error}";
			}
		}
		foreach (var resourceType in resourceTypesToProcess)
		{
			if (!resourceType.Equals("CodeEntries", StringComparison.OrdinalIgnoreCase) &&
				!ResourceTypeRegistry.AllTypes.Contains(resourceType, StringComparer.OrdinalIgnoreCase))
			{
				return $"Unsupported resource type in patch: '{resourceType}'";
			}
		}
		if (hasCodeEntries && ReadCodeMetadataContent(pfs, pfs.HelpersPrefix) == null)
		{
			return "Invalid patch: CodeEntries present without variables_functions.json or code_patch_metadata.json";
		}
		return null;
	}

	private static PatchApplyResult ApplyPatchToDataCore(
		GameMakerData data,
		PatchFileSystem pfs,
		string? dataPath,
		string? outputPath,
		bool logDetailedCodeImport,
		bool validateBeforeApply,
		IProgress<BatchProgress>? progress,
		Stopwatch totalSw,
		Stopwatch patchLoadSw,
		out bool noOp,
		out bool usedMinimalApply)
	{
		noOp = false;
		usedMinimalApply = false;
		using var warningScope = LogService.CollectWarnings(out var warningsQueue);
		var manifest = pfs.Manifest;
		var applyPlan = manifest?.ApplyPlan ?? BuildPatchApplyPlan(manifest?.Resources ?? new Dictionary<string, ResourceTypeChanges>(StringComparer.OrdinalIgnoreCase));
		var phaseSw = new Stopwatch();
		var nonCodeImportSw = new Stopwatch();
		var codeImportSw = new Stopwatch();
		var finalizeSw = new Stopwatch();
		var preCodeReorderSw = new Stopwatch();
		var postSpriteReorderSw = new Stopwatch();
		var finalReorderSw = new Stopwatch();
		var repairDuplicatesSw = new Stopwatch();
		var applyEventsSw = new Stopwatch();
		var repairDanglingCodeSw = new Stopwatch();
		var ensureScriptsSw = new Stopwatch();
		var integritySw = new Stopwatch();
		PatchApplyPreparation? preparation = null;
		bool shouldAppend;
		ResourceImportService.SetPatchFileSystem(pfs);
		try
		{
			var resourceTypesToProcess = BuildResourceTypesToProcess(pfs, manifest);
			var scriptOrderOnly = resourceTypesToProcess.Count == 0 && HasScriptOrderOnlyAssetPayload(pfs);
			if (scriptOrderOnly)
			{
				resourceTypesToProcess.Add("Scripts");
			}
			var hasCodeEntries = CreateImportProgressState(resourceTypesToProcess).HasCodeEntries;
			if (validateBeforeApply)
			{
				var validationError = ValidatePatchBeforeApply(pfs, resourceTypesToProcess, hasCodeEntries);
				if (validationError != null)
				{
					return new PatchApplyResult
					{
						Success = false,
						Error = validationError,
						Warnings = [.. warningsQueue]
					};
				}
			}
			var handler = new LogInterpolatedStringHandler(37, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Resources to process: ");
				handler.AppendFormatted(string.Join(", ", resourceTypesToProcess));
			}
			LogService.Log(ref handler);
			if (resourceTypesToProcess.Count == 0)
			{
				noOp = true;
				progress?.Report(new BatchProgress(0, 0, null));
				return new PatchApplyResult
				{
					Success = true,
					AppliedCount = 0,
					FailedCount = 0,
					Warnings = [.. warningsQueue]
				};
			}
			if (!scriptOrderOnly && CanUseMinimalStandardApply(applyPlan, resourceTypesToProcess))
			{
				usedMinimalApply = true;
				var handler2 = new LogInterpolatedStringHandler(58, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler2.AppendLiteral("[PatchService] Applying only required resource functions: ");
					handler2.AppendFormatted(string.Join(", ", resourceTypesToProcess));
				}
				LogService.Log(ref handler2);
				(var minimalAppliedCount, var minimalFailedCount) = ImportMinimalResources(data, pfs, resourceTypesToProcess, dataPath, outputPath, progress);
				if (minimalFailedCount > 0 || minimalAppliedCount == 0)
				{
					return new PatchApplyResult
					{
						Success = false,
						AppliedCount = minimalAppliedCount,
						FailedCount = minimalFailedCount,
						Warnings = [.. warningsQueue],
						Error = (minimalFailedCount > 0) ? $"Patch apply failed: {minimalFailedCount} resource type(s) failed during import; output was not written" : "No resources were applied successfully"
					};
				}
				var minimalIntegrity = DataIntegrityService.ValidateOnly(data);
				if (!minimalIntegrity.Success)
				{
					return new PatchApplyResult
					{
						Success = false,
						AppliedCount = minimalAppliedCount,
						FailedCount = minimalFailedCount,
						Warnings = [.. warningsQueue],
						Error = "Integrity validation failed: " + string.Join("; ", minimalIntegrity.Errors.Take(20))
					};
				}
				return new PatchApplyResult
				{
					Success = true,
					AppliedCount = minimalAppliedCount,
					FailedCount = minimalFailedCount,
					Warnings = [.. warningsQueue]
				};
			}
			if (logDetailedCodeImport)
			{
				LogService.Progress(5, 100);
			}
			preparation = PreparePatchApply(data, pfs, resourceTypesToProcess, applyPlan, hasCodeEntries, scriptOrderOnly);
			(var appliedCount, var failedCount) = ImportPatchResources(data, pfs, manifest, resourceTypesToProcess, preparation, hasCodeEntries, dataPath, outputPath, nonCodeImportSw, codeImportSw, phaseSw, preCodeReorderSw, postSpriteReorderSw, logDetailedCodeImport, progress);
			finalizeSw.Start();
			appliedCount += ApplyFinalAssetOrder(preparation.NeedsFinalAssetReorder, data, pfs, resourceTypesToProcess, preparation.Helpers, preparation.StagedEmbeddedTexturesDir, scriptOrderOnly, finalReorderSw) ? 1 : 0;
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
			var integrity = RepairAndValidateImportedData(data, resourceTypesToProcess, preparation.NeedsHeavyFinalize, repairDanglingCodeSw, ensureScriptsSw, integritySw);
			if (integrity != null && !integrity.Success)
			{
				finalizeSw.Stop();
				return new PatchApplyResult
				{
					Success = false,
					AppliedCount = appliedCount,
					FailedCount = failedCount,
					Warnings = [.. warningsQueue],
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
					AppliedCount = appliedCount,
					FailedCount = failedCount,
					Warnings = [.. warningsQueue],
					Error = $"Patch apply failed: {failedCount} resource type(s) failed during import; output was not written"
				};
			}
			if (appliedCount == 0)
			{
				return new PatchApplyResult
				{
					Success = false,
					AppliedCount = 0,
					FailedCount = 0,
					Warnings = [.. warningsQueue],
					Error = "No resources were applied successfully"
				};
			}
			var handler3 = new LogInterpolatedStringHandler(211, 12, out shouldAppend);
			if (shouldAppend)
			{
				handler3.AppendLiteral("[Timing] Apply in-memory breakdown: patchLoad=");
				handler3.AppendFormatted(patchLoadSw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s, nonCode=");
				handler3.AppendFormatted(nonCodeImportSw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s, code=");
				handler3.AppendFormatted(codeImportSw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s, finalize=");
				handler3.AppendFormatted(finalizeSw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s, reorderPreCode=");
				handler3.AppendFormatted(preCodeReorderSw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s, reorderPostSprite=");
				handler3.AppendFormatted(postSpriteReorderSw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s, reorderFinal=");
				handler3.AppendFormatted(finalReorderSw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s, repairDupes=");
				handler3.AppendFormatted(repairDuplicatesSw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s, applyEvents=");
				handler3.AppendFormatted(applyEventsSw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s, repairDangling=");
				handler3.AppendFormatted(repairDanglingCodeSw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s, ensureScripts=");
				handler3.AppendFormatted(ensureScriptsSw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s, integrity=");
				handler3.AppendFormatted(integritySw.Elapsed.TotalSeconds, "F2");
				handler3.AppendLiteral("s");
			}
			LogService.Log(ref handler3);
			return new PatchApplyResult
			{
				Success = true,
				AppliedCount = appliedCount,
				FailedCount = failedCount,
				Warnings = [.. warningsQueue]
			};
		}
		finally
		{
			ResourceImportService.SetPatchFileSystem(null);
			if (preparation?.StagedEmbeddedTexturesDir != null && Directory.Exists(preparation.StagedEmbeddedTexturesDir))
			{
				try
				{
					Directory.Delete(preparation.StagedEmbeddedTexturesDir, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private static async Task<PatchApplyResult> ApplyPatchCoreAsync(string dataPath, string patchPath, string outputPath, bool allowXdeltaFallback, bool verifyModifiedHash)
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
		PatchFileSystem? pfsForFallback = null;
		G3MPatchManifest? manifestForFallback = null;
		bool shouldAppend;
		try
		{
			var totalSw = Stopwatch.StartNew();
			var phaseSw = new Stopwatch();
			var patchLoadSw = new Stopwatch();
			var dataLoadSw = new Stopwatch();
			var saveSw = new Stopwatch();
			var verifyHashSw = new Stopwatch();
			LogService.SetOperation("Applying patch");
			LogService.Progress(0, 100);
			patchLoadSw.Start();
			phaseSw.Restart();
			LogService.Log("[PatchService] Loading .g3mpatch file into memory...");
			var pfs = await Task.Run(() => PatchFileSystem.LoadFromZip(patchPath, allowXdeltaFallback)).ConfigureAwait(false);
			var manifest = pfs.Manifest;
			patchLoadSw.Stop();
			pfsForFallback = pfs;
			manifestForFallback = manifest;
			shouldAppend = allowXdeltaFallback;
			if (shouldAppend)
			{
				shouldAppend = await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfs, manifest).ConfigureAwait(false);
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
			dataLoadSw.Start();
			phaseSw.Restart();
			LogService.Log("[PatchService] Loading data file for .g3mpatch apply...");
			GameMakerData data;
			using (var stream = OpenDataReadStream(dataPath))
			{
				data = await GameMakerIO.ReadAsync(stream).ConfigureAwait(false);
			}
			dataLoadSw.Stop();
			var handler = new LogInterpolatedStringHandler(30, 2, out shouldAppend);
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
			var handler2 = new LogInterpolatedStringHandler(28, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler2.AppendLiteral("[PatchService] Data loaded: ");
				handler2.AppendFormatted(data.GeneralInfo?.DisplayName?.Content ?? "Unknown");
			}
			LogService.Log(ref handler2);
			try
			{
				var inMemoryResult = ApplyPatchToDataCore(
					data,
					pfs,
					dataPath,
					outputPath,
					logDetailedCodeImport: true,
					validateBeforeApply: false,
					progress: null,
					totalSw: totalSw,
					patchLoadSw: patchLoadSw,
					out var noOp,
					out var usedMinimalApply);
				if (noOp)
				{
					LogService.Log("[PatchService] No resources to apply");
					var noOpOutDir = Path.GetDirectoryName(outputPath);
					if (!string.IsNullOrEmpty(noOpOutDir) && !Directory.Exists(noOpOutDir))
					{
						Directory.CreateDirectory(noOpOutDir);
					}
					File.Copy(dataPath, outputPath, overwrite: true);
					LogService.Progress(100, 100);
					LogService.ProgressComplete();
					return inMemoryResult;
				}
				if (!inMemoryResult.Success)
				{
					if (!usedMinimalApply && await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfs, manifest).ConfigureAwait(false))
					{
						return new PatchApplyResult
						{
							Success = true
						};
					}
					return inMemoryResult;
				}
				phaseSw.Restart();
				LogService.Log("[PatchService] Saving modified data file...");
				saveSw.Start();
				var outDir = Path.GetDirectoryName(outputPath);
				if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
				{
					Directory.CreateDirectory(outDir);
				}
				using (var outStream = OpenDataWriteStream(outputPath))
				{
					await GameMakerIO.WriteAsync(outStream, data).ConfigureAwait(false);
				}
				saveSw.Stop();
				var savedValidationError = ValidateSavedDataFile(outputPath);
				if (savedValidationError != null)
				{
					DeleteInvalidOutput(outputPath);
					if (!usedMinimalApply && await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfs, manifest).ConfigureAwait(false))
					{
						return new PatchApplyResult
						{
							Success = true
						};
					}
					return new PatchApplyResult
					{
						Success = false,
						AppliedCount = inMemoryResult.AppliedCount,
						FailedCount = inMemoryResult.FailedCount,
						Warnings = inMemoryResult.Warnings,
						Error = "Saved data validation failed: " + savedValidationError
					};
				}
				if (usedMinimalApply)
				{
					totalSw.Stop();
					var handler5 = new LogInterpolatedStringHandler(31, 1, out shouldAppend);
					if (shouldAppend)
					{
						handler5.AppendLiteral("[Timing] Minimal apply total: ");
						handler5.AppendFormatted(totalSw.Elapsed.TotalSeconds, "F1");
						handler5.AppendLiteral("s");
					}
					LogService.Log(ref handler5);
					return inMemoryResult;
				}
				var handler6 = new LogInterpolatedStringHandler(31, 2, out shouldAppend);
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
				var handler7 = new LogInterpolatedStringHandler(45, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler7.AppendLiteral("[Timing] === PATCH APPLY TOTAL: ");
					handler7.AppendFormatted(totalSw.Elapsed.TotalSeconds, "F1");
					handler7.AppendLiteral("s === RAM: ");
					handler7.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler7.AppendLiteral("MB");
				}
				LogService.Log(ref handler7);
				var expectedModifiedHash = manifest?.Modified?.Md5;
				if (verifyModifiedHash && allowXdeltaFallback && !string.IsNullOrWhiteSpace(expectedModifiedHash))
				{
					verifyHashSw.Start();
					var obj = await HashService.ComputeFileHashAsync(outputPath).ConfigureAwait(false);
					verifyHashSw.Stop();
					if (!obj.Equals(expectedModifiedHash, StringComparison.OrdinalIgnoreCase))
					{
						LogService.Warning("[PatchService] Output hash differs from the patch's modified file hash");
						if (await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfs, manifest).ConfigureAwait(false))
						{
							return new PatchApplyResult
							{
								Success = true
							};
						}
					}
				}
				LogService.Log("[PatchService] Patch applied successfully");
				return inMemoryResult;
			}
			finally
			{
				ResourceImportService.SetPatchFileSystem(null);
				pfs.ReleaseFileData();
				pfs.ReleaseCodeEntries();
				data.Dispose();
			}
		}
		catch (Exception ex2) when (ModdingCancellation.IsCancellation(ex2))
		{
			ModdingCancellation.Rethrow(ex2, ModdingCancellation.Token);
			throw;
		}
		catch (Exception ex2)
		{
			shouldAppend = pfsForFallback != null;
			if (shouldAppend)
			{
				shouldAppend = await TryApplyXdeltaFallbackFromPatchAsync(dataPath, outputPath, patchPath, pfsForFallback, manifestForFallback).ConfigureAwait(false);
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
			var totalSw = Stopwatch.StartNew();
			var patchLoadSw = Stopwatch.StartNew();
			LogService.Log("[PatchService] Loading .g3mpatch file into memory for in-memory apply...");
			var pfs = await Task.Run(() => PatchFileSystem.LoadFromZip(patchPath, loadExactPayloads: false)).ConfigureAwait(false);
			patchLoadSw.Stop();
			try
			{
				return ApplyPatchToDataCore(
					data,
					pfs,
					workingDataPath,
					workingDataPath,
					logDetailedCodeImport: false,
					validateBeforeApply: false,
					progress: null,
					totalSw: totalSw,
					patchLoadSw: patchLoadSw,
					out _,
					out _);
			}
			finally
			{
				pfs.ReleaseFileData();
				pfs.ReleaseCodeEntries();
			}
		}
		catch (Exception ex2) when (ModdingCancellation.IsCancellation(ex2))
		{
			ModdingCancellation.Rethrow(ex2, ModdingCancellation.Token);
			throw;
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
			var objectEventCodeFilter = resourceTypes.Contains("GameObjects", StringComparer.OrdinalIgnoreCase) ? null : GetChangedCodeEntryNames(manifest);
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
		var result = DataIntegrityService.RepairAndValidate(data);
		integritySw.Stop();
		return result;
	}

	private static void RepairDuplicateGameObjectPlaceholders(GameMakerData data)
	{
		var byName = new Dictionary<string, List<GameMakerGameObject>>(StringComparer.Ordinal);
		foreach (var obj in data.GameObjects)
		{
			var name = obj?.Name?.Content;
			if (!string.IsNullOrEmpty(name))
			{
				if (!byName.TryGetValue(name, out var list))
				{
					list = byName[name] = [];
				}
				list.Add(obj);
			}
		}
		var repaired = 0;
		foreach (var group in byName.Values)
		{
			if (group.Count < 2)
			{
				continue;
			}
			var source = group.Where(o => !IsGameObjectPlaceholder(o)).OrderByDescending(GameObjectCompletenessScore).FirstOrDefault();
			if (source == null)
			{
				continue;
			}
			foreach (var target in group)
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
			var handler = new LogInterpolatedStringHandler(60, 1, out var shouldAppend);
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
		var hasEvents = obj.Events.Any(e => e.Count > 0);
		if (obj.Sprite == null && obj.ParentId == null && obj.TextureMaskId == null && !obj.Solid && obj.Depth == 0 && !obj.Persistent)
		{
			return !hasEvents;
		}
		return false;
	}

	private static int GameObjectCompletenessScore(GameMakerGameObject obj)
	{
		var score = 0;
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
		if (obj.Events.Any(e => e.Count > 0))
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
		for (var eventType = 0; eventType < Math.Min(source.Events.Count, target.Events.Count); eventType++)
		{
			target.Events[eventType].Clear();
			foreach (var sourceEvent in source.Events[eventType])
			{
				var targetEvent = new GameMakerGameObject.Event
				{
					EventSubtype = sourceEvent.EventSubtype
				};
				foreach (var sourceAction in sourceEvent.Actions)
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
		var action = new GameMakerGameObject.EventAction();
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
			var actionName = actionNameElm.GetString();
			action.ActionName = (!string.IsNullOrEmpty(actionName)) ? data.Strings.MakeString(actionName) : null;
		}
		if (actionElm.TryGetProperty("codeId", out var codeIdElm))
		{
			var codeName = codeIdElm.GetString();
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
		HashSet<GameMakerCode> liveCodeEntries = [];
		foreach (var code in data.Code)
		{
			if (code != null)
			{
				liveCodeEntries.Add(code);
			}
		}
		var repairedScripts = 0;
		var nulledScripts = 0;
		foreach (var script in data.Scripts)
		{
			if (script?.Code == null || liveCodeEntries.Contains(script.Code))
			{
				continue;
			}
			GameMakerCode? replacement = null;
			var currentCodeName = script.Code.Name?.Content;
			if (!string.IsNullOrEmpty(currentCodeName))
			{
				replacement = ScriptCodeResolver.Resolve(data, script.Name?.Content, currentCodeName);
			}
			if (replacement == null)
			{
				var scriptName = script.Name?.Content;
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
		var removedGlobalInit = 0;
		for (var i = data.GlobalInitScripts.Count - 1; i >= 0; i--)
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
		var relinked = 0;
		var created = 0;
		var scriptNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (var script in data.Scripts)
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
		foreach (var item in data.Code)
		{
			foreach (var code in EnumerateCodeTreeRecursive(item))
			{
				var codeName = code?.Name?.Content;
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
			var handler = new LogInterpolatedStringHandler(53, 1, out shouldAppend);
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
			var handler2 = new LogInterpolatedStringHandler(71, 1, out shouldAppend);
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
		if (manifest?.Resources == null || !manifest.Resources.TryGetValue(resourceType, out var changes) || changes.Deleted == null || changes.Deleted.Count == 0)
		{
			return 0;
		}
		var handled = 0;
		foreach (var name in changes.Deleted)
		{
			if (RemoveResourceByName(data, resourceType, name))
			{
				handled++;
			}
		}
		if (handled > 0)
		{
			var handler = new LogInterpolatedStringHandler(48, 2, out var shouldAppend);
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

	internal static bool DeleteResource(GameMakerData data, string resourceType, string? name) => RemoveResourceByName(data, resourceType, name);

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
		for (var i = list.Count - 1; i >= 0; i--)
		{
			var item = list[i];
			var itemName = item?.GetType().GetProperty("Name")?.GetValue(item);
			if (string.Equals(itemName?.GetType().GetProperty("Content")?.GetValue(itemName) as string, name, StringComparison.Ordinal))
			{
				if (item != null && isReferenced(data, item))
				{
					var handler = new LogInterpolatedStringHandler(69, 2, out var shouldAppend);
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
		for (var i = list.Count - 1; i >= 0; i--)
		{
			var item = list[i];
			var itemName = item?.GetType().GetProperty("Name")?.GetValue(item);
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
		for (var i = data.Extensions.Count - 1; i >= 0; i--)
		{
			if (string.Equals(data.Extensions[i]?.Name?.Content, name, StringComparison.Ordinal))
			{
				data.Extensions.RemoveAt(i);
				var productIdData = data.FORM.EXTN?.productIdData;
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
		for (var i = data.Scripts.Count - 1; i >= 0; i--)
		{
			var script = data.Scripts[i];
			if (string.Equals(script?.Name?.Content, name, StringComparison.Ordinal))
			{
				var codeName = script.Code?.Name?.Content;
				if (!string.IsNullOrEmpty(codeName) && ScriptCodeResolver.Resolve(data, name, codeName) != null)
				{
					var handler = new LogInterpolatedStringHandler(77, 1, out var shouldAppend);
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
		for (var i = data.Functions.Count - 1; i >= 0; i--)
		{
			if (string.Equals(data.Functions[i]?.Name?.Content, codeName, StringComparison.Ordinal))
			{
				data.Functions.RemoveAt(i);
				var handler = new LogInterpolatedStringHandler(63, 1, out var shouldAppend);
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
		foreach (var item in data.Code)
		{
			foreach (var item2 in EnumerateCodeTreeRecursive(item))
			{
				foreach (var item3 in item2.Instructions ?? [])
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
		var removedScripts = 0;
		var removedFunctions = 0;
		for (var i = data.Scripts.Count - 1; i >= 0; i--)
		{
			var script = data.Scripts[i];
			var scriptName = script?.Name?.Content;
			if (!string.IsNullOrWhiteSpace(scriptName) && script?.Code != null)
			{
				var liveCode = ScriptCodeResolver.Resolve(data, scriptName, script.Code.Name?.Content);
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
			var handler = new LogInterpolatedStringHandler(71, 2, out var shouldAppend);
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
		if (manifest?.Resources == null || !manifest.Resources.TryGetValue("Scripts", out var changes) || changes.Deleted == null || changes.Deleted.Count == 0)
		{
			return;
		}
		var removedScripts = 0;
		var removedFunctions = 0;
		foreach (var deletedScriptName in changes.Deleted)
		{
			if (string.IsNullOrWhiteSpace(deletedScriptName) || ScriptCodeResolver.Resolve(data, deletedScriptName, deletedScriptName) != null)
			{
				continue;
			}
			for (var i = data.Scripts.Count - 1; i >= 0; i--)
			{
				var script = data.Scripts[i];
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
			var handler = new LogInterpolatedStringHandler(66, 2, out var shouldAppend);
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
		foreach (var script in data.Scripts)
		{
			if (script?.Code == code)
			{
				return true;
			}
		}
		foreach (var item in data.GlobalInitScripts ?? [])
		{
			if (item?.Code == code)
			{
				return true;
			}
		}
		foreach (var item2 in data.GameEndScripts ?? [])
		{
			if (item2?.Code == code)
			{
				return true;
			}
		}
		foreach (var gameObject in data.GameObjects)
		{
			foreach (var item3 in gameObject?.Events ?? [])
			{
				foreach (var item4 in item3 ?? [])
				{
					foreach (var item5 in item4.Actions ?? [])
					{
						if (item5?.CodeId == code)
						{
							return true;
						}
					}
				}
			}
		}
		foreach (var room in data.Rooms)
		{
			if (room?.CreationCodeId == code)
			{
				return true;
			}
			foreach (var inst in room?.GameObjects ?? [])
			{
				if (inst?.CreationCode == code || inst?.PreCreateCode == code)
				{
					return true;
				}
			}
			foreach (var item6 in room?.Layers ?? [])
			{
				foreach (var inst2 in item6?.InstancesData?.Instances ?? [])
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
		foreach (var obj in data.GameObjects)
		{
			if (obj?.ParentId == gameObject)
			{
				return true;
			}
			var eventType = 0;
			while (obj != null && eventType < obj.Events.Count)
			{
				if (eventType == 4)
				{
					foreach (var evt in obj.Events[eventType])
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
		foreach (var room in data.Rooms)
		{
			foreach (var item in room?.Views ?? [])
			{
				if (item?.ObjectId == gameObject)
				{
					return true;
				}
			}
			foreach (var item2 in room?.GameObjects ?? [])
			{
				if (item2?.ObjectDefinition == gameObject)
				{
					return true;
				}
			}
			foreach (var item3 in room?.Layers ?? [])
			{
				foreach (var item4 in item3?.InstancesData?.Instances ?? [])
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
		foreach (var obj in data.GameObjects)
		{
			if (obj?.Sprite == sprite || obj?.TextureMaskId == sprite)
			{
				return true;
			}
		}
		foreach (var background in data.Backgrounds)
		{
			if (background?.GMS2ExportedSprite == sprite)
			{
				return true;
			}
		}
		foreach (var item in data.ParticleSystemEmitters ?? [])
		{
			if (item?.Sprite == sprite)
			{
				return true;
			}
		}
		foreach (var room in data.Rooms)
		{
			foreach (var tile in room?.Tiles ?? [])
			{
				if (tile != null && tile.spriteMode && tile.SpriteDefinition == sprite)
				{
					return true;
				}
			}
			foreach (var layer in room?.Layers ?? [])
			{
				if (layer?.BackgroundData?.Sprite == sprite)
				{
					return true;
				}
				foreach (var item2 in layer?.AssetsData?.Sprites ?? [])
				{
					if (item2?.Sprite == sprite)
					{
						return true;
					}
				}
				foreach (var tile2 in layer?.AssetsData?.LegacyTiles ?? [])
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
		foreach (var room in data.Rooms)
		{
			foreach (var item in room?.Backgrounds ?? [])
			{
				if (item?.BackgroundDefinition == background)
				{
					return true;
				}
			}
			foreach (var tile in room?.Tiles ?? [])
			{
				if (tile != null && !tile.spriteMode && tile.BackgroundDefinition == background)
				{
					return true;
				}
			}
			foreach (var item2 in room?.Layers ?? [])
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
		foreach (var room in data.Rooms)
		{
			foreach (var item in room?.Layers ?? [])
			{
				foreach (var item2 in item?.AssetsData?.TextItems ?? [])
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
		foreach (var item in data.Sequences ?? [])
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
		var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
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
			foreach (var item in enumerable)
			{
				if (ObjectGraphReferences(item, target, seen, depth + 1))
				{
					return true;
				}
			}
			return false;
		}
		var properties = node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
		foreach (var prop in properties)
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
		var currentHash = await HashService.ComputeFileHashAsync(dataPath).ConfigureAwait(false);
		var expectedOriginalHash = manifest?.Original?.Md5;
		if (!string.IsNullOrEmpty(expectedOriginalHash) && !currentHash.Equals(expectedOriginalHash, StringComparison.OrdinalIgnoreCase))
		{
			LogService.Warning("[PatchService] Xdelta fallback skipped because the original MD5 does not match");
			return false;
		}
		var extension = Path.GetExtension(pfs.ExactPatchPath ?? string.Empty);
		if (string.IsNullOrWhiteSpace(extension))
		{
			extension = ".xdelta";
		}
		var tempExactPatch = Path.Combine(TempRoot.Path, $"g3mlib_exact_apply_{Guid.NewGuid():N}{extension}");
		try
		{
			await File.WriteAllBytesAsync(tempExactPatch, pfs.ExactPatchBytes).ConfigureAwait(false);
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
			var exactResult = await new XDeltaService().ApplyPatchAsync(dataPath, tempExactPatch, outputPath).ConfigureAwait(false);
			if (!exactResult.Success)
			{
				LogService.Warning("[PatchService] Xdelta fallback failed: " + exactResult.Error);
				return false;
			}
			var expectedModifiedHash = manifest?.Modified?.Md5;
			if (!string.IsNullOrEmpty(expectedModifiedHash) && !(await HashService.ComputeFileHashAsync(outputPath).ConfigureAwait(false)).Equals(expectedModifiedHash, StringComparison.OrdinalIgnoreCase))
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
				pfs = await Task.Run(() => PatchFileSystem.LoadFromZip(patchPath, loadExactPayloads: true, skipAsmBackedGml: true)).ConfigureAwait(false);
				manifest ??= pfs.Manifest;
			}
			catch (Exception ex)
			{
				LogService.Warning("[PatchService] Xdelta fallback could not be loaded: " + ex.Message);
				return false;
			}
		}
		return await TryApplyXdeltaFallbackAsync(dataPath, outputPath, pfs, manifest).ConfigureAwait(false);
	}

	private static void ApplyDeferredGlobalScripts(GameMakerData data, string globalScriptsJson)
	{
		var tempDir = Path.Combine(TempRoot.Path, $"g3mlib_globals_{Guid.NewGuid():N}");
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
		foreach (var child in code.ChildEntries)
		{
			foreach (var item in EnumerateCodeTreeRecursive(child))
			{
				yield return item;
			}
		}
	}

	private static Dictionary<string, GameMakerCode> BuildArchiveCodeLookup(GameMakerData data)
	{
		var lookup = new Dictionary<string, GameMakerCode>(StringComparer.Ordinal);
		var topLevelByName = new Dictionary<string, List<GameMakerCode>>(StringComparer.Ordinal);
		foreach (var code in data.Code)
		{
			var name = code?.Name?.Content;
			if (code != null && code.ParentEntry == null && !string.IsNullOrEmpty(name))
			{
				if (!topLevelByName.TryGetValue(name, out var list))
				{
					list = topLevelByName[name] = [];
				}
				list.Add(code);
			}
		}
		Dictionary<GameMakerCode, Dictionary<string, List<GameMakerCode>>> childByParentAndName = [];
		foreach (var descriptor in CodeEntryArchiveIdentity.DescribeEntries(data))
		{
			GameMakerCode? code2 = null;
			if (descriptor.ParentArchiveKey != null)
			{
				if (!lookup.TryGetValue(descriptor.ParentArchiveKey, out var parent))
				{
					continue;
				}
				if (!childByParentAndName.TryGetValue(parent, out var childrenByName))
				{
					childrenByName = new Dictionary<string, List<GameMakerCode>>(StringComparer.Ordinal);
					foreach (var child in parent.ChildEntries)
					{
						var childName = child?.Name?.Content;
						if (child != null && !string.IsNullOrEmpty(childName))
						{
							if (!childrenByName.TryGetValue(childName, out var children))
							{
								children = childrenByName[childName] = [];
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
			else if (topLevelByName.TryGetValue(descriptor.LogicalName, out var matches2) && descriptor.Occurrence > 0 && descriptor.Occurrence <= matches2.Count)
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

	private static Dictionary<string, (string LogicalName, int Occurrence, string? ParentArchiveKey)> ParseTargetCodeEntryMetadata(JsonElement root, Dictionary<string, string>? fallbackLogicalNames)
	{
		var map = new Dictionary<string, (string, int, string)>(StringComparer.Ordinal);
		if (!root.TryGetProperty("codeEntries", out var ceArray) || ceArray.ValueKind != JsonValueKind.Array)
		{
			return map;
		}
		foreach (var ce in ceArray.EnumerateArray())
		{
			if (ce.ValueKind == JsonValueKind.Object)
			{
				var key = ce.TryGetProperty("key", out var keyElem) ? keyElem.GetString() : null;
				var name = ce.TryGetProperty("name", out var nameElem) ? nameElem.GetString() : null;
				var occurrence = (!ce.TryGetProperty("occurrence", out var occElem)) ? 1 : occElem.GetInt32();
				var parentArchiveKey = ce.TryGetProperty("parent", out var parentElem) ? parentElem.GetString() : null;
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
				var name2 = ce.GetString();
				if (!string.IsNullOrWhiteSpace(name2))
				{
					var key2 = fallbackLogicalNames?.FirstOrDefault(x => x.Value == name2).Key ?? name2;
					var occurrence2 = map.Values.Count(((string LogicalName, int Occurrence, string ParentArchiveKey) x) => x.LogicalName == name2) + 1;
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
		var archiveLookup = BuildArchiveCodeLookup(data);
		var used = new HashSet<GameMakerCode>(ReferenceEqualityComparer.Instance);
		var reordered = new List<GameMakerCode>(data.Code.Count);
		foreach (var entryKey in targetCodeEntriesByKey.Keys)
		{
			if (archiveLookup.TryGetValue(entryKey, out var code) && code != null && used.Add(code))
			{
				reordered.Add(code);
			}
		}
		foreach (var code2 in data.Code)
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
		foreach (var code3 in reordered)
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
		var dictionary = BuildArchiveCodeLookup(data);
		var seen = new HashSet<GameMakerCode>(ReferenceEqualityComparer.Instance);
		List<GameMakerCode> toRemove = [];
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
		var removeSet = new HashSet<GameMakerCode>(toRemove, ReferenceEqualityComparer.Instance);
		foreach (var code2 in toRemove)
		{
			code2.ParentEntry?.ChildEntries.Remove(code2);
			code2.ParentEntry = null;
			for (var i = code2.ChildEntries.Count - 1; i >= 0; i--)
			{
				if (removeSet.Contains(code2.ChildEntries[i]))
				{
					code2.ChildEntries.RemoveAt(i);
				}
			}
			for (var i2 = data.Scripts.Count - 1; i2 >= 0; i2--)
			{
				var script = data.Scripts[i2];
				if (script?.Code == code2 || string.Equals(script?.Name?.Content, code2.Name?.Content, StringComparison.Ordinal))
				{
					data.Scripts.RemoveAt(i2);
				}
			}
			for (var i3 = data.CodeLocals.Count - 1; i3 >= 0; i3--)
			{
				if (data.CodeLocals[i3].Name == code2.Name || string.Equals(data.CodeLocals[i3].Name?.Content, code2.Name?.Content, StringComparison.Ordinal))
				{
					data.CodeLocals.RemoveAt(i3);
				}
			}
			data.Code.Remove(code2);
		}
		var handler = new LogInterpolatedStringHandler(66, 1, out var shouldAppend);
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
			using var doc = JsonDocument.Parse(variablesFunctionsJson);
			if (HasCompleteCodeEntryMetadata(doc.RootElement))
			{
				Dictionary<string, (string, int, string)> targetCodeEntriesByKey = ParseTargetCodeEntryMetadata(doc.RootElement, null);
				PruneCodeEntriesOutsideTargetMetadata(data, targetCodeEntriesByKey);
				PruneExcessCodeAndScriptsByTargetNames(data, targetCodeEntriesByKey.Values.Select(((string LogicalName, int Occurrence, string ParentArchiveKey) x) => x.LogicalName));
			}
		}
		catch (Exception ex)
		{
			var handler = new LogInterpolatedStringHandler(46, 1, out var shouldAppend);
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
		var remainingByName = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var name in targetCodeNames)
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
		var removeCodes = new HashSet<GameMakerCode>(ReferenceEqualityComparer.Instance);
		for (var i = data.Code.Count - 1; i >= 0; i--)
		{
			var code = data.Code[i];
			var name2 = code?.Name?.Content;
			if (code != null && !string.IsNullOrEmpty(name2))
			{
				var remaining = remainingByName.GetValueOrDefault(name2);
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
		var removedScripts = 0;
		if (removeCodes.Count <= 0)
		{
			return;
		}
		for (var i2 = data.Scripts.Count - 1; i2 >= 0; i2--)
		{
			var script = data.Scripts[i2];
			var code2 = script?.Code;
			var scriptName = script?.Name?.Content;
			if (code2 != null && removeCodes.Contains(code2) || !string.IsNullOrEmpty(scriptName) && removeCodes.Any(c => string.Equals(c.Name?.Content, scriptName, StringComparison.Ordinal)))
			{
				data.Scripts.RemoveAt(i2);
				removedScripts++;
			}
		}
		foreach (var code3 in removeCodes)
		{
			code3.ParentEntry?.ChildEntries.Remove(code3);
			code3.ParentEntry = null;
		}
		var handler = new LogInterpolatedStringHandler(65, 2, out var shouldAppend);
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
		var compactPath = Path.Combine(helpersDir, "code_patch_metadata.json");
		if (pfs.FileExists(compactPath))
		{
			return pfs.ReadAllText(compactPath);
		}
		var legacyPath = Path.Combine(helpersDir, "variables_functions.json");
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
			["sprites"] = [.. data.Sprites.Select(s => s?.Name?.Content ?? "")],
			["objects"] = [.. data.GameObjects.Select(o => o?.Name?.Content ?? "")],
			["sounds"] = [.. data.Sounds.Select(s => s?.Name?.Content ?? "")],
			["backgrounds"] = [.. data.Backgrounds.Select(b => b?.Name?.Content ?? "")],
			["paths"] = [.. data.Paths.Select(p => p?.Name?.Content ?? "")],
			["scripts"] = [.. data.Scripts.Select(s => s?.Name?.Content ?? "")],
			["fonts"] = [.. data.Fonts.Select(f => f?.Name?.Content ?? "")],
			["rooms"] = [.. data.Rooms.Select(r => r?.Name?.Content ?? "")],
			["timelines"] = [.. data.Timelines.Select(t => t?.Name?.Content ?? "")],
			["shaders"] = [.. data.Shaders.Select(s => s?.Name?.Content ?? "")],
			["audiogroups"] = [.. data.AudioGroups.Select(a => a?.Name?.Content ?? "")]
		};
	}

	private static Dictionary<int, byte[]> CaptureUntouchedEmbeddedTextures(GameMakerData data, PatchFileSystem pfs)
	{
		var touched = GetTouchedEmbeddedTextureIndices(pfs);
		Dictionary<int, byte[]> result = [];
		var gm2022_5 = data.IsVersionAtLeast(2022u, 5u);
		for (var i = 0; i < data.EmbeddedTextures.Count; i++)
		{
			if (touched.Contains(i))
			{
				continue;
			}
			var image = data.EmbeddedTextures[i]?.TextureData?.Image;
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
		HashSet<int> result = [];
		foreach (var path in pfs.GetAllFilePaths())
		{
			if (!path.StartsWith("EmbeddedTextures/", StringComparison.OrdinalIgnoreCase) && !path.StartsWith(pfs.HelpersPrefix + "/EmbeddedTextures/", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
			for (var i = 0; i < parts.Length; i++)
			{
				if (parts[i].StartsWith("texture_", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[i]["texture_".Length..], out var index))
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
		var restored = 0;
		var gm2022_5 = data.IsVersionAtLeast(2022u, 5u);
		foreach (var (index, imageBytes) in snapshot)
		{
			if (index < 0 || index >= data.EmbeddedTextures.Count || data.EmbeddedTextures[index]?.TextureData == null)
			{
				continue;
			}
			try
			{
				using var stream = new MemoryStream(imageBytes, writable: false);
				using var reader = new FileBinaryReader(stream);
				data.EmbeddedTextures[index].TextureData.Image = GMImage.FromBinaryReader(reader, stream.Length, gm2022_5);
				restored++;
			}
			catch
			{
			}
		}
		if (restored > 0)
		{
			var handler = new LogInterpolatedStringHandler(51, 1, out var shouldAppend);
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
		var referenced = new HashSet<GameMakerEmbeddedTexture>(ReferenceEqualityComparer.Instance);
		foreach (var tpi in data.TexturePageItems)
		{
			if (tpi?.TexturePage != null)
			{
				referenced.Add(tpi.TexturePage);
			}
		}
		var oldCount = data.EmbeddedTextures.Count;
		while (data.EmbeddedTextures.Count > 0)
		{
			var embeddedTextures = data.EmbeddedTextures;
			var texture = embeddedTextures[embeddedTextures.Count - 1];
			if (texture != null && referenced.Contains(texture))
			{
				break;
			}
			data.EmbeddedTextures.RemoveAt(data.EmbeddedTextures.Count - 1);
		}
		if (data.EmbeddedTextures.Count != oldCount)
		{
			var handler = new LogInterpolatedStringHandler(67, 2, out var shouldAppend);
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
			using var doc = JsonDocument.Parse(spriteFrameMapBytes);
			using var stream = new MemoryStream();
			using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
			{
				Indented = true
			}))
			{
				writer.WriteStartObject();
				foreach (var prop in doc.RootElement.EnumerateObject())
				{
					if (removeFontRelinks && prop.NameEquals("fonts"))
					{
						continue;
					}
					if (prop.NameEquals("sprites"))
					{
						writer.WritePropertyName(prop.Name);
						writer.WriteStartObject();
						foreach (var item in prop.Value.EnumerateObject())
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
		var repairedTpi = 0;
		var repairedTextures = 0;
		HashSet<GameMakerTexturePageItem> tpiSet = [.. data.TexturePageItems];
		HashSet<GameMakerEmbeddedTexture> textureSet = [.. data.EmbeddedTextures];
		foreach (var font in data.Fonts)
		{
			var tpi = font?.Texture;
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
			var handler = new LogInterpolatedStringHandler(76, 2, out var shouldAppend);
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
		var reordered = ReorderLocalChildFunctions(data);
		bool shouldAppend;
		if (reordered > 0)
		{
			var handler = new LogInterpolatedStringHandler(69, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[PatchService] Restored local child Function order for ");
				handler.AppendFormatted(reordered);
				handler.AppendLiteral(" table slot(s)");
			}
			LogService.Log(ref handler);
		}
		var codeByName = new Dictionary<string, GameMakerCode>(StringComparer.Ordinal);
		foreach (var code in data.Code)
		{
			var name = code?.Name?.Content;
			if (!string.IsNullOrEmpty(name) && code != null)
			{
				codeByName[name] = code;
			}
		}
		var functionByName = new Dictionary<string, GameMakerFunction>(StringComparer.Ordinal);
		foreach (var function in data.Functions)
		{
			var name2 = function?.Name?.Content;
			if (!string.IsNullOrEmpty(name2) && function != null && !functionByName.ContainsKey(name2))
			{
				functionByName[name2] = function;
			}
		}
		Dictionary<GameMakerCode, Dictionary<string, GameMakerFunction?>> localMaps = [];
		Dictionary<GameMakerCode, Dictionary<string, GameMakerFunction>> localOrdinalMaps = [];
		var repaired = 0;
		foreach (var code2 in data.Code)
		{
			if (code2 == null)
			{
				continue;
			}
			var owner = code2.ParentEntry ?? code2;
			if (owner.ChildEntries == null || owner.ChildEntries.Count == 0)
			{
				continue;
			}
			var localMap = GetLocalMap(owner);
			if (localMap.Count == 0)
			{
				continue;
			}
			foreach (var instruction in code2.Instructions ?? [])
			{
				var currentFunction = instruction.ValueFunction;
				var currentName = currentFunction?.Name?.Content;
				if (!string.IsNullOrEmpty(currentName) && FunctionNameUtil.IsLocalChildFunctionName(currentName) && (!codeByName.TryGetValue(currentName, out var referencedCode) || referencedCode.ParentEntry != owner))
				{
					if ((!localMap.TryGetValue(FunctionNameUtil.NormalizeLocalChildFunctionName(currentName), out var replacement) || replacement == null) && FunctionNameUtil.TryGetLocalChildOrdinalFamily(currentName, out var family, out var ordinal))
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
			var handler2 = new LogInterpolatedStringHandler(83, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler2.AppendLiteral("[PatchService] Re-linked ");
				handler2.AppendFormatted(repaired);
				handler2.AppendLiteral(" local child Function references to their owning CodeEntry");
			}
			LogService.Log(ref handler2);
		}
		var pruned = PruneDetachedLocalStructFunctions(data, codeByName);
		if (pruned > 0)
		{
			var handler3 = new LogInterpolatedStringHandler(61, 1, out shouldAppend);
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
			if (localMaps.TryGetValue(gameMakerCode, out var cached))
			{
				return cached;
			}
			return localMaps[gameMakerCode] = DataIntegrityService.BuildLocalChildFunctionMap(gameMakerCode, functionByName);
		}
		Dictionary<string, GameMakerFunction> GetLocalOrdinalMap(GameMakerCode gameMakerCode)
		{
			if (localOrdinalMaps.TryGetValue(gameMakerCode, out var cached))
			{
				return cached;
			}
			return localOrdinalMaps[gameMakerCode] = DataIntegrityService.BuildLocalChildOrdinalMap(data, gameMakerCode, functionByName);
		}
	}

	private static int ReorderLocalChildFunctions(GameMakerData data)
	{
		var functionByName = new Dictionary<string, GameMakerFunction>(StringComparer.Ordinal);
		foreach (var function in data.Functions)
		{
			var name = function?.Name?.Content;
			if (!string.IsNullOrEmpty(name) && function != null)
			{
				functionByName.TryAdd(name, function);
			}
		}
		var reordered = 0;
		foreach (var parent in data.Code)
		{
			if (parent?.ParentEntry != null || parent?.ChildEntries == null)
			{
				continue;
			}
			List<GameMakerFunction> expected = [];
			foreach (var childEntry in parent.ChildEntries)
			{
				var name2 = childEntry?.Name?.Content;
				if (!string.IsNullOrEmpty(name2) && FunctionNameUtil.IsLocalChildFunctionName(name2) && functionByName.TryGetValue(name2, out var function2))
				{
					expected.Add(function2);
				}
			}
			if (expected.Count < 2)
			{
				continue;
			}
			HashSet<GameMakerFunction> expectedSet = [.. expected];
			List<int> slots = [.. from index2 in Enumerable.Range(0, data.Functions.Count)
							   where expectedSet.Contains(data.Functions[index2])
							   select index2];
			if (slots.Count != expected.Count)
			{
				continue;
			}
			for (var index = 0; index < slots.Count; index++)
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

	private static int PruneDetachedLocalStructFunctions(GameMakerData data, Dictionary<string, GameMakerCode> codeByName)
	{
		if (data.Functions == null)
		{
			return 0;
		}
		HashSet<GameMakerFunction> referenced = [];
		foreach (var code in data.Code ?? [])
		{
			if (code == null)
			{
				continue;
			}
			foreach (var entry in new GameMakerCode[1] { code }.Concat(code.ChildEntries ?? []))
			{
				if (entry?.Instructions == null)
				{
					continue;
				}
				foreach (var instruction in entry.Instructions)
				{
					if (instruction.ValueFunction != null)
					{
						referenced.Add(instruction.ValueFunction);
					}
				}
			}
		}
		var liveLocalSignatures = new HashSet<string>(StringComparer.Ordinal);
		foreach (var code2 in data.Code ?? [])
		{
			if (code2?.ParentEntry != null && !string.IsNullOrEmpty(code2.Name?.Content) && FunctionNameUtil.IsLocalChildFunctionName(code2.Name.Content))
			{
				liveLocalSignatures.Add(FunctionNameUtil.NormalizeLocalChildFunctionName(code2.Name.Content));
			}
		}
		var removed = 0;
		for (var i = data.Functions.Count - 1; i >= 0; i--)
		{
			var function = data.Functions[i];
			var name = function?.Name?.Content;
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
			return [];
		}
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		var ctx = new GlobalDecompileContext(data);
		foreach (var code in data.Code)
		{
			var codeName = code?.Name?.Content;
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
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var key in pfs.GmlEntries.Keys.Concat(pfs.AsmEntries.Keys))
		{
			names.Add(key);
			if (pfs.CodeEntryLogicalNames.TryGetValue(key, out var logicalName))
			{
				names.Add(logicalName);
			}
		}
		return names;
	}

	private static bool RecompileUntouchedCodeForAssetOrderShifts(GameMakerData data, Dictionary<string, List<string>> originalAssetNames, Dictionary<string, string> originalGmlByCodeName, HashSet<string> patchCodeEntryNames, string helpersDir, string variablesFunctionsContent)
	{
		var shiftedNames = GetShiftedAssetNames(originalAssetNames, CaptureAssetNamesForCodeRemap(data));
		if (shiftedNames.Count == 0)
		{
			return false;
		}
		var gmlToRecompile = new Dictionary<string, string>(StringComparer.Ordinal);
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
		var handler = new LogInterpolatedStringHandler(101, 2, out var shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[PatchService] Recompiling ");
			handler.AppendFormatted(gmlToRecompile.Count);
			handler.AppendLiteral(" untouched code entries for shifted asset indices (");
			handler.AppendFormatted(shiftedNames.Count);
			handler.AppendLiteral(" shifted asset name(s))");
		}
		LogService.Log(ref handler);
		ImportCodeEntriesDirect(data, gmlToRecompile, [], null, null, helpersDir, out var gmlCompilationRan, variablesFunctionsContent);
		return gmlCompilationRan;
	}

	private static HashSet<string> GetShiftedAssetNames(Dictionary<string, List<string>> before, Dictionary<string, List<string>> after)
	{
		var shifted = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (section, beforeNames) in before)
		{
			if (!after.TryGetValue(section, out var afterNames))
			{
				continue;
			}
			var afterIndexByName = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
			for (var i = 0; i < afterNames.Count; i++)
			{
				var name = afterNames[i];
				if (IsSafeAssetNameForCodeRemap(name))
				{
					if (!afterIndexByName.TryGetValue(name, out var queue))
					{
						queue = afterIndexByName[name] = new Queue<int>();
					}
					queue.Enqueue(i);
				}
			}
			for (var j = 0; j < beforeNames.Count; j++)
			{
				var name2 = beforeNames[j];
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
		foreach (var name in shiftedNames)
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
		var searchIndex = 0;
		while (searchIndex < text.Length)
		{
			var matchIndex = text.IndexOf(identifier, searchIndex, StringComparison.Ordinal);
			if (matchIndex < 0)
			{
				return false;
			}
			var beforeIndex = matchIndex - 1;
			var afterIndex = matchIndex + identifier.Length;
			var num = beforeIndex < 0 || !IsIdentifierChar(text[beforeIndex]);
			var hasIdentifierBoundaryAfter = afterIndex >= text.Length || !IsIdentifierChar(text[afterIndex]);
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
		var sw = Stopwatch.StartNew();
		var phaseSw = new Stopwatch();
		var handler = new LogInterpolatedStringHandler(66, 2, out var shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[ImportCodeEntries] Starting hybrid import: ");
			handler.AppendFormatted(gmlEntries.Count);
			handler.AppendLiteral(" GML + ");
			handler.AppendFormatted(asmEntries.Count);
			handler.AppendLiteral(" ASM entries...");
		}
		LogService.Log(ref handler);
		var handler2 = new LogInterpolatedStringHandler(55, 3, out shouldAppend);
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
			var varFuncPath = Path.Combine(helpersDir, "variables_functions.json");
			if (!File.Exists(varFuncPath))
			{
				throw new FileNotFoundException("variables_functions.json was not found", varFuncPath);
			}
			vfContent = File.ReadAllText(varFuncPath, Encoding.UTF8);
		}
		using var vfDoc = JsonDocument.Parse(vfContent);
		var root = vfDoc.RootElement;
		Dictionary<string, (string, int, string)> targetCodeEntriesByKey = ParseTargetCodeEntryMetadata(root, codeEntryLogicalNames);
		var hasCompleteCodeEntryMetadata = HasCompleteCodeEntryMetadata(root);
		var hasFunctionVariableTables = root.TryGetProperty("functions", out var functionsElement) && functionsElement.ValueKind == JsonValueKind.Array && root.TryGetProperty("variables", out var variablesElement) && variablesElement.ValueKind == JsonValueKind.Array;
		var targetTopLevelNamesByKey = new Dictionary<string, string>(StringComparer.Ordinal);
		var targetEntryNames = new HashSet<string>(StringComparer.Ordinal);
		var targetEntryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
		var targetChildEntriesByParent = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);
		var isAssetShiftRecompileOnly = asmEntries.Count == 0 && asmEntryPaths == null && codeEntryLogicalNames == null && objectEventsContentOverride == null;
		string key;
		(string, int, string) value;
		foreach (var item in targetCodeEntriesByKey)
		{
			item.Deconstruct(out key, out value);
			var entryKey = key;
			var meta = value;
			if (meta.Item3 != null)
			{
				if (!targetChildEntriesByParent.TryGetValue(meta.Item3, out var childList))
				{
					childList = [];
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
		var allGmlCoveredByAsm = !isAssetShiftRecompileOnly && asmEntries.Count > 0 && gmlEntries.Keys.All(asmEntries.ContainsKey);
		var preCompileEntries = new HashSet<string>(data.Code.Count);
		if (!allGmlCoveredByAsm)
		{
			foreach (var c in data.Code)
			{
				if (c != null && c.Name?.Content != null)
				{
					preCompileEntries.Add(c.Name.Content);
				}
			}
		}
		GlobalDecompileContext? ctx = null;
		CodeImportGroup? importGroup = null;
		Dictionary<string, GameMakerCode> codeLookup = [];
		Dictionary<string, GameMakerCode> archiveCodeLookup = [];
		Dictionary<string, (GameMakerGameObject, int)> goLookup = [];
		Dictionary<string, string> collisionTargetsByCode = [];
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
			foreach (var item2 in data.Code)
			{
				foreach (var nested in EnumerateCodeTreeRecursive(item2))
				{
					if (nested != null && nested.Name?.Content != null)
					{
						codeLookup.TryAdd(nested.Name.Content, nested);
					}
				}
			}
			archiveCodeLookup = BuildArchiveCodeLookup(data);
			goLookup = new Dictionary<string, (GameMakerGameObject, int)>(data.GameObjects.Count);
			for (var i = 0; i < data.GameObjects.Count; i++)
			{
				var go = data.GameObjects[i];
				if (go != null && go.Name?.Content != null)
				{
					goLookup.TryAdd(go.Name.Content, (go, i));
				}
			}
			collisionTargetsByCode = LoadCollisionTargetsByCode(helpersDir, objectEventsContentOverride);
		}
		var regularCount = 0;
		var collisionCount = 0;
		var asmBackedCompileSkippedCount = allGmlCoveredByAsm ? gmlEntries.Count : 0;
		List<(string, string, bool)> gmlOnlyEntries = [];
		string value2;
		if (!allGmlCoveredByAsm)
		{
			foreach (var gmlEntry in gmlEntries)
			{
				gmlEntry.Deconstruct(out key, out value2);
				var entryKey2 = key;
				var gmlCode = value2;
				var codeName = targetTopLevelNamesByKey.GetValueOrDefault(entryKey2, codeEntryLogicalNames?.GetValueOrDefault(entryKey2) ?? entryKey2);
				if (!isAssetShiftRecompileOnly && asmEntries.ContainsKey(entryKey2))
				{
					asmBackedCompileSkippedCount++;
					continue;
				}
				if (codeName.Contains("_Collision_", StringComparison.Ordinal))
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
						var handler3 = new LogInterpolatedStringHandler(38, 2, out shouldAppend);
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
					var handler4 = new LogInterpolatedStringHandler(31, 2, out shouldAppend);
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
			var handler5 = new LogInterpolatedStringHandler(65, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler5.AppendLiteral("[ImportCodeEntries] Skipped GML compile for ");
				handler5.AppendFormatted(asmBackedCompileSkippedCount);
				handler5.AppendLiteral(" ASM-backed entrie(s)");
			}
			LogService.Log(ref handler5);
		}
		var handler6 = new LogInterpolatedStringHandler(61, 3, out shouldAppend);
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
			List<GameMakerCode> entriesToDelete = [];
			var seenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (var c2 in data.Code)
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
				var seen = seenCounts.GetValueOrDefault(c2.Name.Content) + 1;
				seenCounts[c2.Name.Content] = seen;
				if (seen > targetEntryCounts.GetValueOrDefault(c2.Name.Content))
				{
					entriesToDelete.Add(c2);
				}
			}
			if (entriesToDelete.Count > 0)
			{
				var handler7 = new LogInterpolatedStringHandler(51, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler7.AppendLiteral("[ImportCodeEntries] Removing ");
					handler7.AppendFormatted(entriesToDelete.Count);
					handler7.AppendLiteral(" stale code entries...");
				}
				LogService.Log(ref handler7);
				var deletedEvents = 0;
				foreach (var code in entriesToDelete)
				{
					List<GameMakerCode> children = [];
					foreach (var c3 in data.Code)
					{
						if (c3?.ParentEntry == code)
						{
							children.Add(c3);
						}
					}
					foreach (var child in children)
					{
						data.Code.Remove(child);
						for (var i2 = data.CodeLocals.Count - 1; i2 >= 0; i2--)
						{
							if (data.CodeLocals[i2].Name == child.Name)
							{
								data.CodeLocals.RemoveAt(i2);
								break;
							}
						}
					}
					foreach (var obj in data.GameObjects)
					{
						if (obj == null)
						{
							continue;
						}
						for (var evtType = 0; evtType < obj.Events.Count; evtType++)
						{
							var evtList = obj.Events[evtType];
							for (var i3 = evtList.Count - 1; i3 >= 0; i3--)
							{
								if (evtList[i3].Actions.Count > 0 && evtList[i3].Actions[0].CodeId == code)
								{
									evtList.RemoveAt(i3);
									deletedEvents++;
								}
							}
						}
					}
					foreach (var script in data.Scripts)
					{
						if (script?.Code == code)
						{
							script.Code = null;
						}
					}
					for (var i4 = data.GlobalInitScripts.Count - 1; i4 >= 0; i4--)
					{
						if (data.GlobalInitScripts[i4].Code == code)
						{
							data.GlobalInitScripts.RemoveAt(i4);
						}
					}
					for (var i5 = data.CodeLocals.Count - 1; i5 >= 0; i5--)
					{
						if (data.CodeLocals[i5].Name == code.Name)
						{
							data.CodeLocals.RemoveAt(i5);
							break;
						}
					}
					data.Code.Remove(code);
				}
				var handler8 = new LogInterpolatedStringHandler(51, 3, out shouldAppend);
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
		var touchesObjectEventCode = false;
		foreach (var entryKey3 in gmlEntries.Keys)
		{
			var codeName2 = targetTopLevelNamesByKey.GetValueOrDefault(entryKey3, codeEntryLogicalNames?.GetValueOrDefault(entryKey3) ?? entryKey3);
			if (codeName2.Contains("_Collision_", StringComparison.Ordinal) || codeName2.StartsWith("gml_Object_", StringComparison.Ordinal))
			{
				touchesObjectEventCode = true;
				break;
			}
		}
		if (!touchesObjectEventCode)
		{
			foreach (var entryKey4 in asmEntries.Keys)
			{
				var codeName3 = codeEntryLogicalNames?.GetValueOrDefault(entryKey4) ?? targetTopLevelNamesByKey.GetValueOrDefault(entryKey4, entryKey4);
				if (codeName3.Contains("_Collision_", StringComparison.Ordinal) || codeName3.StartsWith("gml_Object_", StringComparison.Ordinal))
				{
					touchesObjectEventCode = true;
					break;
				}
			}
		}
		var hasQueuedGmlCompilation = regularCount + collisionCount > 0;
		if (touchesObjectEventCode && hasQueuedGmlCompilation)
		{
			LogService.Log("[ImportCodeEntries] Taking event snapshot before compilation...");
		}
		var topLevelEntrySnapshot = new Dictionary<string, (GameMakerCode, GameMakerCodeLocals, GameMakerScript)>(StringComparer.Ordinal);
		var childEntrySnapshot = new Dictionary<string, (GameMakerCode, GameMakerCodeLocals, GameMakerScript, string)>(StringComparer.Ordinal);
		List<(GameMakerCode, string)> pendingChildReattachments = [];
		if (hasQueuedGmlCompilation)
		{
			var scriptByCode = new Dictionary<GameMakerCode, GameMakerScript>(data.Scripts.Count);
			foreach (var s in data.Scripts)
			{
				if (s?.Code != null)
				{
					scriptByCode.TryAdd(s.Code, s);
				}
			}
			var codeLocalsByName = new Dictionary<GameMakerString, GameMakerCodeLocals>(data.CodeLocals.Count);
			foreach (var cl in data.CodeLocals)
			{
				if (cl.Name != null)
				{
					codeLocalsByName.TryAdd(cl.Name, cl);
				}
			}
			var snapshotArchiveLookup = BuildArchiveCodeLookup(data);
			foreach (var descriptor in CodeEntryArchiveIdentity.DescribeEntries(data))
			{
				if (descriptor.ParentArchiveKey == null && snapshotArchiveLookup.TryGetValue(descriptor.ArchiveKey, out var top) && top != null && top.Name?.Content != null)
				{
					scriptByCode.TryGetValue(top, out var script2);
					codeLocalsByName.TryGetValue(top.Name, out var locals);
					topLevelEntrySnapshot[descriptor.ArchiveKey] = (top, locals, script2);
				}
			}
			foreach (var descriptor2 in CodeEntryArchiveIdentity.DescribeEntries(data))
			{
				if (descriptor2.ParentArchiveKey != null && snapshotArchiveLookup.TryGetValue(descriptor2.ArchiveKey, out var nested2) && nested2 != null && nested2.Name?.Content != null)
				{
					scriptByCode.TryGetValue(nested2, out var script3);
					codeLocalsByName.TryGetValue(nested2.Name, out var locals2);
					childEntrySnapshot[descriptor2.ArchiveKey] = (nested2, locals2, script3, descriptor2.ParentArchiveKey);
				}
			}
		}
		Dictionary<string, List<(int, uint, string)>> eventSnapshot = [];
		if (touchesObjectEventCode && hasQueuedGmlCompilation)
		{
			var objectNameCountsForEvents = BuildObjectNameCounts(data);
			for (var objIndex = 0; objIndex < data.GameObjects.Count; objIndex++)
			{
				var obj2 = data.GameObjects[objIndex];
				if (obj2 == null || obj2.Name?.Content == null)
				{
					continue;
				}
				List<(int, uint, string)> events = [];
				for (var j = 0; j < obj2.Events.Count; j++)
				{
					foreach (var evt in obj2.Events[j])
					{
						var evtCodeName = (evt.Actions.Count > 0) ? (evt.Actions[0].CodeId?.Name?.Content ?? "") : "";
						events.Add((j, evt.EventSubtype, evtCodeName));
					}
				}
				eventSnapshot[GetObjectEventKey(obj2.Name.Content, objIndex, objectNameCountsForEvents)] = events;
			}
		}
		if (hasQueuedGmlCompilation)
		{
			var handler9 = new LogInterpolatedStringHandler(46, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler9.AppendLiteral("[ImportCodeEntries] Compiling ");
				handler9.AppendFormatted(regularCount + collisionCount);
				handler9.AppendLiteral(" code entries...");
			}
			LogService.Log(ref handler9);
			gmlCompilationRan = true;
			var compileResult = importGroup.Import(throwOnFailedCompile: false);
			if (!compileResult.Successful)
			{
				var handler10 = new LogInterpolatedStringHandler(41, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler10.AppendLiteral("[ImportCodeEntries] Compilation warning: ");
					handler10.AppendFormatted(compileResult.PrintAllErrors(codeEntryNames: true));
				}
				LogService.Log(ref handler10);
				var retryFailures = RetryGmlOnlyCodeEntriesIndividually(data, gmlOnlyEntries, collisionTargetsByCode);
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
		var handler11 = new LogInterpolatedStringHandler(51, 1, out shouldAppend);
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
			var currentArchiveLookupAfterCompile = BuildArchiveCodeLookup(data);

			var currentEntries = new HashSet<string>(BuildArchiveCodeLookup(data).Keys, StringComparer.Ordinal);
			HashSet<GameMakerCode> currentCodeObjects = [.. data.Code];
			var currentScripts = new HashSet<string>(data.Scripts.Count);
			foreach (var s2 in data.Scripts)
			{
				if (s2 != null && s2.Name?.Content != null)
				{
					currentScripts.Add(s2.Name.Content);
				}
			}
			var restoredTopLevel = 0;
			foreach (var item3 in topLevelEntrySnapshot)
			{
				item3.Deconstruct(out value2, out value3);
				var entryKey5 = value2;
				var snapshot = value3;
				if (targetCodeEntriesByKey.Count > 0 && !targetCodeEntriesByKey.ContainsKey(entryKey5) || currentEntries.Contains(entryKey5))
				{
					continue;
				}
				var tuple2 = snapshot;
				var snapCode = tuple2.Item1;
				var snapLocals = tuple2.Item2;
				var snapScript = tuple2.Item3;
				snapCode.ParentEntry = null;
				if (!currentCodeObjects.Contains(snapCode))
				{
					data.Code.Add(snapCode);
					currentCodeObjects.Add(snapCode);
				}
				if (snapLocals != null)
				{
					var localsExist = false;
					foreach (var codeLocal in data.CodeLocals)
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
				var handler12 = new LogInterpolatedStringHandler(80, 1, out shouldAppend);
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
			var restored = 0;
			var repairedParenting = 0;
			foreach (var item4 in childEntrySnapshot)
			{
				item4.Deconstruct(out value2, out value4);
				var entryKey6 = value2;
				var snapshot2 = value4;
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
					var localsExist2 = false;
					foreach (var codeLocal2 in data.CodeLocals)
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
					var handler13 = new LogInterpolatedStringHandler(39, 2, out shouldAppend);
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
					var handler14 = new LogInterpolatedStringHandler(30, 2, out shouldAppend);
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
				var handler15 = new LogInterpolatedStringHandler(69, 1, out shouldAppend);
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
				var handler16 = new LogInterpolatedStringHandler(92, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler16.AppendLiteral("[ImportCodeEntries] Restored ");
					handler16.AppendFormatted(restored);
					handler16.AppendLiteral(" child entries as standalone (detached from recompiled parents)");
				}
				LogService.Log(ref handler16);
			}
			currentArchiveLookupAfterCompile = BuildArchiveCodeLookup(data);

		}
		if (hasQueuedGmlCompilation)
		{
			List<GameMakerCode> spurious = [];
			foreach (var c4 in data.Code)
			{
				if (c4 != null && c4.Name?.Content != null && c4.ParentEntry == null && !preCompileEntries.Contains(c4.Name.Content) && !gmlEntries.ContainsKey(c4.Name.Content))
				{
					spurious.Add(c4);
				}
			}
			if (spurious.Count > 0)
			{
				var handler17 = new LogInterpolatedStringHandler(61, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler17.AppendLiteral("[ImportCodeEntries] Removing ");
					handler17.AppendFormatted(spurious.Count);
					handler17.AppendLiteral(" compiler side-effect entries...");
				}
				LogService.Log(ref handler17);
				foreach (var code2 in spurious)
				{
					List<GameMakerCode> spuriousChildren = [];
					foreach (var c5 in data.Code)
					{
						if (c5?.ParentEntry == code2)
						{
							spuriousChildren.Add(c5);
						}
					}
					foreach (var child2 in spuriousChildren)
					{
						data.Code.Remove(child2);
						for (var i6 = data.CodeLocals.Count - 1; i6 >= 0; i6--)
						{
							if (data.CodeLocals[i6].Name == child2.Name)
							{
								data.CodeLocals.RemoveAt(i6);
								break;
							}
						}
					}
					for (var i7 = data.Scripts.Count - 1; i7 >= 0; i7--)
					{
						if (data.Scripts[i7]?.Code == code2)
						{
							data.Scripts.RemoveAt(i7);
						}
					}
					for (var i8 = data.CodeLocals.Count - 1; i8 >= 0; i8--)
					{
						if (data.CodeLocals[i8].Name == code2.Name)
						{
							data.CodeLocals.RemoveAt(i8);
							break;
						}
					}
					data.Code.Remove(code2);
					var handler18 = new LogInterpolatedStringHandler(11, 1, out shouldAppend);
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
			var linkedScripts = 0;
			foreach (var script4 in data.Scripts)
			{
				if (script4 != null && script4.Name?.Content != null && script4.Code == null)
				{
					var code3 = ScriptCodeResolver.Resolve(data, script4.Name.Content);
					if (code3 != null)
					{
						script4.Code = code3;
						linkedScripts++;
					}
				}
			}
			if (linkedScripts > 0)
			{
				var handler19 = new LogInterpolatedStringHandler(73, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler19.AppendLiteral("[ImportCodeEntries] Linked ");
					handler19.AppendFormatted(linkedScripts);
					handler19.AppendLiteral(" Script entries to their code (were Code=null)");
				}
				LogService.Log(ref handler19);
			}
		}
		var handler20 = new LogInterpolatedStringHandler(41, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler20.AppendLiteral("[ImportCodeEntries] Compilation done in ");
			handler20.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F1");
			handler20.AppendLiteral("s");
		}
		LogService.Log(ref handler20);
		phaseSw.Restart();
		var restoredEvents = 0;
		if (touchesObjectEventCode && hasQueuedGmlCompilation)
		{
			var objectNameCountsForEvents2 = BuildObjectNameCounts(data);
			for (var k = 0; k < data.GameObjects.Count; k++)
			{
				var obj3 = data.GameObjects[k];
				if (obj3 == null || obj3.Name?.Content == null || !eventSnapshot.TryGetValue(GetObjectEventKey(obj3.Name.Content, k, objectNameCountsForEvents2), out var snapshot3))
				{
					continue;
				}
				HashSet<(int, uint)> expectedPairs = [];
				foreach (var (evtType2, subtype, _) in snapshot3)
				{
					expectedPairs.Add((evtType2, subtype));
				}
				for (var l = 0; l < obj3.Events.Count; l++)
				{
					var evtList2 = obj3.Events[l];
					for (var i9 = evtList2.Count - 1; i9 >= 0; i9--)
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
			var handler21 = new LogInterpolatedStringHandler(69, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler21.AppendLiteral("[ImportCodeEntries] Removed ");
				handler21.AppendFormatted(restoredEvents);
				handler21.AppendLiteral(" spurious events added during compilation");
			}
			LogService.Log(ref handler21);
		}
		var handler22 = new LogInterpolatedStringHandler(53, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler22.AppendLiteral("[ImportCodeEntries] Event restore phase complete in ");
			handler22.AppendFormatted(phaseSw.Elapsed.TotalSeconds, "F2");
			handler22.AppendLiteral("s");
		}
		LogService.Log(ref handler22);
		var eventCleanupSw = Stopwatch.StartNew();
		var objEventsContent = objectEventsContentOverride;
		var objEventsPath = Path.Combine(helpersDir, "object_events.json");
		if (objEventsContent == null && File.Exists(objEventsPath))
		{
			objEventsContent = File.ReadAllText(objEventsPath, Encoding.UTF8);
		}
		if (touchesObjectEventCode && hasQueuedGmlCompilation && objEventsContent != null)
		{
			var targetEventsRoot = JsonSerializer.Deserialize<JsonElement>(objEventsContent);
			var authRemoved = 0;
			var objectNameCountsForCleanup = BuildObjectNameCounts(data);
			for (var m = 0; m < data.GameObjects.Count; m++)
			{
				var obj4 = data.GameObjects[m];
				if (obj4 == null || obj4.Name?.Content == null)
				{
					continue;
				}
				var eventKey = ResolveObjectEventsKey(targetEventsRoot, data, obj4.Name.Content, m, objectNameCountsForCleanup);
				if (eventKey == null || !targetEventsRoot.TryGetProperty(eventKey, out var targetEvents))
				{
					continue;
				}
				HashSet<string> targetEventKeys = [];
				var targetCollisionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var evt2 in targetEvents.EnumerateArray())
				{
					var t = evt2.GetProperty("t").GetInt32();
					if (t == 4 && evt2.TryGetProperty("cn", out var cnElem))
					{
						var cn = cnElem.GetString();
						if (!string.IsNullOrEmpty(cn))
						{
							targetCollisionNames.Add(cn);
						}
					}
					var subtype2 = evt2.GetProperty("s").GetUInt32();
					if (t == 4 && evt2.TryGetProperty("cn", out var collNameElem))
					{
						var collName = collNameElem.GetString();
						var occurrence = evt2.TryGetProperty("co", out var coElem) ? coElem.GetInt32() : 0;
						var collObj = (!string.IsNullOrEmpty(collName)) ? ResolveGameObjectByNameOccurrence(data, collName, occurrence) : null;
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
					var eventKeepKey = $"{t}_{subtype2}";
					GameMakerGameObject.Event? targetEvent = null;
					foreach (var existingEvt in obj4.Events[t])
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
					var hasLiveAction = false;
					if (evt2.TryGetProperty("actions", out var actionsElem) && actionsElem.ValueKind == JsonValueKind.Array)
					{
						targetEvent.Actions.Clear();
						foreach (var actionElm in actionsElem.EnumerateArray())
						{
							targetEvent.Actions.Add(CreateEventActionFromJson(data, actionElm));
						}
						hasLiveAction = targetEvent.Actions.Any(action => action?.CodeId != null);
					}
					else if (evt2.TryGetProperty("c", out var codeNameElem))
					{
						var codeName4 = codeNameElem.GetString();
						if (!string.IsNullOrEmpty(codeName4))
						{
							var code4 = data.Code.ByName(codeName4);
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
				for (var evtType3 = 0; evtType3 < obj4.Events.Count; evtType3++)
				{
					for (var j2 = obj4.Events[evtType3].Count - 1; j2 >= 0; j2--)
					{
						bool shouldKeep;
						if (evtType3 == 4)
						{
							var collObjIdx = (int)obj4.Events[evtType3][j2].EventSubtype;
							var collObjName = (collObjIdx < 0 || collObjIdx >= data.GameObjects.Count) ? null : data.GameObjects[collObjIdx]?.Name?.Content;
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
				var handler23 = new LogInterpolatedStringHandler(73, 1, out shouldAppend);
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
		var handler24 = new LogInterpolatedStringHandler(43, 1, out shouldAppend);
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
			var handler25 = new LogInterpolatedStringHandler(43, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler25.AppendLiteral("[ImportCodeEntries] Hybrid import done in ");
				handler25.AppendFormatted(sw.Elapsed.TotalSeconds, "F1");
				handler25.AppendLiteral("s");
			}
			LogService.Log(ref handler25);
			return;
		}
		var functionReconcileSw = Stopwatch.StartNew();
		if (hasFunctionVariableTables && !gmlCompilationRan)
		{
			ApplyTargetFunctionVariableTables(data, root, "postcompile");
		}
		else if (hasFunctionVariableTables)
		{
			LogService.Log("[ImportCodeEntries] Function/variable reconciliation skipped after GML compilation");
		}
		functionReconcileSw.Stop();
		var handler26 = new LogInterpolatedStringHandler(63, 1, out shouldAppend);
		if (shouldAppend)
		{
			handler26.AppendLiteral("[ImportCodeEntries] Function reconciliation phase complete in ");
			handler26.AppendFormatted(functionReconcileSw.Elapsed.TotalSeconds, "F2");
			handler26.AppendLiteral("s");
		}
		LogService.Log(ref handler26);
		Dictionary<string, GameMakerString>? codeImportStringLookup = null;
		var variableReconcileSw = Stopwatch.StartNew();
		variableReconcileSw.Stop();
		var handler27 = new LogInterpolatedStringHandler(63, 1, out shouldAppend);
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
			var handler28 = new LogInterpolatedStringHandler(43, 1, out shouldAppend);
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
		var localVarLookupSw = Stopwatch.StartNew();
		Dictionary<string, int> localVarLookup = [];
		for (var vi = 0; vi < data.Variables.Count; vi++)
		{
			var v = data.Variables[vi];
			var name = v?.Name?.Content;
			if (name != null && v.InstanceType == GameMakerInstruction.InstanceType.Local)
			{
				localVarLookup.TryAdd(name, vi);
			}
		}
		localVarLookupSw.Stop();
		var stripStringIdxRegex = StripStringIdxRegex();
		var reassembled = 0;
		var asmFailed = 0;
		List<(string, string, bool)> asmGmlFallbackEntries = [];
		var archiveLookupSw = Stopwatch.StartNew();
		archiveCodeLookup = BuildArchiveCodeLookup(data);

		archiveLookupSw.Stop();
		var skeletonSw = Stopwatch.StartNew();
		codeImportStringLookup ??= BuildStringLookup(data);
		var createdAsmSkeletons = 0;
		foreach (var entryKey7 in asmEntries.Keys)
		{
			if (archiveCodeLookup.ContainsKey(entryKey7))
			{
				continue;
			}
			var codeName5 = codeEntryLogicalNames?.GetValueOrDefault(entryKey7) ?? targetTopLevelNamesByKey.GetValueOrDefault(entryKey7) ?? Path.GetFileName(entryKey7);
			var newCode = new GameMakerCode
			{
				Name = MakeStringCached(data, codeImportStringLookup, codeName5)
			};
			if (asmEntryPaths != null && asmEntryPaths.TryGetValue(entryKey7, out var asmPath))
			{
				var parts = asmPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length >= 3)
				{
					var parentKey = parts[^2];
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
			var handler29 = new LogInterpolatedStringHandler(75, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler29.AppendLiteral("[ImportCodeEntries] Created ");
				handler29.AppendFormatted(createdAsmSkeletons);
				handler29.AppendLiteral(" missing ASM code skeletons from patch metadata");
			}
			LogService.Log(ref handler29);
		}
		skeletonSw.Stop();
		var assemblerCacheSw = Stopwatch.StartNew();
		Assembler.SetLookupCaches(data);
		assemblerCacheSw.Stop();
		var asmLoopSw = Stopwatch.StartNew();
		var childDirectiveSw = new Stopwatch();
		var asmPreprocessSw = new Stopwatch();
		var assembleOnlySw = new Stopwatch();
		var replaceInstructionsSw = new Stopwatch();
		HashSet<GameMakerCode> currentCodeSet = [.. data.Code];
		Dictionary<GameMakerCode, List<GameMakerScript>> scriptsByCode = [];
		foreach (var script5 in data.Scripts)
		{
			if (script5?.Code != null)
			{
				if (!scriptsByCode.TryGetValue(script5.Code, out var scriptList))
				{
					scriptList = [];
					scriptsByCode[script5.Code] = scriptList;
				}
				scriptList.Add(script5);
			}
		}
		var preprocessedAsmEntries = 0;
		var childDirectiveEntries = 0;
		try
		{
			foreach (var asmEntry in asmEntries)
			{
				asmEntry.Deconstruct(out value2, out key);
				var entryKey8 = value2;
				var asmText = key;
				if (!archiveCodeLookup.TryGetValue(entryKey8, out var code5))
				{
					continue;
				}
				var codeName6 = codeEntryLogicalNames?.GetValueOrDefault(entryKey8) ?? targetTopLevelNamesByKey.GetValueOrDefault(entryKey8) ?? code5.Name?.Content ?? entryKey8;
				try
				{
					List<string> targetChildNames = [];
					if (asmText.Contains("\n> ", StringComparison.Ordinal) || asmText.StartsWith("> ", StringComparison.Ordinal))
					{
						childDirectiveSw.Start();
						using var sr = new StringReader(asmText);
						string ln;
						while ((ln = sr.ReadLine()) != null)
						{
							ln = ln.Trim();
							if (ln.StartsWith("> ", StringComparison.Ordinal))
							{
								var rest = ln[2..].Trim();
								var sp = rest.IndexOf(' ', StringComparison.Ordinal);
								if (sp > 0)
								{
									targetChildNames.Add(rest[..sp]);
								}
							}
						}
						childDirectiveSw.Stop();
					}
					Dictionary<string, string> nameMap = [];
					if (targetChildNames.Count > 0)
					{
						childDirectiveEntries++;
						childDirectiveSw.Start();
						targetChildEntriesByParent.TryGetValue(entryKey8, out var explicitChildEntries);
						var hasExplicitChildEntries = false;
						if (explicitChildEntries != null && explicitChildEntries.Count > 0)
						{
							var explicitChildren = explicitChildEntries;
							hasExplicitChildEntries = true;
							foreach (var existingChild in code5.ChildEntries.ToList())
							{
								code5.ChildEntries.Remove(existingChild);
								if (existingChild.ParentEntry == code5)
								{
									existingChild.ParentEntry = null;
								}
								data.Code.Remove(existingChild);
								currentCodeSet.Remove(existingChild);
								for (var i10 = data.CodeLocals.Count - 1; i10 >= 0; i10--)
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
								foreach (var item5 in scriptsForChild)
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
						var targetChildSet = new HashSet<string>(targetChildNames, StringComparer.Ordinal);
						foreach (var compiledChild in code5.ChildEntries.ToList())
						{
							var compiledName = compiledChild.Name?.Content;
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
							for (var i11 = data.CodeLocals.Count - 1; i11 >= 0; i11--)
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
							foreach (var item6 in scriptsForChild2)
							{
								item6.Code = null;
							}
						}
						List<GameMakerCode> oursChildren = [.. code5.ChildEntries.OrderBy(gameMakerCode => gameMakerCode.Offset)];
						if (targetChildNames.Count != oursChildren.Count)
						{
							var handler30 = new LogInterpolatedStringHandler(103, 3, out shouldAppend);
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
						var remapCount = (!hasExplicitChildEntries) ? Math.Min(targetChildNames.Count, oursChildren.Count) : 0;
						for (var ci = 0; ci < remapCount; ci++)
						{
							var tName = targetChildNames[ci];
							var oName = oursChildren[ci].Name?.Content;
							if (!string.IsNullOrEmpty(oName) && tName != oName)
							{
								nameMap[tName] = oName;
							}
						}
						childDirectiveSw.Stop();
					}
					var needsLocalVarRemap = asmText.Contains(".localvar", StringComparison.Ordinal);
					var needsStringSuffixStrip = asmText.Contains("push.s ", StringComparison.Ordinal);
					var needsChildNameRemap = nameMap.Count > 0;
					var assemblySource = asmText;
					if (needsLocalVarRemap || needsStringSuffixStrip || needsChildNameRemap)
					{
						preprocessedAsmEntries++;
						asmPreprocessSw.Start();
						var sb = new StringBuilder(asmText.Length);
						using var sr2 = new StringReader(asmText);
						string ln2;
						while ((ln2 = sr2.ReadLine()) != null)
						{
							var trimmed = ln2.Trim();
							if (needsLocalVarRemap && trimmed.StartsWith(".localvar", StringComparison.Ordinal))
							{
								var parts2 = trimmed.Split(' ');
								if (parts2.Length >= 4)
								{
									var varName = parts2[2];
									if (localVarLookup.TryGetValue(varName, out var newIdx))
									{
										parts2[3] = newIdx.ToString(CultureInfo.InvariantCulture);
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
								foreach (var kvp in nameMap)
								{
									if (ln2.Contains(kvp.Key, StringComparison.Ordinal))
									{
										ln2 = ln2.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);
									}
								}
							}
							sb.AppendLine(ln2);
						}
						assemblySource = sb.ToString();
						asmPreprocessSw.Stop();
					}
					assembleOnlySw.Start();
					var newInstructions = Assembler.Assemble(assemblySource, data, delegate (Action action)
					{
						action();
					}, code5);
					assembleOnlySw.Stop();
					replaceInstructionsSw.Start();
					code5.Instructions.Clear();
					foreach (var instr in newInstructions)
					{
						code5.Instructions.Add(instr);
					}
					var totalWords = 0u;
					foreach (var instr2 in newInstructions)
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
					if (gmlEntries.TryGetValue(entryKey8, out var fallbackGml))
					{
						asmGmlFallbackEntries.Add((codeName6, fallbackGml, codeName6.Contains("_Collision_", StringComparison.Ordinal)));
					}
					if (asmFailed <= 5)
					{
						var handler31 = new LogInterpolatedStringHandler(36, 2, out shouldAppend);
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
			var fallbackFailed = RetryGmlOnlyCodeEntriesIndividually(data, asmGmlFallbackEntries, collisionTargetsByCode);
			var recovered = asmGmlFallbackEntries.Count - fallbackFailed;
			asmFailed -= recovered;
			reassembled += recovered;
			var handler32 = new LogInterpolatedStringHandler(59, 2, out shouldAppend);
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
		var postAsmTopologySw = Stopwatch.StartNew();
		if (pendingChildReattachments.Count > 0)
		{
			var reattachedChildren = 0;
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
				var handler33 = new LogInterpolatedStringHandler(75, 1, out shouldAppend);
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
			var canonicalParents = 0;
			var canonicalChildren = 0;
			var allTrackedCodes = new Dictionary<string, GameMakerCode>(StringComparer.Ordinal);
			foreach (var item7 in topLevelEntrySnapshot)
			{
				item7.Deconstruct(out key, out value3);
				(allTrackedCodes[key], _, _) = value3;
			}
			foreach (var item8 in childEntrySnapshot)
			{
				item8.Deconstruct(out key, out value4);
				(allTrackedCodes[key], _, _, _) = value4;
			}
			archiveCodeLookup = BuildArchiveCodeLookup(data);
			foreach (var item9 in archiveCodeLookup)
			{
				item9.Deconstruct(out key, out var value5);
				var entryKey11 = key;
				var code6 = value5;
				allTrackedCodes[entryKey11] = code6;
			}
			foreach (var item10 in targetCodeEntriesByKey)
			{
				item10.Deconstruct(out key, out value);
				var entryKey12 = key;
				var meta2 = value;
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
			foreach (var item11 in targetCodeEntriesByKey)
			{
				item11.Deconstruct(out key, out value);
				var entryKey13 = key;
				var meta3 = value;
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
			var handler34 = new LogInterpolatedStringHandler(92, 2, out shouldAppend);
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

		postAsmTopologySw.Stop();
		if (asmFailed > 5)
		{
			var handler35 = new LogInterpolatedStringHandler(57, 1, out shouldAppend);
			if (shouldAppend)
			{
				handler35.AppendLiteral("[ImportCodeEntries] ... and ");
				handler35.AppendFormatted(asmFailed - 5);
				handler35.AppendLiteral(" more ASM errors (suppressed)");
			}
			LogService.Log(ref handler35);
		}
		var handler36 = new LogInterpolatedStringHandler(165, 14, out shouldAppend);
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
		var handler37 = new LogInterpolatedStringHandler(46, 3, out shouldAppend);
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
		var handler38 = new LogInterpolatedStringHandler(47, 1, out shouldAppend);
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
		var phaseSw = Stopwatch.StartNew();
		var handler = new LogInterpolatedStringHandler(90, 1, out var shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[ImportCodeEntries] Retrying ");
			handler.AppendFormatted(gmlOnlyEntries.Count);
			handler.AppendLiteral(" GML-only entries individually after group compile failure...");
		}
		LogService.Log(ref handler);
		var ctx = new GlobalDecompileContext(data);
		ctx.PrepareForCompilation();
		var applied = 0;
		var failed = 0;
		foreach (var (CodeName, GmlCode, IsCollision) in gmlOnlyEntries)
		{
			try
			{
				var codeLookup = new Dictionary<string, GameMakerCode>(data.Code.Count);
				foreach (var item in data.Code)
				{
					foreach (var nested in EnumerateCodeTreeRecursive(item))
					{
						if (nested != null && nested.Name?.Content != null)
						{
							codeLookup.TryAdd(nested.Name.Content, nested);
						}
					}
				}
				var goLookup = new Dictionary<string, (GameMakerGameObject, int)>(data.GameObjects.Count);
				for (var i = 0; i < data.GameObjects.Count; i++)
				{
					var go = data.GameObjects[i];
					if (go != null && go.Name?.Content != null)
					{
						goLookup.TryAdd(go.Name.Content, (go, i));
					}
				}
				var singleGroup = new CodeImportGroup(data, ctx)
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
				var result = singleGroup.Import(throwOnFailedCompile: false);
				if (result.Successful)
				{
					applied++;
					continue;
				}
				failed++;
				var handler2 = new LogInterpolatedStringHandler(48, 2, out shouldAppend);
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
				var handler3 = new LogInterpolatedStringHandler(51, 2, out shouldAppend);
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
		var handler4 = new LogInterpolatedStringHandler(59, 4, out shouldAppend);
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
		var oldByName = new Dictionary<string, GameMakerFunction>(StringComparer.Ordinal);
		foreach (var function in data.Functions)
		{
			var name = function?.Name?.Content;
			if (!string.IsNullOrEmpty(name) && function != null)
			{
				oldByName.TryAdd(name, function);
			}
		}
		var stringLookup = BuildStringLookup(data);
		var newList = new List<GameMakerFunction>(funcArray.GetArrayLength());
		var newByName = new Dictionary<string, GameMakerFunction>(StringComparer.Ordinal);
		foreach (var item in funcArray.EnumerateArray())
		{
			var name2 = item.GetString();
			if (!string.IsNullOrEmpty(name2))
			{
				name2 = FunctionNameUtil.CanonicalizeFunctionName(name2);
				var function2 = oldByName.TryGetValue(name2, out var existing) ? existing : new GameMakerFunction
				{
					Name = MakeStringCached(data, stringLookup, name2)
				};
				function2.Name = MakeStringCached(data, stringLookup, name2);
				newList.Add(function2);
				newByName.TryAdd(name2, function2);
			}
		}
		data.Functions.Clear();
		foreach (var function3 in newList)
		{
			data.Functions.Add(function3);
		}
		var remapped = 0;
		foreach (var item2 in data.Code)
		{
			foreach (var item3 in EnumerateCodeTreeRecursive(item2))
			{
				foreach (var instruction in item3.Instructions ?? [])
				{
					var name3 = instruction.ValueFunction?.Name?.Content;
					if (!string.IsNullOrEmpty(name3) && newByName.TryGetValue(FunctionNameUtil.CanonicalizeFunctionName(name3), out var replacement) && instruction.ValueFunction != replacement)
					{
						instruction.ValueFunction = replacement;
						remapped++;
					}
				}
			}
		}
		var handler = new LogInterpolatedStringHandler(49, 3, out var shouldAppend);
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
		var newList = new List<GameMakerVariable>(varArray.GetArrayLength());
		Dictionary<(string, GameMakerInstruction.InstanceType, int), GameMakerVariable> newExact = [];
		Dictionary<(string, GameMakerInstruction.InstanceType), GameMakerVariable> newByNameType = [];
		foreach (var vElem in varArray.EnumerateArray())
		{
			var name = vElem.GetProperty("n").GetString() ?? "";
			var instType = vElem.GetProperty("t").GetInt32();
			var varId = vElem.GetProperty("id").GetInt32();
			if (!string.IsNullOrEmpty(name))
			{
				var instanceType = (GameMakerInstruction.InstanceType)instType;
				var variable = new GameMakerVariable
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
		foreach (var variable2 in newList)
		{
			data.Variables.Add(variable2);
		}
		var remapped = 0;
		foreach (var item in data.Code)
		{
			foreach (var item2 in EnumerateCodeTreeRecursive(item))
			{
				foreach (var instruction in item2.Instructions ?? [])
				{
					var variable3 = instruction.ValueVariable;
					var name2 = variable3?.Name?.Content;
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
		var handler = new LogInterpolatedStringHandler(49, 3, out var shouldAppend);
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
		var created = new GameMakerString(content);
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
		if (stringLookup.TryGetValue(content, out var existing))
		{
			return existing;
		}
		var created = new GameMakerString(content);
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
		var stringLookup = new Dictionary<string, GameMakerString>(data.Strings.Count, StringComparer.Ordinal);
		foreach (var str in data.Strings)
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
		if (!TryParseObjectEventCodeName(codeName, out var objectName, out var eventType, out var eventSubtype) || !goLookup.TryGetValue(objectName, out (GameMakerGameObject, int) objEntry))
		{
			return;
		}
		var (obj, _) = objEntry;
		if (eventType < 0 || eventType >= obj.Events.Count)
		{
			return;
		}
		if (!codeLookup.TryGetValue(codeName, out var codeEntry))
		{
			codeEntry = codeLookup[codeName] = GameMakerCode.CreateEmptyEntry(data, codeName);
		}
		var eventList = obj.Events[eventType];
		GameMakerGameObject.Event? targetEvent = null;
		foreach (var evt in eventList)
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
		var counts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var gameObject in data.GameObjects)
		{
			var name = gameObject?.Name?.Content;
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
			var exactKey = GetObjectEventKey(objectName, objectIndex, objectNameCounts);
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
		var occurrence = 0;
		for (var i = 0; i < objectIndex; i++)
		{
			if (string.Equals(data.GameObjects[i]?.Name?.Content, objectName, StringComparison.Ordinal))
			{
				occurrence++;
			}
		}
		List<(int, string)> duplicateKeys = [];
		var prefix = objectName + "__idx";
		foreach (var prop in targetEventsRoot.EnumerateObject())
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
		using var doc = JsonDocument.Parse(objectEventsJson);
		var root = doc.RootElement;
		var objectNameCounts = BuildObjectNameCounts(data);
		for (var objIndex = 0; objIndex < data.GameObjects.Count; objIndex++)
		{
			var obj = data.GameObjects[objIndex];
			if (obj == null || obj.Name?.Content == null)
			{
				continue;
			}
			var eventKey = ResolveObjectEventsKey(root, data, obj.Name.Content, objIndex, objectNameCounts);
			if (eventKey == null || !root.TryGetProperty(eventKey, out var targetEvents))
			{
				continue;
			}
			List<(int, uint, JsonElement?, string, string)> desired = [];
			var objectUsesChangedCode = changedCodeNames == null;
			foreach (var evt in targetEvents.EnumerateArray())
			{
				var type = evt.GetProperty("t").GetInt32();
				var subtype = evt.GetProperty("s").GetUInt32();
				var actions = (evt.TryGetProperty("actions", out var actionsElem) && actionsElem.ValueKind == JsonValueKind.Array) ? new JsonElement?(actionsElem) : null;
				var codeName = evt.TryGetProperty("c", out var c) ? (c.GetString() ?? "") : "";
				var collisionName = evt.TryGetProperty("cn", out var cn) ? cn.GetString() : null;
				if (!objectUsesChangedCode)
				{
					if (!string.IsNullOrEmpty(codeName) && changedCodeNames.Contains(codeName))
					{
						objectUsesChangedCode = true;
					}
					else if (actions.HasValue)
					{
						foreach (var item in actions.Value.EnumerateArray())
						{
							if (item.TryGetProperty("codeId", out var codeIdElm) && changedCodeNames.Contains(codeIdElm.GetString() ?? ""))
							{
								objectUsesChangedCode = true;
								break;
							}
						}
					}
				}
				var collisionOccurrence = evt.TryGetProperty("co", out var co) ? co.GetInt32() : 0;
				if (type == 4 && !string.IsNullOrEmpty(collisionName))
				{
					var collObj = ResolveGameObjectByNameOccurrence(data, collisionName, collisionOccurrence);
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
			for (var eventType = 0; eventType < obj.Events.Count; eventType++)
			{
				obj.Events[eventType].Clear();
			}
			foreach (var evt2 in desired)
			{
				if (evt2.Item1 < 0 || evt2.Item1 >= obj.Events.Count)
				{
					continue;
				}
				var targetEvent = new GameMakerGameObject.Event
				{
					EventSubtype = evt2.Item2
				};
				if (evt2.Item3.HasValue)
				{
					foreach (var actionElm in evt2.Item3.Value.EnumerateArray())
					{
						targetEvent.Actions.Add(CreateEventActionFromJson(data, actionElm));
					}
				}
				else if (!string.IsNullOrEmpty(evt2.Item4))
				{
					var code = data.Code.ByName(evt2.Item4);
					if (code != null)
					{
						targetEvent.Actions.Add(new GameMakerGameObject.EventAction
						{
							CodeId = code
						});
					}
				}
				if (targetEvent.Actions.Any(action => action?.CodeId != null))
				{
					obj.Events[evt2.Item1].Add(targetEvent);
				}
			}
		}
	}

	private static GameMakerGameObject? ResolveGameObjectByNameOccurrence(GameMakerData data, string objectName, int occurrence)
	{
		var targetOccurrence = Math.Max(occurrence, 0);
		var seen = 0;
		foreach (var obj in data.GameObjects)
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
		(string, int)[] array =
		[
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
		];
		for (var i = 0; i < array.Length; i++)
		{
			var tuple = array[i];
			var suffix = tuple.Item1;
			var type = tuple.Item2;
			var idx = codeName.LastIndexOf(suffix, StringComparison.Ordinal);
			if (idx > "gml_Object_".Length && uint.TryParse(codeName[(idx + suffix.Length)..], out eventSubtype))
			{
				var length = "gml_Object_".Length;
				objectName = codeName[length..idx];
				eventType = type;
				return true;
			}
		}
		return false;
	}

	[GeneratedRegex("\"@\\d+")]
	private static partial Regex StripStringIdxRegex();

	private static void ImportCollisionEvent(GameMakerData data, string codeName, string gmlCode, CodeImportGroup importGroup, Dictionary<string, GameMakerCode> codeLookup, Dictionary<string, (GameMakerGameObject obj, int idx)> goLookup, Dictionary<string, string> collisionTargetsByCode)
	{
		if (!codeName.StartsWith("gml_Object_", StringComparison.Ordinal))
		{
			return;
		}
		var collisionIdx = codeName.LastIndexOf("_Collision_", StringComparison.Ordinal);
		if (collisionIdx < 0)
		{
			return;
		}
		var length = "gml_Object_".Length;
		var objectName = codeName[length..collisionIdx];
		var identifier = codeName[(collisionIdx + "_Collision_".Length)..];
		if (!goLookup.TryGetValue(objectName, out (GameMakerGameObject, int) objEntry))
		{
			return;
		}
		var (obj, _) = objEntry;
		uint collisionIndex;
		if (collisionTargetsByCode.TryGetValue(codeName, out var collisionObjectName))
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
		var collisionEvents = obj.Events[4];
		GameMakerCode? codeEntry = null;
		foreach (var evt in collisionEvents)
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
			codeEntry ??= GameMakerCode.CreateEmptyEntry(data, codeName);
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
			codeEntry ??= GameMakerCode.CreateEmptyEntry(data, codeName);
			var newEvent = new GameMakerGameObject.Event
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
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		var objectEventsContent = objectEventsContentOverride;
		var objEventsPath = Path.Combine(helpersDir, "object_events.json");
		if (objectEventsContent == null && File.Exists(objEventsPath))
		{
			objectEventsContent = File.ReadAllText(objEventsPath, Encoding.UTF8);
		}
		if (objectEventsContent == null)
		{
			return result;
		}
		using var doc = JsonDocument.Parse(objectEventsContent);
		foreach (var item in doc.RootElement.EnumerateObject())
		{
			foreach (var evt in item.Value.EnumerateArray())
			{
				if (evt.TryGetProperty("t", out var typeElem) && typeElem.GetInt32() == 4 && evt.TryGetProperty("c", out var codeElem) && codeElem.ValueKind == JsonValueKind.String && evt.TryGetProperty("cn", out var collisionNameElem) && collisionNameElem.ValueKind == JsonValueKind.String)
				{
					var codeName = codeElem.GetString();
					var collisionName = collisionNameElem.GetString();
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
		List<string> parts = [];
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

	public static Task<PatchValidateResult> ValidatePatchAsync(string patchPath, string? dataPath, G3MCacheOptions? cacheOptions)
		=> ValidatePatchAsync(patchPath, dataPath, cacheOptions, CancellationToken.None);

	public static async Task<PatchValidateResult> ValidatePatchAsync(string patchPath, string? dataPath = null, G3MCacheOptions? cacheOptions = null, CancellationToken cancellationToken = default)
	{
		using var cancellationScope = ModdingCancellation.Begin(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
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
			using var zipStream = new FileStream(patchPath, FileMode.Open, FileAccess.Read);
			using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
			var manifestEntry = archive.GetEntry("g3mpatch.json");
			if (manifestEntry == null)
			{
				return new PatchValidateResult
				{
					Success = false,
					Error = "Invalid patch: g3mpatch.json not found"
				};
			}
			if (manifestEntry.Length > PatchFileSystem.MaxManifestBytes)
			{
				return new PatchValidateResult
				{
					Success = false,
					Error = "Invalid patch: g3mpatch.json is too large"
				};
			}
			using var reader = new StreamReader(await manifestEntry.OpenAsync(cancellationToken).ConfigureAwait(false));
			// The header length can lie, so the cap is enforced on what is actually read
			var manifestText = new StringBuilder();
			var chars = new char[8192];
			int charsRead;
			while ((charsRead = await reader.ReadAsync(chars.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
			{
				manifestText.Append(chars, 0, charsRead);
				if (manifestText.Length > PatchFileSystem.MaxManifestBytes)
				{
					return new PatchValidateResult { Success = false, Error = "Invalid patch: g3mpatch.json is too large" };
				}
			}
			var manifest = JsonSerializer.Deserialize<G3MPatchManifest>(manifestText.ToString());
			if (manifest == null)
			{
				return new PatchValidateResult
				{
					Success = false,
					Error = "Invalid patch: failed to parse manifest"
				};
			}
			cancellationToken.ThrowIfCancellationRequested();
			if (dataPath != null && File.Exists(dataPath))
			{
				var text = (G3MCacheService.TryReadDataFileInfo(dataPath, cacheOptions)?.Md5) ?? await HashService.ComputeFileHashAsync(dataPath).ConfigureAwait(false);
				var dataHash = text;
				if (manifest.Original?.Md5 != null && manifest.Original.Md5 != dataHash)
				{
					LogService.Warning(" Data file hash doesn't match original - patch may not apply correctly");
				}
			}
			cancellationToken.ThrowIfCancellationRequested();
			return new PatchValidateResult
			{
				Success = true,
				Manifest = manifest
			};
		}
		catch (Exception ex) when (ModdingCancellation.IsCancellation(ex))
		{
			ModdingCancellation.Rethrow(ex, cancellationToken);
			throw;
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
		var ext = Path.GetExtension(inputPath).ToLowerInvariant();
		if (ext is ".g3mpatch" or ".zip") return inputPath;
		if (ext == ".csx")
			throw new NotSupportedException("CSX inputs require a scripting host; materialize the data file before creating a patch.");
		if (!PatchInputService.IsXDelta(inputPath) && !DataFileExtensionUtil.IsDataFile(inputPath))
			throw new NotSupportedException("Unsupported patch input '" + Path.GetFileName(inputPath) + "'.");

		var outputZip = Path.Combine(tempDir ?? TempRoot.Path, $"g3mlib_conv_{Guid.NewGuid():N}.g3mpatch");
		var result = await CreatePatchAsync(originalPath, inputPath, outputZip, precomputedOriginalHashes, precomputedOriginalInfo, precomputedOriginalNameCounts, precomputedOriginalOrderedNames, null, includeXdeltaFallback: false, cacheOptions).ConfigureAwait(false);
		if (!result.Success)
			throw new InvalidOperationException("Failed to create .g3mpatch from '" + Path.GetFileName(inputPath) + "': " + result.Error);
		return outputZip;
	}

	public static void LogCreateCompatibilityWarnings(DataFileInfo original, DataFileInfo modified)
	{
		ArgumentNullException.ThrowIfNull(modified);
		ArgumentNullException.ThrowIfNull(original);
		if (original.BytecodeVersion != modified.BytecodeVersion)
		{
			LogService.Warning($"Data file bytecode version differs ({original.BytecodeVersion} vs {modified.BytecodeVersion}). " + "The patch will still be created.");
		}
		var originalGeneralInfo = original.GeneralInfo;
		var modifiedGeneralInfo = modified.GeneralInfo;
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
			var smaller = Math.Min(originalGeneralInfo.RoomOrderCount, modifiedGeneralInfo.RoomOrderCount);
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
		var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var resourceType2 in resourceTypes)
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
		var sawAny = false;
		var sawOnlyScriptMetadata = true;
		foreach (var resourceType in resourceTypesToProcess)
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
		foreach (var resourceType in resourceTypesToProcess)
		{
			if (resourceType.Equals("Scripts", StringComparison.OrdinalIgnoreCase) || resourceType.Equals("GlobalScripts", StringComparison.OrdinalIgnoreCase) || resourceType.Equals("CodeEntries", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool RequiresObjectEventsHelper(Dictionary<string, HashSet<string>> changedNamesPerType)
	{
		if (!changedNamesPerType.TryGetValue("CodeEntries", out var codeEntries))
		{
			return false;
		}
		foreach (var codeName in codeEntries)
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
		if (resourceTypesToProcess.Contains("GameObjects", StringComparer.OrdinalIgnoreCase))
		{
			return true;
		}
		if (manifest?.Resources == null || !manifest.Resources.TryGetValue("CodeEntries", out var codeChanges))
		{
			return false;
		}
		var changedCodeNames = GetChangedCodeEntryNames(codeChanges);
		if (changedCodeNames.Count == 0)
		{
			return true;
		}
		var objectEventsText = Encoding.UTF8.GetString(objectEventsBytes);
		foreach (var codeName in changedCodeNames)
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
		if (manifest?.Resources == null || !manifest.Resources.TryGetValue("CodeEntries", out var codeChanges))
		{
			return [];
		}
		return GetChangedCodeEntryNames(codeChanges);
	}

	private static HashSet<string> GetChangedCodeEntryNames(ResourceTypeChanges codeChanges)
	{
		var names = new HashSet<string>(StringComparer.Ordinal);
		if (codeChanges.Changed != null)
		{
			foreach (var change in codeChanges.Changed)
			{
				if (!string.IsNullOrWhiteSpace(change?.Name))
				{
					names.Add(change.Name);
				}
			}
		}
		if (codeChanges.New != null)
		{
			foreach (var change2 in codeChanges.New)
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
		foreach (var changedResourceType in changedResourceTypes)
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
		foreach (var resourceType in changedResourceTypes)
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
		return resourceType switch
		{
			"AudioGroups" or "GeneralInfo" or "Options" or "Shaders" or "EmbeddedAudio" or "FilterEffects" or "Language" or "FeatureFlags" or "Tags" or "TextureGroupInfo" or "EmbeddedImages" or "AnimationCurves" or "Extensions" => true,
			_ => false,
		};
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
		string[] resourceTypes = [.. resources.Keys];
		var requiresCodePipeline = resourceTypes.Contains("CodeEntries", StringComparer.OrdinalIgnoreCase);
		var requiresTexturePipeline = RequiresTextureMappingHelpers(resourceTypes) || resourceTypes.Contains("EmbeddedTextures", StringComparer.OrdinalIgnoreCase) || resourceTypes.Contains("TexturePageItems", StringComparer.OrdinalIgnoreCase);
		var requiresAssetReorder = RequiresFinalAssetReorder(resourceTypes);
		var requiresHeavyFinalize = RequiresHeavyFinalize(resourceTypes);
		List<string> simpleTypes = [.. resourceTypes.Where(IsDirectApplyFriendlyResourceType).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
		List<string> heavyTypes = [.. resourceTypes.Where(x => !IsDirectApplyFriendlyResourceType(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
		var supportsDirectResourceApply = heavyTypes.Count == 0 && resources.Values.All(changes => (changes.Deleted?.Count ?? 0) == 0);
		return new PatchApplyPlan
		{
			Mode = supportsDirectResourceApply ? "direct" : "standard",
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
			foreach (var helperForcedResourceType in helperForcedResourceTypes)
			{
				if (!string.IsNullOrWhiteSpace(helperForcedResourceType))
				{
					return true;
				}
			}
		}
		var sawAny = false;
		foreach (var changedResourceType in changedResourceTypes)
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
		var resourceTypes = new HashSet<string>(changedNamesPerType.Keys, StringComparer.OrdinalIgnoreCase);
		var allTypes = ResourceTypeRegistry.AllTypes;
		foreach (var resourceType in allTypes)
		{
			var obj = originalHashes.GetValueOrDefault(resourceType) ?? [];
			var modTypeHashes = modifiedHashes.GetValueOrDefault(resourceType) ?? [];
			foreach (var name in obj.Keys)
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
		var hasGameObjects = false;
		foreach (var changedResourceType in changedResourceTypes)
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
		var changes = manifest?.Resources?.GetValueOrDefault("GameObjects");
		if (changes == null)
		{
			return true;
		}
		List<string> names = [];
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
		var baseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var item in names)
		{
			var baseName = StripIdxSuffix(item);
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
		var sawAny = false;
		foreach (var resourceType in resourceTypesToProcess)
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
		foreach (var item in resourceTypesToProcess)
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
			var currentNames = GetCurrentAssetOrderSection(data, section);
			if (currentNames == null || currentNames.Count != targetNames.Count)
			{
				return false;
			}
			for (var i = 0; i < targetNames.Count; i++)
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
		var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		string? currentSection = null;
		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i].TrimEnd();
			if (line.StartsWith("@@", StringComparison.Ordinal) && line.EndsWith("@@", StringComparison.Ordinal) && line.Length > 4)
			{
				var text = line;
				currentSection = text[2..^2];
				sections[currentSection] = [];
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
			"sounds" => [.. data.Sounds.Select(s => s?.Name?.Content ?? "")],
			"sprites" => [.. data.Sprites.Select(s => s?.Name?.Content ?? "")],
			"backgrounds" => [.. data.Backgrounds.Select(b => b?.Name?.Content ?? "")],
			"paths" => [.. data.Paths.Select(p => p?.Name?.Content ?? "")],
			"scripts" => [.. data.Scripts.Select(s => s?.Name?.Content ?? "")],
			"fonts" => [.. data.Fonts.Select(f => f?.Name?.Content ?? "")],
			"objects" => [.. data.GameObjects.Select(o => o?.Name?.Content ?? "")],
			"timelines" => [.. data.Timelines.Select(t => t?.Name?.Content ?? "")],
			"rooms" => [.. data.Rooms.Select(r => r?.Name?.Content ?? "")],
			"shaders" => [.. data.Shaders.Select(s => s?.Name?.Content ?? "")],
			"extensions" => [.. data.Extensions.Select(e => e?.Name?.Content ?? "")],
			"audiogroups" => [.. data.AudioGroups.Select(a => a?.Name?.Content ?? "")],
			_ => null,
		};
	}

	private static string[] SelectAssetOrderSections(string[] lines, HashSet<string> keepSections, params string[] skipSections)
	{
		var skip = new HashSet<string>(skipSections, StringComparer.OrdinalIgnoreCase);
		var result = new List<string>(lines.Length);
		var inSelected = false;
		var inCounts = false;
		foreach (var line in lines)
		{
			if (line.StartsWith("@@", StringComparison.Ordinal) && line.EndsWith("@@", StringComparison.Ordinal) && line.Length > 4)
			{
				var text = line;
				var sect = text[2..^2];
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
					var eq = line.IndexOf('=', StringComparison.Ordinal);
					if (eq > 0)
					{
						var countName = line[..eq];
						if (skip.Contains(countName) || !keepSections.Contains(countName) && !countName.Equals("EmbeddedTextures", StringComparison.OrdinalIgnoreCase) && !countName.Equals("TexturePageItems", StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}
					}
				}
				result.Add(line);
			}
		}
		return [.. result];
	}

	private static void CopyEmbeddedTexturesForAssetOrder(PatchFileSystem pfs, string assetOrderDir)
	{
		var embeddedDir = Path.Combine(assetOrderDir, "EmbeddedTextures");
		foreach (var path in pfs.GetAllFilePaths())
		{
			string? sourcePrefix = null;
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
			var relative = path[sourcePrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
			if (!string.IsNullOrWhiteSpace(relative))
			{
				var path2 = Paths.JoinVerifyWithinDirectory(embeddedDir, relative);
				var outputDir = Path.GetDirectoryName(path2);
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
		var hasEmbeddedTextures = false;
		foreach (var path in pfs.GetAllFilePaths())
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
		var stageDir = Path.Combine(TempRoot.Path, $"g3mlib_embedded_stage_{Guid.NewGuid():N}");
		Directory.CreateDirectory(stageDir);
		CopyEmbeddedTexturesForAssetOrder(pfs, stageDir);
		ExportMissingEmbeddedTexturesForAssetOrder(data, pfs, stageDir);
		return stageDir;
	}

	private static void ExportMissingEmbeddedTexturesForAssetOrder(GameMakerData data, PatchFileSystem pfs, string stageDir)
	{
		var embeddedDir = Path.Combine(stageDir, "EmbeddedTextures");
		Directory.CreateDirectory(embeddedDir);
		var touchedIndices = GetTouchedEmbeddedTextureIndices(pfs);
		var gm2022_5 = data.IsVersionAtLeast(2022u, 5u);
		for (var i = 0; i < data.EmbeddedTextures.Count; i++)
		{
			if (touchedIndices.Contains(i))
			{
				continue;
			}
			var texture = data.EmbeddedTextures[i];
			if (texture == null)
			{
				continue;
			}
			var image = texture.TextureData?.Image;
			if (image != null)
			{
				var textureName = $"texture_{i:D4}";
				var textureDir = Path.Combine(embeddedDir, textureName);
				var jsonPath = Path.Combine(textureDir, textureName + ".json");
				var binPath = Path.Combine(textureDir, textureName + ".bin");
				if (!File.Exists(jsonPath) && !File.Exists(binPath))
				{
					Directory.CreateDirectory(textureDir);
					File.WriteAllBytes(binPath, image.ToSpan(gm2022_5).ToArray());
					var meta = new Dictionary<string, object>
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
		var sourceRoot = Path.Combine(stagedDir, "EmbeddedTextures");
		if (!Directory.Exists(sourceRoot))
		{
			return;
		}
		var targetRoot = Path.Combine(assetOrderDir, "EmbeddedTextures");
		var files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
		foreach (var file in files)
		{
			var relative = Path.GetRelativePath(sourceRoot, file);
			var outputPath = Path.Combine(targetRoot, relative);
			var outputDir = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrEmpty(outputDir))
			{
				Directory.CreateDirectory(outputDir);
			}
			File.Copy(file, outputPath, overwrite: true);
		}
	}
}
