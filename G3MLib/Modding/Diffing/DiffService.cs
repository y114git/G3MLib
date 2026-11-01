using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;
using ImageMagick;

namespace G3MLib.Modding.Diffing;

public static class DiffService
{
	private static readonly string[] ResourceTypes = ResourceTypeRegistry.AllTypes;

	private static readonly string[] TextExtensions = [".gml", ".json", ".txt", ".xml"];

	private static readonly HashSet<string> BinaryHeavyResourceTypes = new(StringComparer.OrdinalIgnoreCase) { "Sprites", "Fonts", "Sounds", "EmbeddedAudio", "EmbeddedTextures", "TexturePageItems", "TextureGroupInfo" };

	private static FileStream OpenDataReadStream(string path)
	{
		return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan);
	}

	public static Task<DiffResult> CompareAsync(string file1Path, string file2Path, string outputPath, DiffReportMode reportMode, G3MCacheOptions? cacheOptions)
		=> CompareAsync(file1Path, file2Path, outputPath, reportMode, cacheOptions, CancellationToken.None);

	/// <summary>Compares two files. A cancelled call throws <see cref="OperationCanceledException"/>, leaves an existing report untouched and removes its temporary files.</summary>
	public static async Task<DiffResult> CompareAsync(string file1Path, string file2Path, string outputPath, DiffReportMode reportMode = DiffReportMode.Standard, G3MCacheOptions? cacheOptions = null, CancellationToken cancellationToken = default)
	{
		using var cancellationScope = ModdingCancellation.Begin(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			return await CompareCoreAsync(file1Path, file2Path, outputPath, reportMode, cacheOptions).ConfigureAwait(false);
		}
		catch (Exception ex) when (ModdingCancellation.IsCancellation(ex))
		{
			ModdingCancellation.Rethrow(ex, cancellationToken);
			throw;
		}
	}

	private static async Task<DiffResult> CompareCoreAsync(string file1Path, string file2Path, string outputPath, DiffReportMode reportMode, G3MCacheOptions? cacheOptions)
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
		var ext1 = Path.GetExtension(file1Path).ToLowerInvariant();
		var ext2 = Path.GetExtension(file2Path).ToLowerInvariant();
		var sb = new StringBuilder();
		sb.AppendLine("# Diff Report");
		sb.AppendLine();
		var stringBuilder = sb;
		var stringBuilder2 = stringBuilder;
		var handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("> Generated: ");
		handler.AppendFormatted(DateTime.Now, "yyyy-MM-dd HH:mm:ss");
		stringBuilder2.AppendLine(CultureInfo.InvariantCulture, ref handler);
		sb.AppendLine();
		ResourceDiffSummary? resourceSummary = null;
		var isData1 = DataFileExtensionUtil.IsValidDataExtension(ext1);
		var isData2 = DataFileExtensionUtil.IsValidDataExtension(ext2);
		int differenceCount;
		if ((ext1 == ".g3mpatch" || ext1 == ".zip") && isData2)
		{
			differenceCount = await ComparePatchWithDataAsync(file1Path, file2Path, sb, reportMode).ConfigureAwait(false);
		}
		else if (isData1 && (ext2 == ".g3mpatch" || ext2 == ".zip"))
		{
			differenceCount = await ComparePatchWithDataAsync(file2Path, file1Path, sb, reportMode).ConfigureAwait(false);
		}
		else if ((ext1 == ".g3mpatch" || ext1 == ".zip") && (ext2 == ".g3mpatch" || ext2 == ".zip"))
		{
			differenceCount = await ComparePatchesAsync(file1Path, file2Path, sb, reportMode).ConfigureAwait(false);
		}
		else
		{
			if (!(isData1 && isData2))
			{
				sb.AppendLine("## Unsupported file combination");
				sb.AppendLine();
				stringBuilder = sb;
				var stringBuilder3 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(26, 2, stringBuilder, CultureInfo.InvariantCulture);
				handler.AppendLiteral("Cannot compare `");
				handler.AppendFormatted(ext1);
				handler.AppendLiteral("` with `");
				handler.AppendFormatted(ext2);
				handler.AppendLiteral("`.");
				stringBuilder3.AppendLine(CultureInfo.InvariantCulture, ref handler);
				stringBuilder = sb;
				var stringBuilder4 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(73, 1, stringBuilder, CultureInfo.InvariantCulture);
				handler.AppendLiteral("Supported: ");
				handler.AppendFormatted(DataFileExtensionUtil.GetValidExtensionsDisplay());
				handler.AppendLiteral(" vs same, `.g3mpatch` vs data file, `.g3mpatch` vs `.g3mpatch`");
				stringBuilder4.AppendLine(CultureInfo.InvariantCulture, ref handler);
				await WriteOutputAsync(outputPath, sb.ToString()).ConfigureAwait(false);
				return new DiffResult
				{
					Success = false,
					Error = $"Unsupported file combination: '{ext1}' vs '{ext2}'",
					OutputPath = outputPath
				};
			}
			resourceSummary = await CompareDataFilesAsync(file1Path, file2Path, sb, reportMode, cacheOptions).ConfigureAwait(false);
			differenceCount = resourceSummary.TotalChanged + resourceSummary.TotalNew + resourceSummary.TotalDeleted;
		}
		await WriteOutputAsync(outputPath, sb.ToString()).ConfigureAwait(false);
		return new DiffResult
		{
			Success = true,
			DifferenceCount = differenceCount,
			OutputPath = outputPath,
			Mode = reportMode.ToString().ToLowerInvariant(),
			TotalChanged = resourceSummary?.TotalChanged,
			TotalNew = resourceSummary?.TotalNew,
			TotalDeleted = resourceSummary?.TotalDeleted,
			ByType = resourceSummary?.ByType.Where(kvp => kvp.Value.Changed.Count > 0 || kvp.Value.New.Count > 0 || kvp.Value.Deleted.Count > 0).ToDictionary(kvp => kvp.Key, kvp => new DiffTypeSummary
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
		var outputDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
		{
			Directory.CreateDirectory(outputDir);
		}
		ModdingCancellation.Throw();
		var stagedPath = Path.GetFullPath(outputPath) + $".{Guid.NewGuid():N}.tmp";
		try
		{
			await File.WriteAllTextAsync(stagedPath, contents).ConfigureAwait(false);
			File.Move(stagedPath, outputPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(stagedPath)) File.Delete(stagedPath);
		}
	}

	private static async Task<ResourceDiffSummary> CompareDataFilesAsync(string file1Path, string file2Path, StringBuilder sb, DiffReportMode reportMode, G3MCacheOptions? cacheOptions)
	{
		LogService.SetOperation("Comparing data files");
		LogService.Progress(0, 100);
		var cached1 = G3MCacheService.TryReadDataCache(file1Path, cacheOptions);
		var text = (cached1?.DataInfo.Md5) ?? await HashService.ComputeFileHashAsync(file1Path).ConfigureAwait(false);
		var hash1 = text;
		LogService.Progress(3, 100);
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		var sameFile = string.Equals(Path.GetFullPath(file1Path), Path.GetFullPath(file2Path), comparison);
		var cached2 = sameFile ? cached1 : G3MCacheService.TryReadDataCache(file2Path, cacheOptions);
		if (sameFile)
		{
			text = hash1;
		}
		else
		{
			var text2 = (cached2?.DataInfo.Md5) ?? await HashService.ComputeFileHashAsync(file2Path).ConfigureAwait(false);
			text = text2;
		}
		var hash2 = text;
		LogService.Progress(6, 100);
		sb.AppendLine("## Files");
		sb.AppendLine();
		sb.AppendLine("| Property | File 1 | File 2 |");
		sb.AppendLine("|:---------|:-------|:-------|");
		var stringBuilder = sb;
		var stringBuilder2 = stringBuilder;
		var handler = new StringBuilder.AppendInterpolatedStringHandler(22, 2, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("| **Path** | `");
		handler.AppendFormatted(Path.GetFileName(file1Path));
		handler.AppendLiteral("` | `");
		handler.AppendFormatted(Path.GetFileName(file2Path));
		handler.AppendLiteral("` |");
		stringBuilder2.AppendLine(CultureInfo.InvariantCulture, ref handler);
		stringBuilder = sb;
		var stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(30, 2, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("| **Size** | ");
		handler.AppendFormatted(new FileInfo(file1Path).Length, "N0");
		handler.AppendLiteral(" bytes | ");
		handler.AppendFormatted(new FileInfo(file2Path).Length, "N0");
		handler.AppendLiteral(" bytes |");
		stringBuilder3.AppendLine(CultureInfo.InvariantCulture, ref handler);
		stringBuilder = sb;
		var stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(21, 2, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("| **MD5** | `");
		handler.AppendFormatted(hash1);
		handler.AppendLiteral("` | `");
		handler.AppendFormatted(hash2);
		handler.AppendLiteral("` |");
		stringBuilder4.AppendLine(CultureInfo.InvariantCulture, ref handler);
		sb.AppendLine();
		if (hash1 == hash2)
		{
			sb.AppendLine("Files are **identical** (same MD5 hash).");
			LogService.ProgressComplete();
			return new ResourceDiffSummary();
		}
		var resourceDiffs = await CompareWinFilesAsync(file1Path, file2Path, hash1, hash2, cached1, cached2, reportMode, cacheOptions).ConfigureAwait(false);
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
		var stringBuilder = sb;
		var stringBuilder2 = stringBuilder;
		var handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("| **Patch** | `");
		handler.AppendFormatted(Path.GetFileName(patchPath));
		handler.AppendLiteral("` |");
		stringBuilder2.AppendLine(CultureInfo.InvariantCulture, ref handler);
		stringBuilder = sb;
		var stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("| **Data** | `");
		handler.AppendFormatted(Path.GetFileName(dataPath));
		handler.AppendLiteral("` |");
		stringBuilder3.AppendLine(CultureInfo.InvariantCulture, ref handler);
		sb.AppendLine();
		var tempDir = Path.Combine(TempRoot.Path, $"g3mlib_diff_{Guid.NewGuid():N}");
		var patchExtractDir = Path.Combine(tempDir, "patch");
		var dataExportDir = Path.Combine(tempDir, "data");
		try
		{
			Directory.CreateDirectory(patchExtractDir);
			Directory.CreateDirectory(dataExportDir);
			LogService.Progress(10, 100);
			await ZipFile.ExtractToDirectoryAsync(patchPath, patchExtractDir).ConfigureAwait(false);
			LogService.Progress(20, 100);
			var manifestPath = Path.Combine(patchExtractDir, "g3mpatch.json");
			G3MPatchManifest? manifest = null;
			var manifestValid = true;
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
					manifest = JsonSerializer.Deserialize<G3MPatchManifest>(await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false));
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
					var stringBuilder4 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(57, 1, stringBuilder, CultureInfo.InvariantCulture);
					handler.AppendLiteral("Failed to parse manifest: ");
					handler.AppendFormatted(ex.Message);
					handler.AppendLiteral(". Using folder-based detection.");
					stringBuilder4.AppendLine(CultureInfo.InvariantCulture, ref handler);
					sb.AppendLine();
					manifestValid = false;
				}
			}
			string key;
			HashSet<string> resourceTypesInPatch;
			if (manifest?.Resources != null && manifestValid)
			{
				HashSet<string> hashSet = [];
				foreach (var key2 in manifest.Resources.Keys)
				{
					key = key2;
					hashSet.Add(key);
				}
				resourceTypesInPatch = hashSet;
			}
			else
			{
				resourceTypesInPatch = [.. from name in Directory.GetDirectories(patchExtractDir).Select(Path.GetFileName)
										where !string.IsNullOrEmpty(name) && ResourceTypes.Contains(name)
										select name];
				if (resourceTypesInPatch.Count == 0)
				{
					sb.AppendLine("## Error");
					sb.AppendLine();
					sb.AppendLine("No valid resource folders found in patch.");
					return 0;
				}
				stringBuilder = sb;
				var stringBuilder5 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(27, 1, stringBuilder, CultureInfo.InvariantCulture);
				handler.AppendLiteral("Detected resource folders: ");
				handler.AppendFormatted(string.Join(", ", resourceTypesInPatch));
				stringBuilder5.AppendLine(CultureInfo.InvariantCulture, ref handler);
				sb.AppendLine();
			}
			using (var stream = OpenDataReadStream(dataPath))
			{
				using var dataObj = await GameMakerIO.ReadAsync(stream).ConfigureAwait(false);
				ResourceExportService.ExportTypes(dataObj, dataExportDir, dataPath, resourceTypesInPatch);
			}
			GC.Collect();
			LogService.Progress(60, 100);
			var summary = new ResourceDiffSummary();
			List<TextFileDiff> textDiffs = [];
			if (manifest?.Resources != null && manifestValid)
			{
				foreach (var resource in manifest.Resources)
				{
					resource.Deconstruct(out key, out var value);
					var resourceType = key;
					var changes = value;
					var patchResDir = Path.Combine(patchExtractDir, resourceType);
					var dataResDir = Path.Combine(dataExportDir, resourceType);
					var typeChanges = new DiffResourceTypeChanges();
					IEnumerable<ResourceChange> changed = changes.Changed;
					foreach (var changed2 in changed ?? [])
					{
						if (string.IsNullOrEmpty(changed2.Name))
						{
							continue;
						}
						var patchResPath = Path.Combine(patchResDir, changed2.Name);
						var dataResPath = Path.Combine(dataResDir, changed2.Name);
						if (Directory.Exists(patchResPath) && Directory.Exists(dataResPath))
						{
							if (await AreDirectoriesDifferentAsync(patchResPath, dataResPath).ConfigureAwait(false))
							{
								typeChanges.Changed.Add(changed2.Name);
								await CollectTextDiffsAsync(patchResPath, dataResPath, changed2.Name, resourceType, textDiffs, reportMode).ConfigureAwait(false);
							}
						}
						else
						{
							typeChanges.Changed.Add(changed2.Name);
						}
					}
					changed = changes.New;
					foreach (var newRes in changed ?? [])
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
				foreach (var resourceType in resourceTypesInPatch)
				{
					var dataResDir = Path.Combine(patchExtractDir, resourceType);
					var patchResDir = Path.Combine(dataExportDir, resourceType);
					var typeChanges2 = await CompareResourceTypeAsync(dataResDir, patchResDir).ConfigureAwait(false);
					summary.ByType[resourceType] = typeChanges2;
					summary.TotalChanged += typeChanges2.Changed.Count;
					summary.TotalNew += typeChanges2.New.Count;
					summary.TotalDeleted += typeChanges2.Deleted.Count;
					foreach (var changedName in typeChanges2.Changed)
					{
						var patchResPath2 = Path.Combine(dataResDir, changedName);
						var dataResPath2 = Path.Combine(patchResDir, changedName);
						if (Directory.Exists(patchResPath2) && Directory.Exists(dataResPath2))
						{
							await CollectTextDiffsAsync(patchResPath2, dataResPath2, changedName, resourceType, textDiffs, reportMode).ConfigureAwait(false);
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
		var stringBuilder = sb;
		var stringBuilder2 = stringBuilder;
		var handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("| **Patch 1** | `");
		handler.AppendFormatted(Path.GetFileName(patch1Path));
		handler.AppendLiteral("` |");
		stringBuilder2.AppendLine(CultureInfo.InvariantCulture, ref handler);
		stringBuilder = sb;
		var stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("| **Patch 2** | `");
		handler.AppendFormatted(Path.GetFileName(patch2Path));
		handler.AppendLiteral("` |");
		stringBuilder3.AppendLine(CultureInfo.InvariantCulture, ref handler);
		sb.AppendLine();
		var tempDir = Path.Combine(TempRoot.Path, $"g3mlib_diff_{Guid.NewGuid():N}");
		var patch1Dir = Path.Combine(tempDir, "patch1");
		var patch2Dir = Path.Combine(tempDir, "patch2");
		try
		{
			Directory.CreateDirectory(patch1Dir);
			Directory.CreateDirectory(patch2Dir);
			LogService.Progress(10, 100);
			await ZipFile.ExtractToDirectoryAsync(patch1Path, patch1Dir).ConfigureAwait(false);
			LogService.Progress(20, 100);
			await ZipFile.ExtractToDirectoryAsync(patch2Path, patch2Dir).ConfigureAwait(false);
			LogService.Progress(30, 100);
			var summary = new ResourceDiffSummary();
			var typeIndex = 0;
			var totalTypes = ResourceTypes.Length;
			var resourceTypes = ResourceTypes;
			foreach (var resourceType in resourceTypes)
			{
				var dir1 = Path.Combine(patch1Dir, resourceType);
				var dir2 = Path.Combine(patch2Dir, resourceType);
				var changes = await CompareResourceTypeAsync(dir1, dir2).ConfigureAwait(false);
				summary.ByType[resourceType] = changes;
				summary.TotalChanged += changes.Changed.Count;
				summary.TotalNew += changes.New.Count;
				summary.TotalDeleted += changes.Deleted.Count;
				foreach (var changedName in changes.Changed)
				{
					var resDir1 = Path.Combine(dir1, changedName);
					var resDir2 = Path.Combine(dir2, changedName);
					if (Directory.Exists(resDir1) && Directory.Exists(resDir2))
					{
						await CollectTextDiffsAsync(resDir1, resDir2, changedName, resourceType, summary.TextDiffs, reportMode).ConfigureAwait(false);
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
		var stringBuilder = sb;
		var stringBuilder2 = stringBuilder;
		var handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("| Changed | ");
		handler.AppendFormatted(summary.TotalChanged);
		handler.AppendLiteral(" |");
		stringBuilder2.AppendLine(CultureInfo.InvariantCulture, ref handler);
		stringBuilder = sb;
		var stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("| New | ");
		handler.AppendFormatted(summary.TotalNew);
		handler.AppendLiteral(" |");
		stringBuilder3.AppendLine(CultureInfo.InvariantCulture, ref handler);
		stringBuilder = sb;
		var stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("| Deleted | ");
		handler.AppendFormatted(summary.TotalDeleted);
		handler.AppendLiteral(" |");
		stringBuilder4.AppendLine(CultureInfo.InvariantCulture, ref handler);
		stringBuilder = sb;
		var stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("| **Total** | **");
		handler.AppendFormatted(summary.TotalChanged + summary.TotalNew + summary.TotalDeleted);
		handler.AppendLiteral("** |");
		stringBuilder5.AppendLine(CultureInfo.InvariantCulture, ref handler);
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
			var stringBuilder = sb;
			var stringBuilder2 = stringBuilder;
			var handler = new StringBuilder.AppendInterpolatedStringHandler(3, 1, stringBuilder, CultureInfo.InvariantCulture);
			handler.AppendLiteral("## ");
			handler.AppendFormatted(resourceType);
			stringBuilder2.AppendLine(CultureInfo.InvariantCulture, ref handler);
			sb.AppendLine();
			if (changes.Changed.Count > 0)
			{
				stringBuilder = sb;
				var stringBuilder3 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder, CultureInfo.InvariantCulture);
				handler.AppendLiteral("### Changed (");
				handler.AppendFormatted(changes.Changed.Count);
				handler.AppendLiteral(")");
				stringBuilder3.AppendLine(CultureInfo.InvariantCulture, ref handler);
				sb.AppendLine();
				foreach (var name in changes.Changed)
				{
					stringBuilder = sb;
					var stringBuilder4 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder, CultureInfo.InvariantCulture);
					handler.AppendLiteral("- `");
					handler.AppendFormatted(name);
					handler.AppendLiteral("`");
					stringBuilder4.AppendLine(CultureInfo.InvariantCulture, ref handler);
				}
				sb.AppendLine();
			}
			if (changes.New.Count > 0)
			{
				stringBuilder = sb;
				var stringBuilder5 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder, CultureInfo.InvariantCulture);
				handler.AppendLiteral("### New (");
				handler.AppendFormatted(changes.New.Count);
				handler.AppendLiteral(")");
				stringBuilder5.AppendLine(CultureInfo.InvariantCulture, ref handler);
				sb.AppendLine();
				foreach (var name2 in changes.New)
				{
					stringBuilder = sb;
					var stringBuilder6 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder, CultureInfo.InvariantCulture);
					handler.AppendLiteral("- `");
					handler.AppendFormatted(name2);
					handler.AppendLiteral("`");
					stringBuilder6.AppendLine(CultureInfo.InvariantCulture, ref handler);
				}
				sb.AppendLine();
			}
			if (changes.Deleted.Count <= 0)
			{
				continue;
			}
			stringBuilder = sb;
			var stringBuilder7 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder, CultureInfo.InvariantCulture);
			handler.AppendLiteral("### Deleted (");
			handler.AppendFormatted(changes.Deleted.Count);
			handler.AppendLiteral(")");
			stringBuilder7.AppendLine(CultureInfo.InvariantCulture, ref handler);
			sb.AppendLine();
			foreach (var name3 in changes.Deleted)
			{
				stringBuilder = sb;
				var stringBuilder8 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder, CultureInfo.InvariantCulture);
				handler.AppendLiteral("- `");
				handler.AppendFormatted(name3);
				handler.AppendLiteral("`");
				stringBuilder8.AppendLine(CultureInfo.InvariantCulture, ref handler);
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
		foreach (var group in (from d in textDiffs
							   group d by d.ResourceType + "/" + d.ResourceName).OrderBy(g => g.Key, StringComparer.Ordinal).Take(120))
		{
			var handler = new StringBuilder.AppendInterpolatedStringHandler(9, 2, sb, CultureInfo.InvariantCulture);
			handler.AppendLiteral("| `");
			handler.AppendFormatted(group.Key);
			handler.AppendLiteral("` | ");
			handler.AppendFormatted(group.Count());
			handler.AppendLiteral(" |");
			sb.AppendLine(CultureInfo.InvariantCulture, ref handler);
		}
		if (textDiffs.Select(d => d.ResourceType + "/" + d.ResourceName).Distinct(StringComparer.Ordinal).Count() > 120)
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
		foreach (var diff in textDiffs)
		{
			var handler = new StringBuilder.AppendInterpolatedStringHandler(8, 3, sb, CultureInfo.InvariantCulture);
			handler.AppendLiteral("### `");
			handler.AppendFormatted(diff.ResourceType);
			handler.AppendLiteral("/");
			handler.AppendFormatted(diff.ResourceName);
			handler.AppendLiteral("/");
			handler.AppendFormatted(diff.FileName);
			handler.AppendLiteral("`");
			sb.AppendLine(CultureInfo.InvariantCulture, ref handler);
			sb.AppendLine();
			sb.AppendLine("```diff");
			sb.AppendLine(diff.UnifiedDiff);
			sb.AppendLine("```");
			sb.AppendLine();
		}
	}

	private static void AppendBinaryAnalysis(StringBuilder sb, string file1Path, string file2Path, DiffReportMode reportMode, ResourceDiffSummary resourceDiffs)
	{
		using var s1 = OpenDataReadStream(file1Path);
		using var data1 = GameMakerIO.Read(s1);
		using var s2 = OpenDataReadStream(file2Path);
		using var data2 = GameMakerIO.Read(s2);
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
		CompareNamedList("Variables", data1.Variables, data2.Variables, v => v?.Name?.Content);
		CompareNamedList("Functions", data1.Functions, data2.Functions, f => f?.Name?.Content);
		CompareNamedList("GameObjects", data1.GameObjects, data2.GameObjects, o => o?.Name?.Content);
		CompareNamedList("Sprites", data1.Sprites, data2.Sprites, gameMakerSprite => gameMakerSprite?.Name?.Content);
		CompareNamedList("Rooms", data1.Rooms, data2.Rooms, gameMakerRoom => gameMakerRoom?.Name?.Content);
		CompareNamedList("Scripts", data1.Scripts, data2.Scripts, gameMakerScript => gameMakerScript?.Name?.Content);
		CompareNamedList("Sounds", data1.Sounds, data2.Sounds, gameMakerSound => gameMakerSound?.Name?.Content);
		CompareNamedList("Code Entries", data1.Code, data2.Code, gameMakerCode => gameMakerCode?.Name?.Content);
		if (ResourceTypeChanged(resourceDiffs, "Sprites") || ResourceTypeChanged(resourceDiffs, "Backgrounds") || ResourceTypeChanged(resourceDiffs, "Fonts") || ResourceTypeChanged(resourceDiffs, "TexturePageItems") || ResourceTypeChanged(resourceDiffs, "EmbeddedTextures"))
		{
			AppendTexturePageItemReferenceAnalysis(sb, data1, data2, (reportMode == DiffReportMode.Full) ? 200 : 40);
		}
		List<(string, int, int)> roomDiffs = [];
		if (data1.Rooms != null && data2.Rooms != null)
		{
			var rooms2ByName = new Dictionary<string, GameMakerRoom>(StringComparer.Ordinal);
			foreach (var room in data2.Rooms)
			{
				var name = room?.Name?.Content;
				if (!string.IsNullOrEmpty(name) && room != null)
				{
					rooms2ByName.TryAdd(name, room);
				}
			}
			foreach (var r1 in data1.Rooms)
			{
				if (r1 != null && r1.Name?.Content != null && rooms2ByName.TryGetValue(r1.Name.Content, out var r2))
				{
					var ic1 = CountInstances(r1);
					var ic2 = CountInstances(r2);
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
			var name2 = item.Item1;
			var c1 = item.Item2;
			var c2 = item.Item3;
			var stringBuilder = sb;
			var stringBuilder2 = stringBuilder;
			var handler = new StringBuilder.AppendInterpolatedStringHandler(12, 3, stringBuilder, CultureInfo.InvariantCulture);
			handler.AppendLiteral("| `");
			handler.AppendFormatted(name2);
			handler.AppendLiteral("` | ");
			handler.AppendFormatted(c1);
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(c2);
			handler.AppendLiteral(" |");
			stringBuilder2.AppendLine(CultureInfo.InvariantCulture, ref handler);
		}
		if (roomDiffs.Count > 30)
		{
			var stringBuilder = sb;
			var stringBuilder3 = stringBuilder;
			var handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder, CultureInfo.InvariantCulture);
			handler.AppendLiteral("| *...and ");
			handler.AppendFormatted(roomDiffs.Count - 30);
			handler.AppendLiteral(" more* | | |");
			stringBuilder3.AppendLine(CultureInfo.InvariantCulture, ref handler);
		}
		sb.AppendLine();
		void CompareNamedList<T>(string sectionName, IList<T>? list1, IList<T>? list2, Func<T, string?> getName, int maxItems = 20) where T : notnull
		{
			maxItems = (reportMode == DiffReportMode.Full) ? Math.Max(maxItems, 200) : maxItems;
			List<string> names1 = [.. from text in (list1 ?? Array.Empty<T>()).Select(getName)
								   where text != null
								   select text];
			List<string> names2 = [.. from text in (list2 ?? Array.Empty<T>()).Select(getName)
								   where text != null
								   select text];
			var index1 = new Dictionary<string, int>(StringComparer.Ordinal);
			var index2 = new Dictionary<string, int>(StringComparer.Ordinal);
			for (var i = 0; i < names1.Count; i++)
			{
				index1.TryAdd(names1[i], i);
			}
			for (var i2 = 0; i2 < names2.Count; i2++)
			{
				index2.TryAdd(names2[i2], i2);
			}
			var set1 = index1.Keys.ToHashSet(StringComparer.Ordinal);
			var set2 = index2.Keys.ToHashSet(StringComparer.Ordinal);
			List<string> missing = [.. set1.Except(set2)];
			List<string> extra = [.. set2.Except(set1)];
			List<string> list3 = [.. set1.Intersect(set2)];
			List<(string, int, int)> indexMismatches = [];
			foreach (var name3 in list3)
			{
				var i3 = index1[name3];
				var i4 = index2[name3];
				if (i3 != i4)
				{
					indexMismatches.Add((name3, i3, i4));
				}
			}
			if (missing.Count != 0 || extra.Count != 0 || indexMismatches.Count != 0)
			{
				var stringBuilder4 = sb;
				var stringBuilder5 = stringBuilder4;
				var handler2 = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder4, CultureInfo.InvariantCulture);
				handler2.AppendLiteral("### ");
				handler2.AppendFormatted(sectionName);
				stringBuilder5.AppendLine(CultureInfo.InvariantCulture, ref handler2);
				sb.AppendLine();
				if (missing.Count > 0)
				{
					stringBuilder4 = sb;
					var stringBuilder6 = stringBuilder4;
					handler2 = new StringBuilder.AppendInterpolatedStringHandler(26, 1, stringBuilder4, CultureInfo.InvariantCulture);
					handler2.AppendLiteral("**Missing in File 2 (");
					handler2.AppendFormatted(missing.Count);
					handler2.AppendLiteral("):** ");
					stringBuilder6.AppendLine(CultureInfo.InvariantCulture, ref handler2);
					foreach (var n in missing.Take(maxItems))
					{
						stringBuilder4 = sb;
						var stringBuilder7 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder4, CultureInfo.InvariantCulture);
						handler2.AppendLiteral("- `");
						handler2.AppendFormatted(n);
						handler2.AppendLiteral("`");
						stringBuilder7.AppendLine(CultureInfo.InvariantCulture, ref handler2);
					}
					if (missing.Count > maxItems)
					{
						stringBuilder4 = sb;
						var stringBuilder8 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder4, CultureInfo.InvariantCulture);
						handler2.AppendLiteral("- *...and ");
						handler2.AppendFormatted(missing.Count - maxItems);
						handler2.AppendLiteral(" more*");
						stringBuilder8.AppendLine(CultureInfo.InvariantCulture, ref handler2);
					}
					sb.AppendLine();
				}
				if (extra.Count > 0)
				{
					stringBuilder4 = sb;
					var stringBuilder9 = stringBuilder4;
					handler2 = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder4, CultureInfo.InvariantCulture);
					handler2.AppendLiteral("**Extra in File 2 (");
					handler2.AppendFormatted(extra.Count);
					handler2.AppendLiteral("):** ");
					stringBuilder9.AppendLine(CultureInfo.InvariantCulture, ref handler2);
					foreach (var n2 in extra.Take(maxItems))
					{
						stringBuilder4 = sb;
						var stringBuilder10 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder4, CultureInfo.InvariantCulture);
						handler2.AppendLiteral("- `");
						handler2.AppendFormatted(n2);
						handler2.AppendLiteral("`");
						stringBuilder10.AppendLine(CultureInfo.InvariantCulture, ref handler2);
					}
					if (extra.Count > maxItems)
					{
						stringBuilder4 = sb;
						var stringBuilder11 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder4, CultureInfo.InvariantCulture);
						handler2.AppendLiteral("- *...and ");
						handler2.AppendFormatted(extra.Count - maxItems);
						handler2.AppendLiteral(" more*");
						stringBuilder11.AppendLine(CultureInfo.InvariantCulture, ref handler2);
					}
					sb.AppendLine();
				}
				if (indexMismatches.Count > 0)
				{
					stringBuilder4 = sb;
					var stringBuilder12 = stringBuilder4;
					handler2 = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder4, CultureInfo.InvariantCulture);
					handler2.AppendLiteral("**Index mismatches (");
					handler2.AppendFormatted(indexMismatches.Count);
					handler2.AppendLiteral("):**");
					stringBuilder12.AppendLine(CultureInfo.InvariantCulture, ref handler2);
					sb.AppendLine();
					sb.AppendLine("| Name | File 1 idx | File 2 idx |");
					sb.AppendLine("|:-----|----------:|-----------:|");
					foreach (var item2 in indexMismatches.Take(maxItems))
					{
						var name4 = item2.Item1;
						var i5 = item2.Item2;
						var i6 = item2.Item3;
						stringBuilder4 = sb;
						var stringBuilder13 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(12, 3, stringBuilder4, CultureInfo.InvariantCulture);
						handler2.AppendLiteral("| `");
						handler2.AppendFormatted(name4);
						handler2.AppendLiteral("` | ");
						handler2.AppendFormatted(i5);
						handler2.AppendLiteral(" | ");
						handler2.AppendFormatted(i6);
						handler2.AppendLiteral(" |");
						stringBuilder13.AppendLine(CultureInfo.InvariantCulture, ref handler2);
					}
					if (indexMismatches.Count > maxItems)
					{
						stringBuilder4 = sb;
						var stringBuilder14 = stringBuilder4;
						handler2 = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder4, CultureInfo.InvariantCulture);
						handler2.AppendLiteral("| *...and ");
						handler2.AppendFormatted(indexMismatches.Count - maxItems);
						handler2.AppendLiteral(" more* | | |");
						stringBuilder14.AppendLine(CultureInfo.InvariantCulture, ref handler2);
					}
					sb.AppendLine();
				}
			}
		}
		static int CountInstances(GameMakerRoom gameMakerRoom)
		{
			var count = 0;
			IEnumerable<GameMakerRoom.Layer> layers = gameMakerRoom.Layers;
			foreach (var layer in layers ?? [])
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
			var delta = (num == num2) ? "=" : $"{num2 - num:+#;-#;0}";
			var stringBuilder4 = sb;
			var handler2 = new StringBuilder.AppendInterpolatedStringHandler(13, 4, stringBuilder4, CultureInfo.InvariantCulture);
			handler2.AppendLiteral("| ");
			handler2.AppendFormatted(value);
			handler2.AppendLiteral(" | ");
			handler2.AppendFormatted(num);
			handler2.AppendLiteral(" | ");
			handler2.AppendFormatted(num2);
			handler2.AppendLiteral(" | ");
			handler2.AppendFormatted(delta);
			handler2.AppendLiteral(" |");
			stringBuilder4.AppendLine(CultureInfo.InvariantCulture, ref handler2);
		}
	}

	private static bool ResourceTypeChanged(ResourceDiffSummary summary, string resourceType)
	{
		if (summary.ByType.TryGetValue(resourceType, out var changes))
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
		return summary.ByType.Keys.Any(resourceType => !resourceType.Equals("GeneralInfo", StringComparison.OrdinalIgnoreCase) && ResourceTypeChanged(summary, resourceType));
	}

	private static void AppendTexturePageItemReferenceAnalysis(StringBuilder sb, GameMakerData data1, GameMakerData data2, int maxRows)
	{
		var refs1 = BuildTexturePageItemReferenceMap(data1);
		var refs2 = BuildTexturePageItemReferenceMap(data2);
		List<string> rows = [];
		foreach (var key in from k in refs1.Keys.Union(refs2.Keys)
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
		foreach (var row in rows)
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
		Dictionary<int, List<string>> refs = [];
		var indices = new Dictionary<GameMakerTexturePageItem, int>(ReferenceEqualityComparer.Instance);
		for (var i = 0; i < data.TexturePageItems.Count; i++)
		{
			var item = data.TexturePageItems[i];
			if (item != null)
			{
				indices.TryAdd(item, i);
			}
		}
		foreach (var sprite in data.Sprites ?? [])
		{
			var name = sprite?.Name?.Content;
			if (sprite?.Textures != null && !string.IsNullOrWhiteSpace(name))
			{
				for (var j = 0; j < sprite.Textures.Count; j++)
				{
					Add(sprite.Textures[j]?.Texture, $"sprite:{name}[{j}]");
				}
			}
		}
		foreach (var bg in data.Backgrounds ?? [])
		{
			Add(bg?.Texture, "background:" + (bg?.Name?.Content ?? "?"));
		}
		foreach (var font in data.Fonts ?? [])
		{
			Add(font?.Texture, "font:" + (font?.Name?.Content ?? "?"));
		}
		return refs.ToDictionary(kvp => kvp.Key, kvp => string.Join(", ", kvp.Value.OrderBy(v => v, StringComparer.Ordinal).Take(8)));
		void Add(GameMakerTexturePageItem? tpi, string owner)
		{
			if (tpi != null && indices.TryGetValue(tpi, out var index))
			{
				if (!refs.TryGetValue(index, out var owners))
				{
					owners = [];
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
		var sprites1 = data1.Sprites.Where(s => s?.Name?.Content != null).ToDictionary(s => s.Name.Content, StringComparer.Ordinal);
		var sprites2 = data2.Sprites.Where(s => s?.Name?.Content != null).ToDictionary(s => s.Name.Content, StringComparer.Ordinal);
		List<string> rows = [];
		using var worker = new TextureWorker();
		foreach (var name in sprites1.Keys.Intersect(sprites2.Keys, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
		{
			var left = sprites1[name];
			var right = sprites2[name];
			var minFrames = Math.Min(left.Textures.Count, right.Textures.Count);
			var changedFrames = 0;
			for (var i = 0; i < minFrames; i++)
			{
				if (SpriteFrameHash(worker, left, i) != SpriteFrameHash(worker, right, i))
				{
					changedFrames++;
				}
			}
			var addedFrames = Math.Max(0, right.Textures.Count - left.Textures.Count);
			var removedFrames = Math.Max(0, left.Textures.Count - right.Textures.Count);
			var metadataChanged = left.Width != right.Width || left.Height != right.Height || left.OriginX != right.OriginX || left.OriginY != right.OriginY || left.MarginLeft != right.MarginLeft || left.MarginTop != right.MarginTop || left.MarginRight != right.MarginRight || left.MarginBottom != right.MarginBottom || left.BBoxMode != right.BBoxMode || left.Transparent != right.Transparent || left.Smooth != right.Smooth || left.Preload != right.Preload;
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
		foreach (var row in rows)
		{
			sb.AppendLine(row);
		}
		sb.AppendLine();
	}

	private static string SpriteFrameHash(TextureWorker worker, GameMakerSprite sprite, int frameIndex)
	{
		var tpi = sprite.Textures.ElementAtOrDefault(frameIndex)?.Texture;
		if (tpi == null)
		{
			return "<null>";
		}
		try
		{
			using var image = worker.GetTextureFor(tpi, $"{sprite.Name?.Content ?? "sprite"}_{frameIndex}");
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
		var files1 = Directory.GetFiles(dir1, "*", SearchOption.AllDirectories);
		var array = files1;
		foreach (var file1 in array)
		{
			var ext = Path.GetExtension(file1).ToLowerInvariant();
			if (!TextExtensions.Contains(ext))
			{
				continue;
			}
			var relPath = Path.GetRelativePath(dir1, file1);
			var file2 = Path.Combine(dir2, relPath);
			if (File.Exists(file2))
			{
				var content1 = await File.ReadAllTextAsync(file1).ConfigureAwait(false);
				var content2 = await File.ReadAllTextAsync(file2).ConfigureAwait(false);
				if (content1 != content2)
				{
					textDiffs.Add(new TextFileDiff
					{
						ResourceType = resourceType,
						ResourceName = resourceName,
						FileName = relPath,
						UnifiedDiff = (reportMode == DiffReportMode.Full) ? GenerateUnifiedDiff(content1, content2, relPath) : ""
					});
				}
			}
		}
	}

	private static string GenerateUnifiedDiff(string content1, string content2, string fileName)
	{
		var lines1 = content1.Split('\n');
		var lines2 = content2.Split('\n');
		var sb = new StringBuilder();
		var stringBuilder = sb;
		var stringBuilder2 = stringBuilder;
		var handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("--- a/");
		handler.AppendFormatted(fileName);
		stringBuilder2.AppendLine(CultureInfo.InvariantCulture, ref handler);
		stringBuilder = sb;
		var stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder, CultureInfo.InvariantCulture);
		handler.AppendLiteral("+++ b/");
		handler.AppendFormatted(fileName);
		stringBuilder3.AppendLine(CultureInfo.InvariantCulture, ref handler);
		var i = 0;
		var j = 0;
		var contextLines = 3;
		List<(int, int, List<string>)> hunks = [];
		List<string> currentHunk = [];
		var hunkStart1 = 0;
		var hunkStart2 = 0;
		var inHunk = false;
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
					for (var k = hunkStart1; k < i; k++)
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
			var matchCount = 0;
			var ti = i;
			var tj = j;
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
			for (var l = 0; l < Math.Min(contextLines, matchCount); l++)
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
			var start1 = item2.Item1;
			var start2 = item2.Item2;
			var item = item2.Item3;
			var removed = item.Count(text => text.StartsWith('-'));
			var added = item.Count(text => text.StartsWith('+'));
			var context = item.Count(text => text.StartsWith(' '));
			stringBuilder = sb;
			var stringBuilder4 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(11, 4, stringBuilder, CultureInfo.InvariantCulture);
			handler.AppendLiteral("@@ -");
			handler.AppendFormatted(start1 + 1);
			handler.AppendLiteral(",");
			handler.AppendFormatted(removed + context);
			handler.AppendLiteral(" +");
			handler.AppendFormatted(start2 + 1);
			handler.AppendLiteral(",");
			handler.AppendFormatted(added + context);
			handler.AppendLiteral(" @@");
			stringBuilder4.AppendLine(CultureInfo.InvariantCulture, ref handler);
			foreach (var line in item)
			{
				sb.AppendLine(line);
			}
		}
		return sb.ToString().TrimEnd();
	}

	private static async Task<ResourceDiffSummary> CompareWinFilesAsync(string file1Path, string file2Path, string file1Md5, string file2Md5, G3MDataAnalysisCache? cached1, G3MDataAnalysisCache? cached2, DiffReportMode reportMode, G3MCacheOptions? cacheOptions)
	{
		var summary = new ResourceDiffSummary();
		var tempDir = Path.Combine(TempRoot.Path, $"g3mlib_diff_{Guid.NewGuid():N}");
		var export1Dir = Path.Combine(tempDir, "file1");
		var export2Dir = Path.Combine(tempDir, "file2");
		try
		{
			var diffSw = Stopwatch.StartNew();
			var diffPhaseSw = new Stopwatch();
			LogService.Log("[DiffService] Hashing resources from both files...");
			LogService.Progress(10, 100);
			diffPhaseSw.Restart();
			Dictionary<string, Dictionary<string, string>> hashes1;
			bool shouldAppend;
			if (cached1 != null)
			{
				hashes1 = cached1.ResourceHashes;
				var handler = new LogInterpolatedStringHandler(32, 2, out shouldAppend);
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
				using var stream = OpenDataReadStream(file1Path);
				using var data1 = await GameMakerIO.ReadAsync(stream).ConfigureAwait(false);
				var loadTime = diffPhaseSw.Elapsed;
				diffPhaseSw.Restart();
				hashes1 = ResourceHashService.HashAll(data1);
				var info = new DataFileInfo
				{
					Filename = Path.GetFileName(file1Path),
					Size = new FileInfo(file1Path).Length,
					Md5 = file1Md5,
					BytecodeVersion = data1.GeneralInfo?.BytecodeVersion ?? 0,
					GmsVersion = GeneralInfoUtil.GetVersionDisplay(data1.GeneralInfo),
					GeneralInfo = GeneralInfoUtil.ExtractGeneralInfo(data1)
				};
				await G3MCacheService.WriteDataCacheAsync(file1Path, info, hashes1, PatchService.GetResourceNameCountsForReuse(data1), PatchService.GetOrderSensitiveResourceNamesForReuse(data1), cacheOptions).ConfigureAwait(false);
				var handler2 = new LogInterpolatedStringHandler(40, 3, out shouldAppend);
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
				var handler3 = new LogInterpolatedStringHandler(32, 2, out shouldAppend);
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
				using var stream = OpenDataReadStream(file2Path);
				using var data1 = await GameMakerIO.ReadAsync(stream).ConfigureAwait(false);
				var loadTime = diffPhaseSw.Elapsed;
				diffPhaseSw.Restart();
				hashes2 = ResourceHashService.HashAll(data1);
				var info2 = new DataFileInfo
				{
					Filename = Path.GetFileName(file2Path),
					Size = new FileInfo(file2Path).Length,
					Md5 = file2Md5,
					BytecodeVersion = data1.GeneralInfo?.BytecodeVersion ?? 0,
					GmsVersion = GeneralInfoUtil.GetVersionDisplay(data1.GeneralInfo),
					GeneralInfo = GeneralInfoUtil.ExtractGeneralInfo(data1)
				};
				await G3MCacheService.WriteDataCacheAsync(file2Path, info2, hashes2, PatchService.GetResourceNameCountsForReuse(data1), PatchService.GetOrderSensitiveResourceNamesForReuse(data1), cacheOptions).ConfigureAwait(false);
				var handler4 = new LogInterpolatedStringHandler(40, 3, out shouldAppend);
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
			List<string> changedTypes = [];
			var resourceTypes = ResourceTypes;
			foreach (var resourceType in resourceTypes)
			{
				var h1 = hashes1.GetValueOrDefault(resourceType) ?? [];
				var h2 = hashes2.GetValueOrDefault(resourceType) ?? [];
				var changes = new DiffResourceTypeChanges();
				HashSet<string> names1 = [.. h1.Keys];
				HashSet<string> names2 = [.. h2.Keys];
				foreach (var name in names2.Except(names1))
				{
					changes.New.Add(name);
				}
				foreach (var name2 in names1.Except(names2))
				{
					changes.Deleted.Add(name2);
				}
				foreach (var name3 in names1.Intersect(names2))
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
			var handler5 = new LogInterpolatedStringHandler(45, 3, out shouldAppend);
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
			var textDiffTypes = (reportMode == DiffReportMode.Full) ? changedTypes.ToList() : [.. changedTypes.Where(type => !BinaryHeavyResourceTypes.Contains(type))];
			if (textDiffTypes.Count > 0)
			{
				Directory.CreateDirectory(export1Dir);
				Directory.CreateDirectory(export2Dir);
				diffPhaseSw.Restart();
				var handler6 = new LogInterpolatedStringHandler(59, 2, out shouldAppend);
				if (shouldAppend)
				{
					handler6.AppendLiteral("[DiffService] Exporting ");
					handler6.AppendFormatted(textDiffTypes.Count);
					handler6.AppendLiteral("/");
					handler6.AppendFormatted(changedTypes.Count);
					handler6.AppendLiteral(" changed type(s) for text diffs...");
				}
				LogService.Log(ref handler6);
				using (var stream2 = OpenDataReadStream(file1Path))
				{
					using var data2 = await GameMakerIO.ReadAsync(stream2).ConfigureAwait(false);
					ResourceExportService.ExportTypes(data2, export1Dir, file1Path, textDiffTypes);
				}
				GC.Collect();
				using (var stream3 = OpenDataReadStream(file2Path))
				{
					using var data3 = await GameMakerIO.ReadAsync(stream3).ConfigureAwait(false);
					ResourceExportService.ExportTypes(data3, export2Dir, file2Path, textDiffTypes);
				}
				GC.Collect();
				var handler7 = new LogInterpolatedStringHandler(42, 2, out shouldAppend);
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
				var typeIndex = 0;
				var totalTypes = textDiffTypes.Count;
				foreach (var resourceType2 in textDiffTypes)
				{
					var dir1 = Path.Combine(export1Dir, resourceType2);
					var dir2 = Path.Combine(export2Dir, resourceType2);
					var changes2 = summary.ByType[resourceType2];
					foreach (var changedName in changes2.Changed)
					{
						var resDir1 = Path.Combine(dir1, changedName);
						var resDir2 = Path.Combine(dir2, changedName);
						if (Directory.Exists(resDir1) && Directory.Exists(resDir2))
						{
							await CollectTextDiffsAsync(resDir1, resDir2, changedName, resourceType2, summary.TextDiffs, reportMode).ConfigureAwait(false);
						}
					}
					typeIndex++;
					LogService.Progress(80 + typeIndex * 15 / totalTypes, 100);
				}
				var handler8 = new LogInterpolatedStringHandler(32, 1, out shouldAppend);
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
				var handler9 = new LogInterpolatedStringHandler(73, 1, out shouldAppend);
				if (shouldAppend)
				{
					handler9.AppendLiteral("[DiffService] Text diff export skipped; changed types are binary-heavy (");
					handler9.AppendFormatted(string.Join(", ", changedTypes));
					handler9.AppendLiteral(")");
				}
				LogService.Log(ref handler9);
			}
			diffSw.Stop();
			var handler10 = new LogInterpolatedStringHandler(38, 2, out shouldAppend);
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
		var changes = new DiffResourceTypeChanges();
		var resources1 = Directory.Exists(dir1) ? [.. from n in Directory.GetDirectories(dir1).Select(Path.GetFileName)
																where n != null
																select n] : new HashSet<string>();
		var resources2 = Directory.Exists(dir2) ? [.. from n in Directory.GetDirectories(dir2).Select(Path.GetFileName)
																where n != null
																select n] : new HashSet<string>();
		foreach (var name in resources2.Except(resources1))
		{
			if (!string.IsNullOrEmpty(name))
			{
				changes.New.Add(name);
			}
		}
		foreach (var name2 in resources1.Except(resources2))
		{
			if (!string.IsNullOrEmpty(name2))
			{
				changes.Deleted.Add(name2);
			}
		}
		foreach (var name3 in resources1.Intersect(resources2))
		{
			if (!string.IsNullOrEmpty(name3))
			{
				var dir3 = Path.Combine(dir1, name3);
				var path2 = Path.Combine(dir2, name3);
				if (await AreDirectoriesDifferentAsync(dir3, path2).ConfigureAwait(false))
				{
					changes.Changed.Add(name3);
				}
			}
		}
		return changes;
	}

	private static async Task<bool> AreDirectoriesDifferentAsync(string dir1, string dir2)
	{
		HashSet<string> files1 = [.. from path in Directory.GetFiles(dir1, "*", SearchOption.AllDirectories)
								  select Path.GetRelativePath(dir1, path)];
		HashSet<string> files2 = [.. from path in Directory.GetFiles(dir2, "*", SearchOption.AllDirectories)
								  select Path.GetRelativePath(dir2, path)];
		if (!files1.SetEquals(files2))
		{
			return true;
		}
		foreach (var relPath in files1)
		{
			var f1 = Path.Combine(dir1, relPath);
			var f2 = Path.Combine(dir2, relPath);
			if (!HashService.FileSizesMatch(f1, f2))
			{
				return true;
			}
			if (await HashService.ComputeFileHashAsync(f1).ConfigureAwait(false) != await HashService.ComputeFileHashAsync(f2).ConfigureAwait(false))
			{
				return true;
			}
		}
		return false;
	}
}
