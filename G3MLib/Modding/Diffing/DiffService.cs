using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;
using ImageMagick;

namespace G3MLib.Modding.Diffing;

public static class DiffService
{
	private const int DataFileBufferSize = 1048576;

	private static readonly string[] ResourceTypes = ResourceTypeRegistry.AllTypes;

	private static readonly string[] TextExtensions = new string[4] { ".gml", ".json", ".txt", ".xml" };

	private static readonly HashSet<string> BinaryHeavyResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sprites", "Fonts", "Sounds", "EmbeddedAudio", "EmbeddedTextures", "TexturePageItems", "TextureGroupInfo" };

	private static FileStream OpenDataReadStream(string path)
	{
		return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan);
	}

	public static async Task<DiffResult> CompareAsync(string file1Path, string file2Path, string outputPath, DiffReportMode reportMode = DiffReportMode.Standard, G3MCacheOptions? cacheOptions = null)
	{
		if (!File.Exists(file1Path))
		{
			return new DiffResult
			{
				Success = false,
				Error = "File not found: " + file1Path
			};
		}
		if (!File.Exists(file2Path))
		{
			return new DiffResult
			{
				Success = false,
				Error = "File not found: " + file2Path
			};
		}
		string ext1 = Path.GetExtension(file1Path).ToLowerInvariant();
		string ext2 = Path.GetExtension(file2Path).ToLowerInvariant();
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("# Diff Report");
		sb.AppendLine();
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
		handler.AppendLiteral("> Generated: ");
		handler.AppendFormatted(DateTime.Now, "yyyy-MM-dd HH:mm:ss");
		stringBuilder2.AppendLine(ref handler);
		sb.AppendLine();
		ResourceDiffSummary resourceSummary = null;
		bool isData1 = DataFileExtensionUtil.IsValidDataExtension(ext1);
		bool isData2 = DataFileExtensionUtil.IsValidDataExtension(ext2);
		int differenceCount;
		if ((ext1 == ".g3mpatch" || ext1 == ".zip") && isData2)
		{
			differenceCount = await ComparePatchWithDataAsync(file1Path, file2Path, sb, reportMode);
		}
		else if (isData1 && (ext2 == ".g3mpatch" || ext2 == ".zip"))
		{
			differenceCount = await ComparePatchWithDataAsync(file2Path, file1Path, sb, reportMode);
		}
		else if ((ext1 == ".g3mpatch" || ext1 == ".zip") && (ext2 == ".g3mpatch" || ext2 == ".zip"))
		{
			differenceCount = await ComparePatchesAsync(file1Path, file2Path, sb, reportMode);
		}
		else
		{
			if (!(isData1 && isData2))
			{
				sb.AppendLine("## Unsupported file combination");
				sb.AppendLine();
				stringBuilder = sb;
				StringBuilder stringBuilder3 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(26, 2, stringBuilder);
				handler.AppendLiteral("Cannot compare `");
				handler.AppendFormatted(ext1);
				handler.AppendLiteral("` with `");
				handler.AppendFormatted(ext2);
				handler.AppendLiteral("`.");
				stringBuilder3.AppendLine(ref handler);
				stringBuilder = sb;
				StringBuilder stringBuilder4 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(73, 1, stringBuilder);
				handler.AppendLiteral("Supported: ");
				handler.AppendFormatted(DataFileExtensionUtil.GetValidExtensionsDisplay());
				handler.AppendLiteral(" vs same, `.g3mpatch` vs data file, `.g3mpatch` vs `.g3mpatch`");
				stringBuilder4.AppendLine(ref handler);
				await WriteOutputAsync(outputPath, sb.ToString());
				return new DiffResult
				{
					Success = false,
					Error = $"Unsupported file combination: '{ext1}' vs '{ext2}'",
					OutputPath = outputPath
				};
			}
			resourceSummary = await CompareDataFilesAsync(file1Path, file2Path, sb, reportMode, cacheOptions);
			differenceCount = resourceSummary.TotalChanged + resourceSummary.TotalNew + resourceSummary.TotalDeleted;
		}
		await WriteOutputAsync(outputPath, sb.ToString());
		return new DiffResult
		{
			Success = true,
			DifferenceCount = differenceCount,
			OutputPath = outputPath,
			Mode = reportMode.ToString().ToLowerInvariant(),
			TotalChanged = resourceSummary?.TotalChanged,
			TotalNew = resourceSummary?.TotalNew,
			TotalDeleted = resourceSummary?.TotalDeleted,
			ByType = resourceSummary?.ByType.Where<KeyValuePair<string, DiffResourceTypeChanges>>((KeyValuePair<string, DiffResourceTypeChanges> kvp) => kvp.Value.Changed.Count > 0 || kvp.Value.New.Count > 0 || kvp.Value.Deleted.Count > 0).ToDictionary<KeyValuePair<string, DiffResourceTypeChanges>, string, DiffTypeSummary>((KeyValuePair<string, DiffResourceTypeChanges> kvp) => kvp.Key, (KeyValuePair<string, DiffResourceTypeChanges> kvp) => new DiffTypeSummary
			{
				Changed = kvp.Value.Changed.Count,
				New = kvp.Value.New.Count,
				Deleted = kvp.Value.Deleted.Count
			}, StringComparer.OrdinalIgnoreCase),
			TextDiffCount = resourceSummary?.TextDiffs.Count
		};
	}

	private static async Task WriteOutputAsync(string outputPath, string contents)
	{
		string outputDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
		{
			Directory.CreateDirectory(outputDir);
		}
		await File.WriteAllTextAsync(outputPath, contents);
	}

	private static async Task<ResourceDiffSummary> CompareDataFilesAsync(string file1Path, string file2Path, StringBuilder sb, DiffReportMode reportMode, G3MCacheOptions? cacheOptions)
	{
		LogService.SetOperation("Comparing data files");
		LogService.Progress(0, 100);
		G3MDataAnalysisCache cached1 = G3MCacheService.TryReadDataCache(file1Path, cacheOptions);
		string text = cached1?.DataInfo.Md5;
		if (text == null)
		{
			text = await HashService.ComputeFileHashAsync(file1Path);
		}
		string hash1 = text;
		LogService.Progress(3, 100);
		StringComparison comparison = (OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		bool sameFile = string.Equals(Path.GetFullPath(file1Path), Path.GetFullPath(file2Path), comparison);
		G3MDataAnalysisCache cached2 = (sameFile ? cached1 : G3MCacheService.TryReadDataCache(file2Path, cacheOptions));
		if (sameFile)
		{
			text = hash1;
		}
		else
		{
			string text2 = cached2?.DataInfo.Md5;
			if (text2 == null)
			{
				text2 = await HashService.ComputeFileHashAsync(file2Path);
			}
			text = text2;
		}
		string hash2 = text;
		LogService.Progress(6, 100);
		sb.AppendLine("## Files");
		sb.AppendLine();
		sb.AppendLine("| Property | File 1 | File 2 |");
		sb.AppendLine("|:---------|:-------|:-------|");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(22, 2, stringBuilder);
		handler.AppendLiteral("| **Path** | `");
		handler.AppendFormatted(Path.GetFileName(file1Path));
		handler.AppendLiteral("` | `");
		handler.AppendFormatted(Path.GetFileName(file2Path));
		handler.AppendLiteral("` |");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(30, 2, stringBuilder);
		handler.AppendLiteral("| **Size** | ");
		handler.AppendFormatted(new FileInfo(file1Path).Length, "N0");
		handler.AppendLiteral(" bytes | ");
		handler.AppendFormatted(new FileInfo(file2Path).Length, "N0");
		handler.AppendLiteral(" bytes |");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(21, 2, stringBuilder);
		handler.AppendLiteral("| **MD5** | `");
		handler.AppendFormatted(hash1);
		handler.AppendLiteral("` | `");
		handler.AppendFormatted(hash2);
		handler.AppendLiteral("` |");
		stringBuilder4.AppendLine(ref handler);
		sb.AppendLine();
		if (hash1 == hash2)
		{
			sb.AppendLine("Files are **identical** (same MD5 hash).");
			LogService.ProgressComplete();
			return new ResourceDiffSummary();
		}
		ResourceDiffSummary resourceDiffs = await CompareWinFilesAsync(file1Path, file2Path, hash1, hash2, cached1, cached2, reportMode, cacheOptions);
		_ = resourceDiffs.TotalChanged;
		_ = resourceDiffs.TotalNew;
		_ = resourceDiffs.TotalDeleted;
		LogService.Progress(92, 100);
		AppendSummaryTable(sb, resourceDiffs);
		AppendResourceDetails(sb, resourceDiffs, reportMode);
		if (RequiresBinaryAnalysis(resourceDiffs))
		{
			LogService.Log("[DiffService] Performing binary-level analysis...");
			AppendBinaryAnalysis(sb, file1Path, file2Path, reportMode, resourceDiffs);
		}
		LogService.Progress(100, 100);
		LogService.ProgressComplete();
		return resourceDiffs;
	}

	private static async Task<int> ComparePatchWithDataAsync(string patchPath, string dataPath, StringBuilder sb, DiffReportMode reportMode)
	{
		LogService.SetOperation("Comparing patch with data");
		LogService.Progress(0, 100);
		sb.AppendLine("## Patch vs Data Comparison");
		sb.AppendLine();
		sb.AppendLine("| | File |");
		sb.AppendLine("|:--|:-----|");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder);
		handler.AppendLiteral("| **Patch** | `");
		handler.AppendFormatted(Path.GetFileName(patchPath));
		handler.AppendLiteral("` |");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder);
		handler.AppendLiteral("| **Data** | `");
		handler.AppendFormatted(Path.GetFileName(dataPath));
		handler.AppendLiteral("` |");
		stringBuilder3.AppendLine(ref handler);
		sb.AppendLine();
		string tempDir = Path.Combine(Path.GetTempPath(), $"g3mtool_diff_{Guid.NewGuid():N}");
		string patchExtractDir = Path.Combine(tempDir, "patch");
		string dataExportDir = Path.Combine(tempDir, "data");
		try
		{
			Directory.CreateDirectory(patchExtractDir);
			Directory.CreateDirectory(dataExportDir);
			LogService.Progress(10, 100);
			await ZipFile.ExtractToDirectoryAsync(patchPath, patchExtractDir);
			LogService.Progress(20, 100);
			string manifestPath = Path.Combine(patchExtractDir, "g3mpatch.json");
			G3MPatchManifest manifest = null;
			bool manifestValid = true;
			if (!File.Exists(manifestPath))
			{
				sb.AppendLine("## Warning");
				sb.AppendLine();
				sb.AppendLine("Patch does not contain `g3mpatch.json` manifest. Using folder-based detection.");
				sb.AppendLine();
				manifestValid = false;
			}
			else
			{
				try
				{
					manifest = JsonSerializer.Deserialize<G3MPatchManifest>(await File.ReadAllTextAsync(manifestPath));
					if (manifest?.Resources == null)
					{
						sb.AppendLine("## Warning");
						sb.AppendLine();
						sb.AppendLine("Manifest has no resources. Using folder-based detection.");
						sb.AppendLine();
						manifestValid = false;
					}
				}
				catch (Exception ex)
				{
					sb.AppendLine("## Warning");
					sb.AppendLine();
					stringBuilder = sb;
					StringBuilder stringBuilder4 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(57, 1, stringBuilder);
					handler.AppendLiteral("Failed to parse manifest: ");
					handler.AppendFormatted(ex.Message);
					handler.AppendLiteral(". Using folder-based detection.");
					stringBuilder4.AppendLine(ref handler);
					sb.AppendLine();
					manifestValid = false;
				}
			}
			string key;
			HashSet<string> resourceTypesInPatch;
			if (manifest?.Resources != null && manifestValid)
			{
				HashSet<string> hashSet = new HashSet<string>();
				foreach (string key2 in manifest.Resources.Keys)
				{
					key = key2;
					hashSet.Add(key);
				}
				resourceTypesInPatch = hashSet;
			}
			else
			{
				resourceTypesInPatch = (from name in Directory.GetDirectories(patchExtractDir).Select(Path.GetFileName)
					where !string.IsNullOrEmpty(name) && ResourceTypes.Contains(name)
					select name).ToHashSet();
				if (resourceTypesInPatch.Count == 0)
				{
					sb.AppendLine("## Error");
					sb.AppendLine();
					sb.AppendLine("No valid resource folders found in patch.");
					return 0;
				}
				stringBuilder = sb;
				StringBuilder stringBuilder5 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(27, 1, stringBuilder);
				handler.AppendLiteral("Detected resource folders: ");
				handler.AppendFormatted(string.Join(", ", resourceTypesInPatch));
				stringBuilder5.AppendLine(ref handler);
				sb.AppendLine();
			}
			using (FileStream stream = OpenDataReadStream(dataPath))
			{
				using GameMakerData dataObj = GameMakerIO.Read(stream);
				ResourceExportService.ExportTypes(dataObj, dataExportDir, dataPath, resourceTypesInPatch);
			}
			GC.Collect();
			LogService.Progress(60, 100);
			ResourceDiffSummary summary = new ResourceDiffSummary();
			List<TextFileDiff> textDiffs = new List<TextFileDiff>();
			if (manifest?.Resources != null && manifestValid)
			{
				foreach (KeyValuePair<string, ResourceTypeChanges> resource in manifest.Resources)
				{
					resource.Deconstruct(out key, out var value);
					string resourceType = key;
					ResourceTypeChanges changes = value;
					string patchResDir = Path.Combine(patchExtractDir, resourceType);
					string dataResDir = Path.Combine(dataExportDir, resourceType);
					DiffResourceTypeChanges typeChanges = new DiffResourceTypeChanges();
					IEnumerable<ResourceChange> changed = changes.Changed;
					foreach (ResourceChange changed2 in changed ?? Enumerable.Empty<ResourceChange>())
					{
						if (string.IsNullOrEmpty(changed2.Name))
						{
							continue;
						}
						string patchResPath = Path.Combine(patchResDir, changed2.Name);
						string dataResPath = Path.Combine(dataResDir, changed2.Name);
						if (Directory.Exists(patchResPath) && Directory.Exists(dataResPath))
						{
							if (await AreDirectoriesDifferentAsync(patchResPath, dataResPath))
							{
								typeChanges.Changed.Add(changed2.Name);
								await CollectTextDiffsAsync(patchResPath, dataResPath, changed2.Name, resourceType, textDiffs, reportMode);
							}
						}
						else
						{
							typeChanges.Changed.Add(changed2.Name);
						}
					}
					changed = changes.New;
					foreach (ResourceChange newRes in changed ?? Enumerable.Empty<ResourceChange>())
					{
						if (!string.IsNullOrEmpty(newRes.Name))
						{
							typeChanges.New.Add(newRes.Name);
						}
					}
					summary.ByType[resourceType] = typeChanges;
					summary.TotalChanged += typeChanges.Changed.Count;
					summary.TotalNew += typeChanges.New.Count;
				}
			}
			else
			{
				foreach (string resourceType in resourceTypesInPatch)
				{
					string dataResDir = Path.Combine(patchExtractDir, resourceType);
					string patchResDir = Path.Combine(dataExportDir, resourceType);
					DiffResourceTypeChanges typeChanges2 = await CompareResourceTypeAsync(dataResDir, patchResDir);
					summary.ByType[resourceType] = typeChanges2;
					summary.TotalChanged += typeChanges2.Changed.Count;
					summary.TotalNew += typeChanges2.New.Count;
					summary.TotalDeleted += typeChanges2.Deleted.Count;
					foreach (string changedName in typeChanges2.Changed)
					{
						string patchResPath2 = Path.Combine(dataResDir, changedName);
						string dataResPath2 = Path.Combine(patchResDir, changedName);
						if (Directory.Exists(patchResPath2) && Directory.Exists(dataResPath2))
						{
							await CollectTextDiffsAsync(patchResPath2, dataResPath2, changedName, resourceType, textDiffs, reportMode);
						}
					}
				}
			}
			LogService.Progress(90, 100);
			AppendSummaryTable(sb, summary);
			AppendResourceList(sb, summary);
			AppendTextDiffs(sb, textDiffs);
			LogService.ProgressComplete();
			return summary.TotalChanged + summary.TotalNew;
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

	private static async Task<int> ComparePatchesAsync(string patch1Path, string patch2Path, StringBuilder sb, DiffReportMode reportMode)
	{
		LogService.SetOperation("Comparing patches");
		LogService.Progress(0, 100);
		sb.AppendLine("## Patches");
		sb.AppendLine();
		sb.AppendLine("| | File |");
		sb.AppendLine("|:--|:-----|");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder);
		handler.AppendLiteral("| **Patch 1** | `");
		handler.AppendFormatted(Path.GetFileName(patch1Path));
		handler.AppendLiteral("` |");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder);
		handler.AppendLiteral("| **Patch 2** | `");
		handler.AppendFormatted(Path.GetFileName(patch2Path));
		handler.AppendLiteral("` |");
		stringBuilder3.AppendLine(ref handler);
		sb.AppendLine();
		string tempDir = Path.Combine(Path.GetTempPath(), $"g3mtool_diff_{Guid.NewGuid():N}");
		string patch1Dir = Path.Combine(tempDir, "patch1");
		string patch2Dir = Path.Combine(tempDir, "patch2");
		try
		{
			Directory.CreateDirectory(patch1Dir);
			Directory.CreateDirectory(patch2Dir);
			LogService.Progress(10, 100);
			await ZipFile.ExtractToDirectoryAsync(patch1Path, patch1Dir);
			LogService.Progress(20, 100);
			await ZipFile.ExtractToDirectoryAsync(patch2Path, patch2Dir);
			LogService.Progress(30, 100);
			ResourceDiffSummary summary = new ResourceDiffSummary();
			int typeIndex = 0;
			int totalTypes = ResourceTypes.Length;
			string[] resourceTypes = ResourceTypes;
			foreach (string resourceType in resourceTypes)
			{
				string dir1 = Path.Combine(patch1Dir, resourceType);
				string dir2 = Path.Combine(patch2Dir, resourceType);
				DiffResourceTypeChanges changes = await CompareResourceTypeAsync(dir1, dir2);
				summary.ByType[resourceType] = changes;
				summary.TotalChanged += changes.Changed.Count;
				summary.TotalNew += changes.New.Count;
				summary.TotalDeleted += changes.Deleted.Count;
				foreach (string changedName in changes.Changed)
				{
					string resDir1 = Path.Combine(dir1, changedName);
					string resDir2 = Path.Combine(dir2, changedName);
					if (Directory.Exists(resDir1) && Directory.Exists(resDir2))
					{
						await CollectTextDiffsAsync(resDir1, resDir2, changedName, resourceType, summary.TextDiffs, reportMode);
					}
				}
				typeIndex++;
				LogService.Progress(30 + typeIndex * 60 / totalTypes, 100);
			}
			LogService.Progress(95, 100);
			AppendSummaryTable(sb, summary);
			AppendResourceList(sb, summary);
			AppendTextDiffs(sb, summary.TextDiffs);
			LogService.ProgressComplete();
			return summary.TotalChanged + summary.TotalNew + summary.TotalDeleted;
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

	private static void AppendSummaryTable(StringBuilder sb, ResourceDiffSummary summary)
	{
		sb.AppendLine("## Summary");
		sb.AppendLine();
		sb.AppendLine("> Comparison: **File 2** relative to **File 1**");
		sb.AppendLine("> - **Changed** = exists in both, but content differs");
		sb.AppendLine("> - **New** = exists in File 2, not in File 1");
		sb.AppendLine("> - **Deleted** = exists in File 1, not in File 2");
		sb.AppendLine();
		sb.AppendLine("| Category | Count |");
		sb.AppendLine("|:---------|------:|");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder);
		handler.AppendLiteral("| Changed | ");
		handler.AppendFormatted(summary.TotalChanged);
		handler.AppendLiteral(" |");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder);
		handler.AppendLiteral("| New | ");
		handler.AppendFormatted(summary.TotalNew);
		handler.AppendLiteral(" |");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder);
		handler.AppendLiteral("| Deleted | ");
		handler.AppendFormatted(summary.TotalDeleted);
		handler.AppendLiteral(" |");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder);
		handler.AppendLiteral("| **Total** | **");
		handler.AppendFormatted(summary.TotalChanged + summary.TotalNew + summary.TotalDeleted);
		handler.AppendLiteral("** |");
		stringBuilder5.AppendLine(ref handler);
		sb.AppendLine();
	}

	private static void AppendResourceList(StringBuilder sb, ResourceDiffSummary summary)
	{
		foreach (var (resourceType, changes) in summary.ByType)
		{
			if (changes.Changed.Count == 0 && changes.New.Count == 0 && changes.Deleted.Count == 0)
			{
				continue;
			}
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(3, 1, stringBuilder);
			handler.AppendLiteral("## ");
			handler.AppendFormatted(resourceType);
			stringBuilder2.AppendLine(ref handler);
			sb.AppendLine();
			if (changes.Changed.Count > 0)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder3 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder);
				handler.AppendLiteral("### Changed (");
				handler.AppendFormatted(changes.Changed.Count);
				handler.AppendLiteral(")");
				stringBuilder3.AppendLine(ref handler);
				sb.AppendLine();
				foreach (string name in changes.Changed)
				{
					stringBuilder = sb;
					StringBuilder stringBuilder4 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
					handler.AppendLiteral("- `");
					handler.AppendFormatted(name);
					handler.AppendLiteral("`");
					stringBuilder4.AppendLine(ref handler);
				}
				sb.AppendLine();
			}
			if (changes.New.Count > 0)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder5 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder);
				handler.AppendLiteral("### New (");
				handler.AppendFormatted(changes.New.Count);
				handler.AppendLiteral(")");
				stringBuilder5.AppendLine(ref handler);
				sb.AppendLine();
				foreach (string name2 in changes.New)
				{
					stringBuilder = sb;
					StringBuilder stringBuilder6 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
					handler.AppendLiteral("- `");
					handler.AppendFormatted(name2);
					handler.AppendLiteral("`");
					stringBuilder6.AppendLine(ref handler);
				}
				sb.AppendLine();
			}
			if (changes.Deleted.Count <= 0)
			{
				continue;
			}
			stringBuilder = sb;
			StringBuilder stringBuilder7 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder);
			handler.AppendLiteral("### Deleted (");
			handler.AppendFormatted(changes.Deleted.Count);
			handler.AppendLiteral(")");
			stringBuilder7.AppendLine(ref handler);
			sb.AppendLine();
			foreach (string name3 in changes.Deleted)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder8 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
				handler.AppendLiteral("- `");
				handler.AppendFormatted(name3);
				handler.AppendLiteral("`");
				stringBuilder8.AppendLine(ref handler);
			}
			sb.AppendLine();
		}
	}

	private static void AppendResourceDetails(StringBuilder sb, ResourceDiffSummary summary, DiffReportMode reportMode)
	{
		AppendResourceList(sb, summary);
		if (summary.TextDiffs.Count > 0)
		{
			if (reportMode == DiffReportMode.Full)
			{
				AppendTextDiffs(sb, summary.TextDiffs);
			}
			else
			{
				AppendTextDiffSummary(sb, summary.TextDiffs);
			}
		}
	}

	private static void AppendTextDiffSummary(StringBuilder sb, List<TextFileDiff> textDiffs)
	{
		if (textDiffs.Count == 0)
		{
			return;
		}
		sb.AppendLine("## Text Difference Summary");
		sb.AppendLine();
		sb.AppendLine("Standard mode lists changed text files without unified hunks. Use `--full` to include exact text/code/JSON diffs.");
		sb.AppendLine();
		sb.AppendLine("| Resource | Changed text files |");
		sb.AppendLine("|:---------|-------------------:|");
		foreach (IGrouping<string, TextFileDiff> group in (from d in textDiffs
			group d by d.ResourceType + "/" + d.ResourceName).OrderBy<IGrouping<string, TextFileDiff>, string>((IGrouping<string, TextFileDiff> g) => g.Key, StringComparer.Ordinal).Take(120))
		{
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 2, sb);
			handler.AppendLiteral("| `");
			handler.AppendFormatted(group.Key);
			handler.AppendLiteral("` | ");
			handler.AppendFormatted(group.Count());
			handler.AppendLiteral(" |");
			sb.AppendLine(ref handler);
		}
		if (textDiffs.Select((TextFileDiff d) => d.ResourceType + "/" + d.ResourceName).Distinct<string>(StringComparer.Ordinal).Count() > 120)
		{
			sb.AppendLine("| *additional resources omitted from standard report* | |");
		}
		sb.AppendLine();
	}

	private static void AppendTextDiffs(StringBuilder sb, List<TextFileDiff> textDiffs)
	{
		if (textDiffs.Count == 0)
		{
			return;
		}
		sb.AppendLine("## Text Differences");
		sb.AppendLine();
		foreach (TextFileDiff diff in textDiffs)
		{
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 3, sb);
			handler.AppendLiteral("### `");
			handler.AppendFormatted(diff.ResourceType);
			handler.AppendLiteral("/");
			handler.AppendFormatted(diff.ResourceName);
			handler.AppendLiteral("/");
			handler.AppendFormatted(diff.FileName);
			handler.AppendLiteral("`");
			sb.AppendLine(ref handler);
			sb.AppendLine();
			sb.AppendLine("```diff");
			sb.AppendLine(diff.UnifiedDiff);
			sb.AppendLine("```");
			sb.AppendLine();
		}
	}

	private static void AppendBinaryAnalysis(StringBuilder sb, string file1Path, string file2Path, DiffReportMode reportMode, ResourceDiffSummary resourceDiffs)
	{
		using (FileStream s1 = OpenDataReadStream(file1Path))
		{
			using GameMakerData data1 = GameMakerIO.Read(s1);
			using FileStream s2 = OpenDataReadStream(file2Path);
			using GameMakerData data2 = GameMakerIO.Read(s2);
			sb.AppendLine("## Binary Analysis");
			sb.AppendLine();
			sb.AppendLine("### Resource Counts");
			sb.AppendLine();
			sb.AppendLine("| Resource | File 1 | File 2 | Delta |");
			sb.AppendLine("|:---------|-------:|-------:|------:|");
			Row("Sprites", data1.Sprites?.Count ?? 0, data2.Sprites?.Count ?? 0);
			Row("Backgrounds", data1.Backgrounds?.Count ?? 0, data2.Backgrounds?.Count ?? 0);
			Row("Sounds", data1.Sounds?.Count ?? 0, data2.Sounds?.Count ?? 0);
			Row("Fonts", data1.Fonts?.Count ?? 0, data2.Fonts?.Count ?? 0);
			Row("Paths", data1.Paths?.Count ?? 0, data2.Paths?.Count ?? 0);
			Row("Scripts", data1.Scripts?.Count ?? 0, data2.Scripts?.Count ?? 0);
			Row("GameObjects", data1.GameObjects?.Count ?? 0, data2.GameObjects?.Count ?? 0);
			Row("Rooms", data1.Rooms?.Count ?? 0, data2.Rooms?.Count ?? 0);
			Row("Code", data1.Code?.Count ?? 0, data2.Code?.Count ?? 0);
			Row("Functions", data1.Functions?.Count ?? 0, data2.Functions?.Count ?? 0);
			Row("Variables", data1.Variables?.Count ?? 0, data2.Variables?.Count ?? 0);
			Row("Strings", data1.Strings?.Count ?? 0, data2.Strings?.Count ?? 0);
			Row("Timelines", data1.Timelines?.Count ?? 0, data2.Timelines?.Count ?? 0);
			Row("Shaders", data1.Shaders?.Count ?? 0, data2.Shaders?.Count ?? 0);
			Row("Extensions", data1.Extensions?.Count ?? 0, data2.Extensions?.Count ?? 0);
			Row("AudioGroups", data1.AudioGroups?.Count ?? 0, data2.AudioGroups?.Count ?? 0);
			Row("EmbeddedTextures", data1.EmbeddedTextures?.Count ?? 0, data2.EmbeddedTextures?.Count ?? 0);
			Row("TexturePageItems", data1.TexturePageItems?.Count ?? 0, data2.TexturePageItems?.Count ?? 0);
			sb.AppendLine();
			if (ResourceTypeChanged(resourceDiffs, "Sprites"))
			{
				AppendSpriteFrameAnalysis(sb, data1, data2);
			}
			CompareNamedList<GameMakerVariable>("Variables", data1.Variables, data2.Variables, (GameMakerVariable v) => v?.Name?.Content);
			CompareNamedList<GameMakerFunction>("Functions", data1.Functions, data2.Functions, (GameMakerFunction f) => f?.Name?.Content);
			CompareNamedList<GameMakerGameObject>("GameObjects", data1.GameObjects, data2.GameObjects, (GameMakerGameObject o) => o?.Name?.Content);
			CompareNamedList<GameMakerSprite>("Sprites", data1.Sprites, data2.Sprites, (GameMakerSprite gameMakerSprite) => gameMakerSprite?.Name?.Content);
			CompareNamedList<GameMakerRoom>("Rooms", data1.Rooms, data2.Rooms, (GameMakerRoom gameMakerRoom) => gameMakerRoom?.Name?.Content);
			CompareNamedList<GameMakerScript>("Scripts", data1.Scripts, data2.Scripts, (GameMakerScript gameMakerScript) => gameMakerScript?.Name?.Content);
			CompareNamedList<GameMakerSound>("Sounds", data1.Sounds, data2.Sounds, (GameMakerSound gameMakerSound) => gameMakerSound?.Name?.Content);
			CompareNamedList<GameMakerCode>("Code Entries", data1.Code, data2.Code, (GameMakerCode gameMakerCode) => gameMakerCode?.Name?.Content);
			if (ResourceTypeChanged(resourceDiffs, "Sprites") || ResourceTypeChanged(resourceDiffs, "Backgrounds") || ResourceTypeChanged(resourceDiffs, "Fonts") || ResourceTypeChanged(resourceDiffs, "TexturePageItems") || ResourceTypeChanged(resourceDiffs, "EmbeddedTextures"))
			{
				AppendTexturePageItemReferenceAnalysis(sb, data1, data2, (reportMode == DiffReportMode.Full) ? 200 : 40);
			}
			List<(string, int, int)> roomDiffs = new List<(string, int, int)>();
			if (data1.Rooms != null && data2.Rooms != null)
			{
				Dictionary<string, GameMakerRoom> rooms2ByName = new Dictionary<string, GameMakerRoom>(StringComparer.Ordinal);
				foreach (GameMakerRoom room in data2.Rooms)
				{
					string name = room?.Name?.Content;
					if (!string.IsNullOrEmpty(name) && room != null)
					{
						rooms2ByName.TryAdd(name, room);
					}
				}
				foreach (GameMakerRoom r1 in data1.Rooms)
				{
					if (r1 != null && r1.Name?.Content != null && rooms2ByName.TryGetValue(r1.Name.Content, out var r2))
					{
						int ic1 = CountInstances(r1);
						int ic2 = CountInstances(r2);
						if (ic1 != ic2)
						{
							roomDiffs.Add((r1.Name.Content, ic1, ic2));
						}
					}
				}
			}
			if (roomDiffs.Count <= 0)
			{
				return;
			}
			sb.AppendLine("### Room Instance Counts");
			sb.AppendLine();
			sb.AppendLine("| Room | File 1 | File 2 |");
			sb.AppendLine("|:-----|-------:|-------:|");
			foreach (var item in roomDiffs.Take(30))
			{
				string name2 = item.Item1;
				int c1 = item.Item2;
				int c2 = item.Item3;
				StringBuilder stringBuilder = sb;
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(12, 3, stringBuilder);
				handler.AppendLiteral("| `");
				handler.AppendFormatted(name2);
				handler.AppendLiteral("` | ");
				handler.AppendFormatted(c1);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted(c2);
				handler.AppendLiteral(" |");
				stringBuilder2.AppendLine(ref handler);
			}
			if (roomDiffs.Count > 30)
			{
				StringBuilder stringBuilder = sb;
				StringBuilder stringBuilder3 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder);
				handler.AppendLiteral("| *...and ");
				handler.AppendFormatted(roomDiffs.Count - 30);
				handler.AppendLiteral(" more* | | |");
				stringBuilder3.AppendLine(ref handler);
			}
			sb.AppendLine();
		}
		void CompareNamedList<T>(string sectionName, IList<T>? list1, IList<T>? list2, Func<T, string?> getName, int maxItems = 20) where T : notnull
		{
			maxItems = ((reportMode == DiffReportMode.Full) ? Math.Max(maxItems, 200) : maxItems);
			List<string> names1 = (from text in (list1 ?? Array.Empty<T>()).Select(getName)
				where text != null
				select text).ToList();
			List<string> names2 = (from text in (list2 ?? Array.Empty<T>()).Select(getName)
				where text != null
				select text).ToList();
			Dictionary<string, int> index1 = new Dictionary<string, int>(StringComparer.Ordinal);
			Dictionary<string, int> index2 = new Dictionary<string, int>(StringComparer.Ordinal);
			for (int i = 0; i < names1.Count; i++)
			{
				index1.TryAdd(names1[i], i);
			}
			for (int i2 = 0; i2 < names2.Count; i2++)
			{
				index2.TryAdd(names2[i2], i2);
			}
			HashSet<string> set1 = index1.Keys.ToHashSet<string>(StringComparer.Ordinal);
			HashSet<string> set2 = index2.Keys.ToHashSet<string>(StringComparer.Ordinal);
			List<string> missing = set1.Except(set2).ToList();
			List<string> extra = set2.Except(set1).ToList();
			List<string> list3 = set1.Intersect(set2).ToList();
			List<(string, int, int)> indexMismatches = new List<(string, int, int)>();
			foreach (string name3 in list3)
			{
				int i3 = index1[name3];
				int i4 = index2[name3];
				if (i3 != i4)
				{
					indexMismatches.Add((name3, i3, i4));
				}
			}
			if (missing.Count != 0 || extra.Count != 0 || indexMismatches.Count != 0)
			{
				StringBuilder stringBuilder4 = sb;
				StringBuilder stringBuilder5 = stringBuilder4;
				StringBuilder.AppendInterpolatedStringHandler handler2 = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder4);
				handler2.AppendLiteral("### ");
				handler2.AppendFormatted(sectionName);
				stringBuilder5.AppendLine(ref handler2);
				sb.AppendLine();
				if (missing.Count > 0)
				{
					stringBuilder4 = sb;
					StringBuilder stringBuilder6 = stringBuilder4;
					handler2 = new StringBuilder.AppendInterpolatedStringHandler(26, 1, stringBuilder4);
					handler2.AppendLiteral("**Missing in File 2 (");
					handler2.AppendFormatted(missing.Count);
					handler2.AppendLiteral("):** ");
					stringBuilder6.AppendLine(ref handler2);
					foreach (string n in missing.Take(maxItems))
					{
						stringBuilder4 = sb;
						StringBuilder stringBuilder7 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder4);
						handler2.AppendLiteral("- `");
						handler2.AppendFormatted(n);
						handler2.AppendLiteral("`");
						stringBuilder7.AppendLine(ref handler2);
					}
					if (missing.Count > maxItems)
					{
						stringBuilder4 = sb;
						StringBuilder stringBuilder8 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder4);
						handler2.AppendLiteral("- *...and ");
						handler2.AppendFormatted(missing.Count - maxItems);
						handler2.AppendLiteral(" more*");
						stringBuilder8.AppendLine(ref handler2);
					}
					sb.AppendLine();
				}
				if (extra.Count > 0)
				{
					stringBuilder4 = sb;
					StringBuilder stringBuilder9 = stringBuilder4;
					handler2 = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder4);
					handler2.AppendLiteral("**Extra in File 2 (");
					handler2.AppendFormatted(extra.Count);
					handler2.AppendLiteral("):** ");
					stringBuilder9.AppendLine(ref handler2);
					foreach (string n2 in extra.Take(maxItems))
					{
						stringBuilder4 = sb;
						StringBuilder stringBuilder10 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder4);
						handler2.AppendLiteral("- `");
						handler2.AppendFormatted(n2);
						handler2.AppendLiteral("`");
						stringBuilder10.AppendLine(ref handler2);
					}
					if (extra.Count > maxItems)
					{
						stringBuilder4 = sb;
						StringBuilder stringBuilder11 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder4);
						handler2.AppendLiteral("- *...and ");
						handler2.AppendFormatted(extra.Count - maxItems);
						handler2.AppendLiteral(" more*");
						stringBuilder11.AppendLine(ref handler2);
					}
					sb.AppendLine();
				}
				if (indexMismatches.Count > 0)
				{
					stringBuilder4 = sb;
					StringBuilder stringBuilder12 = stringBuilder4;
					handler2 = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder4);
					handler2.AppendLiteral("**Index mismatches (");
					handler2.AppendFormatted(indexMismatches.Count);
					handler2.AppendLiteral("):**");
					stringBuilder12.AppendLine(ref handler2);
					sb.AppendLine();
					sb.AppendLine("| Name | File 1 idx | File 2 idx |");
					sb.AppendLine("|:-----|----------:|-----------:|");
					foreach (var item2 in indexMismatches.Take(maxItems))
					{
						string name4 = item2.Item1;
						int i5 = item2.Item2;
						int i6 = item2.Item3;
						stringBuilder4 = sb;
						StringBuilder stringBuilder13 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(12, 3, stringBuilder4);
						handler2.AppendLiteral("| `");
						handler2.AppendFormatted(name4);
						handler2.AppendLiteral("` | ");
						handler2.AppendFormatted(i5);
						handler2.AppendLiteral(" | ");
						handler2.AppendFormatted(i6);
						handler2.AppendLiteral(" |");
						stringBuilder13.AppendLine(ref handler2);
					}
					if (indexMismatches.Count > maxItems)
					{
						stringBuilder4 = sb;
						StringBuilder stringBuilder14 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder4);
						handler2.AppendLiteral("| *...and ");
						handler2.AppendFormatted(indexMismatches.Count - maxItems);
						handler2.AppendLiteral(" more* | | |");
						stringBuilder14.AppendLine(ref handler2);
					}
					sb.AppendLine();
				}
			}
		}
		static int CountInstances(GameMakerRoom gameMakerRoom)
		{
			int count = 0;
			IEnumerable<GameMakerRoom.Layer> layers = gameMakerRoom.Layers;
			foreach (GameMakerRoom.Layer layer in layers ?? Enumerable.Empty<GameMakerRoom.Layer>())
			{
				if (layer.InstancesData?.Instances != null)
				{
					count += layer.InstancesData.Instances.Count;
				}
			}
			return count;
		}
		void Row(string value, int num, int num2)
		{
			string delta = ((num == num2) ? "=" : $"{num2 - num:+#;-#;0}");
			StringBuilder stringBuilder4 = sb;
			StringBuilder.AppendInterpolatedStringHandler handler2 = new StringBuilder.AppendInterpolatedStringHandler(13, 4, stringBuilder4);
			handler2.AppendLiteral("| ");
			handler2.AppendFormatted(value);
			handler2.AppendLiteral(" | ");
			handler2.AppendFormatted(num);
			handler2.AppendLiteral(" | ");
			handler2.AppendFormatted(num2);
			handler2.AppendLiteral(" | ");
			handler2.AppendFormatted(delta);
			handler2.AppendLiteral(" |");
			stringBuilder4.AppendLine(ref handler2);
		}
	}

	private static bool ResourceTypeChanged(ResourceDiffSummary summary, string resourceType)
	{
		if (summary.ByType.TryGetValue(resourceType, out DiffResourceTypeChanges changes))
		{
			if (changes.Changed.Count <= 0 && changes.New.Count <= 0)
			{
				return changes.Deleted.Count > 0;
			}
			return true;
		}
		return false;
	}

	private static bool RequiresBinaryAnalysis(ResourceDiffSummary summary)
	{
		return summary.ByType.Keys.Any((string resourceType) => !resourceType.Equals("GeneralInfo", StringComparison.OrdinalIgnoreCase) && ResourceTypeChanged(summary, resourceType));
	}

	private static void AppendTexturePageItemReferenceAnalysis(StringBuilder sb, GameMakerData data1, GameMakerData data2, int maxRows)
	{
		Dictionary<int, string> refs1 = BuildTexturePageItemReferenceMap(data1);
		Dictionary<int, string> refs2 = BuildTexturePageItemReferenceMap(data2);
		List<string> rows = new List<string>();
		foreach (int key in from k in refs1.Keys.Union(refs2.Keys)
			orderby k
			select k)
		{
			refs1.TryGetValue(key, out var left);
			refs2.TryGetValue(key, out var right);
			if (!string.Equals(left, right, StringComparison.Ordinal))
			{
				rows.Add($"| {key} | `{left ?? "<missing>"}` | `{right ?? "<missing>"}` |");
				if (rows.Count >= maxRows)
				{
					break;
				}
			}
		}
		if (rows.Count == 0)
		{
			return;
		}
		sb.AppendLine("### TexturePageItem References");
		sb.AppendLine();
		sb.AppendLine("| TPI index | File 1 refs | File 2 refs |");
		sb.AppendLine("|----------:|:------------|:------------|");
		foreach (string row in rows)
		{
			sb.AppendLine(row);
		}
		if (refs1.Keys.Union(refs2.Keys).Count() > maxRows)
		{
			sb.AppendLine("| *additional changed TPI refs omitted* | | |");
		}
		sb.AppendLine();
	}

	private static Dictionary<int, string> BuildTexturePageItemReferenceMap(GameMakerData data)
	{
		Dictionary<int, List<string>> refs = new Dictionary<int, List<string>>();
		Dictionary<GameMakerTexturePageItem, int> indices = new Dictionary<GameMakerTexturePageItem, int>(ReferenceEqualityComparer.Instance);
		for (int i = 0; i < data.TexturePageItems.Count; i++)
		{
			GameMakerTexturePageItem item = data.TexturePageItems[i];
			if (item != null)
			{
				indices.TryAdd(item, i);
			}
		}
		foreach (GameMakerSprite sprite in data.Sprites ?? new List<GameMakerSprite>())
		{
			string name = sprite?.Name?.Content;
			if (sprite?.Textures != null && !string.IsNullOrWhiteSpace(name))
			{
				for (int j = 0; j < sprite.Textures.Count; j++)
				{
					Add(sprite.Textures[j]?.Texture, $"sprite:{name}[{j}]");
				}
			}
		}
		foreach (GameMakerBackground bg in data.Backgrounds ?? new List<GameMakerBackground>())
		{
			Add(bg?.Texture, "background:" + (bg?.Name?.Content ?? "?"));
		}
		foreach (GameMakerFont font in data.Fonts ?? new List<GameMakerFont>())
		{
			Add(font?.Texture, "font:" + (font?.Name?.Content ?? "?"));
		}
		return refs.ToDictionary<KeyValuePair<int, List<string>>, int, string>((KeyValuePair<int, List<string>> kvp) => kvp.Key, (KeyValuePair<int, List<string>> kvp) => string.Join(", ", kvp.Value.OrderBy<string, string>((string v) => v, StringComparer.Ordinal).Take(8)));
		void Add(GameMakerTexturePageItem? tpi, string owner)
		{
			if (tpi != null && indices.TryGetValue(tpi, out var index))
			{
				if (!refs.TryGetValue(index, out List<string> owners))
				{
					owners = new List<string>();
					refs[index] = owners;
				}
				owners.Add(owner);
			}
		}
	}

	private static void AppendSpriteFrameAnalysis(StringBuilder sb, GameMakerData data1, GameMakerData data2)
	{
		if (data1.Sprites == null || data2.Sprites == null)
		{
			return;
		}
		Dictionary<string, GameMakerSprite> sprites1 = data1.Sprites.Where((GameMakerSprite s) => s?.Name?.Content != null).ToDictionary<GameMakerSprite, string>((GameMakerSprite s) => s.Name.Content, StringComparer.Ordinal);
		Dictionary<string, GameMakerSprite> sprites2 = data2.Sprites.Where((GameMakerSprite s) => s?.Name?.Content != null).ToDictionary<GameMakerSprite, string>((GameMakerSprite s) => s.Name.Content, StringComparer.Ordinal);
		List<string> rows = new List<string>();
		using TextureWorker worker = new TextureWorker();
		foreach (string name in sprites1.Keys.Intersect<string>(sprites2.Keys, StringComparer.Ordinal).OrderBy<string, string>((string n) => n, StringComparer.Ordinal))
		{
			GameMakerSprite left = sprites1[name];
			GameMakerSprite right = sprites2[name];
			int minFrames = Math.Min(left.Textures.Count, right.Textures.Count);
			int changedFrames = 0;
			for (int i = 0; i < minFrames; i++)
			{
				if (SpriteFrameHash(worker, left, i) != SpriteFrameHash(worker, right, i))
				{
					changedFrames++;
				}
			}
			int addedFrames = Math.Max(0, right.Textures.Count - left.Textures.Count);
			int removedFrames = Math.Max(0, left.Textures.Count - right.Textures.Count);
			bool metadataChanged = left.Width != right.Width || left.Height != right.Height || left.OriginX != right.OriginX || left.OriginY != right.OriginY || left.MarginLeft != right.MarginLeft || left.MarginTop != right.MarginTop || left.MarginRight != right.MarginRight || left.MarginBottom != right.MarginBottom || left.BBoxMode != right.BBoxMode || left.Transparent != right.Transparent || left.Smooth != right.Smooth || left.Preload != right.Preload;
			if (changedFrames != 0 || addedFrames != 0 || removedFrames != 0 || metadataChanged)
			{
				rows.Add($"| `{name}` | {left.Textures.Count} -> {right.Textures.Count} | {changedFrames} | {addedFrames} | {removedFrames} | {metadataChanged} |");
				if (rows.Count >= 80)
				{
					break;
				}
			}
		}
		if (rows.Count == 0)
		{
			return;
		}
		sb.AppendLine("### Sprite Frame Differences");
		sb.AppendLine();
		sb.AppendLine("| Sprite | Frames | Changed Frames | Added Frames | Removed Frames | Metadata Changed |");
		sb.AppendLine("|:-------|:-------|---------------:|-------------:|---------------:|:-----------------|");
		foreach (string row in rows)
		{
			sb.AppendLine(row);
		}
		sb.AppendLine();
	}

	private static string SpriteFrameHash(TextureWorker worker, GameMakerSprite sprite, int frameIndex)
	{
		GameMakerTexturePageItem tpi = sprite.Textures.ElementAtOrDefault(frameIndex)?.Texture;
		if (tpi == null)
		{
			return "<null>";
		}
		try
		{
			using IMagickImage<byte> image = worker.GetTextureFor(tpi, $"{sprite.Name?.Content ?? "sprite"}_{frameIndex}");
			return Convert.ToHexStringLower(SHA256.HashData(image.ToByteArray(MagickFormat.Png)));
		}
		catch
		{
			return "<decode-error>";
		}
	}

	private static async Task CollectTextDiffsAsync(string dir1, string dir2, string resourceName, string resourceType, List<TextFileDiff> textDiffs, DiffReportMode reportMode)
	{
		if (reportMode == DiffReportMode.Standard && BinaryHeavyResourceTypes.Contains(resourceType))
		{
			return;
		}
		string[] files1 = Directory.GetFiles(dir1, "*", SearchOption.AllDirectories);
		string[] array = files1;
		foreach (string file1 in array)
		{
			string ext = Path.GetExtension(file1).ToLowerInvariant();
			if (!TextExtensions.Contains(ext))
			{
				continue;
			}
			string relPath = Path.GetRelativePath(dir1, file1);
			string file2 = Path.Combine(dir2, relPath);
			if (File.Exists(file2))
			{
				string content1 = await File.ReadAllTextAsync(file1);
				string content2 = await File.ReadAllTextAsync(file2);
				if (content1 != content2)
				{
					textDiffs.Add(new TextFileDiff
					{
						ResourceType = resourceType,
						ResourceName = resourceName,
						FileName = relPath,
						UnifiedDiff = ((reportMode == DiffReportMode.Full) ? GenerateUnifiedDiff(content1, content2, relPath) : "")
					});
				}
			}
		}
	}

	private static string GenerateUnifiedDiff(string content1, string content2, string fileName)
	{
		string[] lines1 = content1.Split('\n');
		string[] lines2 = content2.Split('\n');
		StringBuilder sb = new StringBuilder();
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("--- a/");
		handler.AppendFormatted(fileName);
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("+++ b/");
		handler.AppendFormatted(fileName);
		stringBuilder3.AppendLine(ref handler);
		int i = 0;
		int j = 0;
		int contextLines = 3;
		List<(int, int, List<string>)> hunks = new List<(int, int, List<string>)>();
		List<string> currentHunk = new List<string>();
		int hunkStart1 = 0;
		int hunkStart2 = 0;
		bool inHunk = false;
		while (i < lines1.Length || j < lines2.Length)
		{
			if (i < lines1.Length && j < lines2.Length && lines1[i].TrimEnd('\r') == lines2[j].TrimEnd('\r'))
			{
				if (inHunk)
				{
					currentHunk.Add(" " + lines1[i].TrimEnd('\r'));
				}
				i++;
				j++;
			}
			else
			{
				if (!inHunk)
				{
					inHunk = true;
					hunkStart1 = Math.Max(0, i - contextLines);
					hunkStart2 = Math.Max(0, j - contextLines);
					for (int k = hunkStart1; k < i; k++)
					{
						currentHunk.Add(" " + lines1[k].TrimEnd('\r'));
					}
				}
				if (i < lines1.Length)
				{
					currentHunk.Add("-" + lines1[i].TrimEnd('\r'));
					i++;
				}
				if (j < lines2.Length && (i >= lines1.Length || lines1[Math.Min(i, lines1.Length - 1)].TrimEnd('\r') != lines2[j].TrimEnd('\r')))
				{
					currentHunk.Add("+" + lines2[j].TrimEnd('\r'));
					j++;
				}
			}
			if (!inHunk || i >= lines1.Length || j >= lines2.Length || !(lines1[i].TrimEnd('\r') == lines2[j].TrimEnd('\r')))
			{
				continue;
			}
			int matchCount = 0;
			int ti = i;
			int tj = j;
			while (ti < lines1.Length && tj < lines2.Length && lines1[ti].TrimEnd('\r') == lines2[tj].TrimEnd('\r') && matchCount < contextLines * 2)
			{
				matchCount++;
				ti++;
				tj++;
			}
			if (matchCount < contextLines * 2 && (ti < lines1.Length || tj < lines2.Length))
			{
				continue;
			}
			for (int l = 0; l < Math.Min(contextLines, matchCount); l++)
			{
				if (i + l < lines1.Length)
				{
					currentHunk.Add(" " + lines1[i + l].TrimEnd('\r'));
				}
			}
			hunks.Add((hunkStart1, hunkStart2, currentHunk.ToList()));
			currentHunk.Clear();
			inHunk = false;
			i += Math.Min(contextLines, matchCount);
			j += Math.Min(contextLines, matchCount);
		}
		if (currentHunk.Count > 0)
		{
			hunks.Add((hunkStart1, hunkStart2, currentHunk));
		}
		foreach (var item2 in hunks)
		{
			int start1 = item2.Item1;
			int start2 = item2.Item2;
			List<string> item = item2.Item3;
			int removed = item.Count((string text) => text.StartsWith('-'));
			int added = item.Count((string text) => text.StartsWith('+'));
			int context = item.Count((string text) => text.StartsWith(' '));
			stringBuilder = sb;
			StringBuilder stringBuilder4 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(11, 4, stringBuilder);
			handler.AppendLiteral("@@ -");
			handler.AppendFormatted(start1 + 1);
			handler.AppendLiteral(",");
			handler.AppendFormatted(removed + context);
			handler.AppendLiteral(" +");
			handler.AppendFormatted(start2 + 1);
			handler.AppendLiteral(",");
			handler.AppendFormatted(added + context);
			handler.AppendLiteral(" @@");
			stringBuilder4.AppendLine(ref handler);
			foreach (string line in item)
			{
				sb.AppendLine(line);
			}
		}
		return sb.ToString().TrimEnd();
	}

	private static async Task<ResourceDiffSummary> CompareWinFilesAsync(string file1Path, string file2Path, string file1Md5, string file2Md5, G3MDataAnalysisCache? cached1, G3MDataAnalysisCache? cached2, DiffReportMode reportMode, G3MCacheOptions? cacheOptions)
	{
		ResourceDiffSummary summary = new ResourceDiffSummary();
		string tempDir = Path.Combine(Path.GetTempPath(), $"g3mtool_diff_{Guid.NewGuid():N}");
		string export1Dir = Path.Combine(tempDir, "file1");
		string export2Dir = Path.Combine(tempDir, "file2");
		try
		{
			Stopwatch diffSw = Stopwatch.StartNew();
			Stopwatch diffPhaseSw = new Stopwatch();
			LogService.Log("[DiffService] Hashing resources from both files...");
			LogService.Progress(10, 100);
			diffPhaseSw.Restart();
			Dictionary<string, Dictionary<string, string>> hashes1;
			bool shouldAppend;
			if (cached1 != null)
			{
				hashes1 = cached1.ResourceHashes;
				LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(32, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler.AppendLiteral("[Timing] File1 cache: ");
					handler.AppendFormatted(diffPhaseSw.Elapsed.TotalSeconds, "F1");
					handler.AppendLiteral("s, RAM: ");
					handler.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler.AppendLiteral("MB");
				}
				LogService.Log(ref handler);
			}
			else
			{
				using FileStream stream = OpenDataReadStream(file1Path);
				using GameMakerData data1 = GameMakerIO.Read(stream);
				TimeSpan loadTime = diffPhaseSw.Elapsed;
				diffPhaseSw.Restart();
				hashes1 = ResourceHashService.HashAll(data1);
				DataFileInfo info = new DataFileInfo
				{
					Filename = Path.GetFileName(file1Path),
					Size = new FileInfo(file1Path).Length,
					Md5 = file1Md5,
					BytecodeVersion = (data1.GeneralInfo?.BytecodeVersion ?? 0),
					GmsVersion = GeneralInfoUtil.GetVersionDisplay(data1.GeneralInfo),
					GeneralInfo = GeneralInfoUtil.ExtractGeneralInfo(data1)
				};
				await G3MCacheService.WriteDataCacheAsync(file1Path, info, hashes1, PatchService.GetResourceNameCountsForReuse(data1), PatchService.GetOrderSensitiveResourceNamesForReuse(data1), cacheOptions);
				LogInterpolatedStringHandler handler2 = new LogInterpolatedStringHandler(40, 3, out shouldAppend);
				if (shouldAppend)
				{
					handler2.AppendLiteral("[Timing] File1 load: ");
					handler2.AppendFormatted(loadTime.TotalSeconds, "F1");
					handler2.AppendLiteral("s, hash: ");
					handler2.AppendFormatted(diffPhaseSw.Elapsed.TotalSeconds, "F1");
					handler2.AppendLiteral("s, RAM: ");
					handler2.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler2.AppendLiteral("MB");
				}
				LogService.Log(ref handler2);
			}
			GC.Collect();
			LogService.Progress(25, 100);
			diffPhaseSw.Restart();
			Dictionary<string, Dictionary<string, string>> hashes2;
			if (cached2 != null)
			{
				hashes2 = cached2.ResourceHashes;
				LogInterpolatedStringHandler handler3 = new LogInterpolatedStringHandler(32, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler3.AppendLiteral("[Timing] File2 cache: ");
					handler3.AppendFormatted(diffPhaseSw.Elapsed.TotalSeconds, "F1");
					handler3.AppendLiteral("s, RAM: ");
					handler3.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler3.AppendLiteral("MB");
				}
				LogService.Log(ref handler3);
			}
			else
			{
				using FileStream stream = OpenDataReadStream(file2Path);
				using GameMakerData data1 = GameMakerIO.Read(stream);
				TimeSpan loadTime = diffPhaseSw.Elapsed;
				diffPhaseSw.Restart();
				hashes2 = ResourceHashService.HashAll(data1);
				DataFileInfo info2 = new DataFileInfo
				{
					Filename = Path.GetFileName(file2Path),
					Size = new FileInfo(file2Path).Length,
					Md5 = file2Md5,
					BytecodeVersion = (data1.GeneralInfo?.BytecodeVersion ?? 0),
					GmsVersion = GeneralInfoUtil.GetVersionDisplay(data1.GeneralInfo),
					GeneralInfo = GeneralInfoUtil.ExtractGeneralInfo(data1)
				};
				await G3MCacheService.WriteDataCacheAsync(file2Path, info2, hashes2, PatchService.GetResourceNameCountsForReuse(data1), PatchService.GetOrderSensitiveResourceNamesForReuse(data1), cacheOptions);
				LogInterpolatedStringHandler handler4 = new LogInterpolatedStringHandler(40, 3, out shouldAppend);
				if (shouldAppend)
				{
					handler4.AppendLiteral("[Timing] File2 load: ");
					handler4.AppendFormatted(loadTime.TotalSeconds, "F1");
					handler4.AppendLiteral("s, hash: ");
					handler4.AppendFormatted(diffPhaseSw.Elapsed.TotalSeconds, "F1");
					handler4.AppendLiteral("s, RAM: ");
					handler4.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
					handler4.AppendLiteral("MB");
				}
				LogService.Log(ref handler4);
			}
			GC.Collect();
			LogService.Progress(40, 100);
			diffPhaseSw.Restart();
			List<string> changedTypes = new List<string>();
			string[] resourceTypes = ResourceTypes;
			foreach (string resourceType in resourceTypes)
			{
				Dictionary<string, string> h1 = hashes1.GetValueOrDefault(resourceType) ?? new Dictionary<string, string>();
				Dictionary<string, string> h2 = hashes2.GetValueOrDefault(resourceType) ?? new Dictionary<string, string>();
				DiffResourceTypeChanges changes = new DiffResourceTypeChanges();
				HashSet<string> names1 = h1.Keys.ToHashSet();
				HashSet<string> names2 = h2.Keys.ToHashSet();
				foreach (string name in names2.Except(names1))
				{
					changes.New.Add(name);
				}
				foreach (string name2 in names1.Except(names2))
				{
					changes.Deleted.Add(name2);
				}
				foreach (string name3 in names1.Intersect(names2))
				{
					if (h1[name3] != h2[name3])
					{
						changes.Changed.Add(name3);
					}
				}
				summary.ByType[resourceType] = changes;
				summary.TotalChanged += changes.Changed.Count;
				summary.TotalNew += changes.New.Count;
				summary.TotalDeleted += changes.Deleted.Count;
				if (changes.Changed.Count > 0 || changes.New.Count > 0 || changes.Deleted.Count > 0)
				{
					changedTypes.Add(resourceType);
				}
			}
			LogInterpolatedStringHandler handler5 = new LogInterpolatedStringHandler(45, 3, out shouldAppend);
			if (shouldAppend)
			{
				handler5.AppendLiteral("[Timing] Hash comparison: ");
				handler5.AppendFormatted(diffPhaseSw.Elapsed.TotalSeconds, "F1");
				handler5.AppendLiteral("s, changed types: ");
				handler5.AppendFormatted(changedTypes.Count);
				handler5.AppendLiteral("/");
				handler5.AppendFormatted(ResourceTypes.Length);
			}
			LogService.Log(ref handler5);
			LogService.Progress(50, 100);
			List<string> textDiffTypes = ((reportMode == DiffReportMode.Full) ? changedTypes.ToList() : changedTypes.Where((string type) => !BinaryHeavyResourceTypes.Contains(type)).ToList());
			if (textDiffTypes.Count > 0)
			{
				Directory.CreateDirectory(export1Dir);
				Directory.CreateDirectory(export2Dir);
				diffPhaseSw.Restart();
				LogInterpolatedStringHandler handler6 = new LogInterpolatedStringHandler(59, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler6.AppendLiteral("[DiffService] Exporting ");
					handler6.AppendFormatted(textDiffTypes.Count);
					handler6.AppendLiteral("/");
					handler6.AppendFormatted(changedTypes.Count);
					handler6.AppendLiteral(" changed type(s) for text diffs...");
				}
				LogService.Log(ref handler6);
				using (FileStream stream2 = OpenDataReadStream(file1Path))
				{
					using GameMakerData data2 = GameMakerIO.Read(stream2);
					ResourceExportService.ExportTypes(data2, export1Dir, file1Path, textDiffTypes);
				}
				GC.Collect();
				using (FileStream stream3 = OpenDataReadStream(file2Path))
				{
					using GameMakerData data3 = GameMakerIO.Read(stream3);
					ResourceExportService.ExportTypes(data3, export2Dir, file2Path, textDiffTypes);
				}
				GC.Collect();
				LogInterpolatedStringHandler handler7 = new LogInterpolatedStringHandler(42, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler7.AppendLiteral("[Timing] Selective export (");
					handler7.AppendFormatted(textDiffTypes.Count);
					handler7.AppendLiteral(" text types): ");
					handler7.AppendFormatted(diffPhaseSw.Elapsed.TotalSeconds, "F1");
					handler7.AppendLiteral("s");
				}
				LogService.Log(ref handler7);
				LogService.Progress(80, 100);
				diffPhaseSw.Restart();
				int typeIndex = 0;
				int totalTypes = textDiffTypes.Count;
				foreach (string resourceType2 in textDiffTypes)
				{
					string dir1 = Path.Combine(export1Dir, resourceType2);
					string dir2 = Path.Combine(export2Dir, resourceType2);
					DiffResourceTypeChanges changes2 = summary.ByType[resourceType2];
					foreach (string changedName in changes2.Changed)
					{
						string resDir1 = Path.Combine(dir1, changedName);
						string resDir2 = Path.Combine(dir2, changedName);
						if (Directory.Exists(resDir1) && Directory.Exists(resDir2))
						{
							await CollectTextDiffsAsync(resDir1, resDir2, changedName, resourceType2, summary.TextDiffs, reportMode);
						}
					}
					typeIndex++;
					LogService.Progress(80 + typeIndex * 15 / totalTypes, 100);
				}
				LogInterpolatedStringHandler handler8 = new LogInterpolatedStringHandler(32, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler8.AppendLiteral("[Timing] Text diff collection: ");
					handler8.AppendFormatted(diffPhaseSw.Elapsed.TotalSeconds, "F1");
					handler8.AppendLiteral("s");
				}
				LogService.Log(ref handler8);
			}
			else if (changedTypes.Count > 0)
			{
				LogInterpolatedStringHandler handler9 = new LogInterpolatedStringHandler(73, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler9.AppendLiteral("[DiffService] Text diff export skipped; changed types are binary-heavy (");
					handler9.AppendFormatted(string.Join(", ", changedTypes));
					handler9.AppendLiteral(")");
				}
				LogService.Log(ref handler9);
			}
			diffSw.Stop();
			LogInterpolatedStringHandler handler10 = new LogInterpolatedStringHandler(38, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler10.AppendLiteral("[Timing] === DIFF TOTAL: ");
				handler10.AppendFormatted(diffSw.Elapsed.TotalSeconds, "F1");
				handler10.AppendLiteral("s === RAM: ");
				handler10.AppendFormatted(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
				handler10.AppendLiteral("MB");
			}
			LogService.Log(ref handler10);
			return summary;
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

	private static async Task<DiffResourceTypeChanges> CompareResourceTypeAsync(string dir1, string dir2)
	{
		DiffResourceTypeChanges changes = new DiffResourceTypeChanges();
		HashSet<string> resources1 = (Directory.Exists(dir1) ? (from n in Directory.GetDirectories(dir1).Select(Path.GetFileName)
			where n != null
			select n).ToHashSet() : new HashSet<string>());
		HashSet<string> resources2 = (Directory.Exists(dir2) ? (from n in Directory.GetDirectories(dir2).Select(Path.GetFileName)
			where n != null
			select n).ToHashSet() : new HashSet<string>());
		foreach (string name in resources2.Except(resources1))
		{
			if (!string.IsNullOrEmpty(name))
			{
				changes.New.Add(name);
			}
		}
		foreach (string name2 in resources1.Except(resources2))
		{
			if (!string.IsNullOrEmpty(name2))
			{
				changes.Deleted.Add(name2);
			}
		}
		foreach (string name3 in resources1.Intersect(resources2))
		{
			if (!string.IsNullOrEmpty(name3))
			{
				string dir3 = Path.Combine(dir1, name3);
				string path2 = Path.Combine(dir2, name3);
				if (await AreDirectoriesDifferentAsync(dir3, path2))
				{
					changes.Changed.Add(name3);
				}
			}
		}
		return changes;
	}

	private static async Task<bool> AreDirectoriesDifferentAsync(string dir1, string dir2)
	{
		HashSet<string> files1 = (from path in Directory.GetFiles(dir1, "*", SearchOption.AllDirectories)
			select Path.GetRelativePath(dir1, path)).ToHashSet();
		HashSet<string> files2 = (from path in Directory.GetFiles(dir2, "*", SearchOption.AllDirectories)
			select Path.GetRelativePath(dir2, path)).ToHashSet();
		if (!files1.SetEquals(files2))
		{
			return true;
		}
		foreach (string relPath in files1)
		{
			string f1 = Path.Combine(dir1, relPath);
			string f2 = Path.Combine(dir2, relPath);
			if (!HashService.FileSizesMatch(f1, f2))
			{
				return true;
			}
			if (await HashService.ComputeFileHashAsync(f1) != await HashService.ComputeFileHashAsync(f2))
			{
				return true;
			}
		}
		return false;
	}
}
