using System.IO;
using G3MLib.DataFile.Models;
using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;

namespace G3MLib.Modding.Resources;

public static partial class ResourceImportService
{
	// Compiles the .gml file of each entry folder the export wrote. The .asm files are read-only companions: the library has no assembler.
	// Child entries are rebuilt by the compiler from the .gml, so they have no file of their own to import.
	private static void ImportCodeEntries(GameMakerData data, string inputDir)
	{
		var failures = ImportCodeEntries(data, inputDir, null, out _);
		// Outside a batch run nothing collects the results, so a failure has to stop the call.
		if (failures.Count > 0 && !BatchRun.Active)
		{
			throw new InvalidOperationException("Code entries failed to import:\n" + string.Join('\n', failures));
		}
	}

	/// <summary>The project import's way in: takes the gate and returns the failures and the number of entries applied, whether or not a batch run is active.</summary>
	internal static List<string> ImportCodeEntriesForProject(GameMakerData data, string inputDir, List<string> warnings, out int imported)
	{
		ArgumentNullException.ThrowIfNull(data);
		using var gate = data.Gate.BeginWrite(cancellationToken: ModdingCancellation.Token);
		return ImportCodeEntries(data, inputDir, warnings, out imported);
	}

	private static List<string> ImportCodeEntries(GameMakerData data, string inputDir, List<string>? warnings, out int imported)
	{
		var byKey = CodeEntryArchiveIdentity.DescribeEntriesByKey(data);
		CodeImportGroup group = new(data) { AutoCreateAssets = false };
		List<(string Dir, string Key, string Name, CodeArchiveEntryDescriptor? Descriptor)> matched = [];
		foreach (var dir in GetDirs(inputDir))
		{
			var key = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
			var name = byKey.TryGetValue(key, out var descriptor) ? descriptor.LogicalName : key;
			if (_only is null || MatchesOnlyName(key) || MatchesOnlyName(name))
			{
				matched.Add((dir, key, name, descriptor));
			}
		}
		// Folders the name filter leaves out are not part of this run
		BatchRun.Expect(matched.Count);
		List<string> failures = [];
		foreach (var (dir, key, name, descriptor) in matched)
		{
			var gml = Path.Combine(dir, key + ".gml");
			if (!FExists(gml))
			{
				BatchRun.Item(name, ResultKind.Skipped, FExists(Path.Combine(dir, key + ".asm")) ? "Only an .asm file was found and the library cannot assemble it" : "Missing gml file");
				continue;
			}
			if (descriptor is { Occurrence: > 1 })
			{
				warnings?.Add($"Code entry \"{name}\" occurs more than once and was not imported.");
				BatchRun.Item(name, ResultKind.Skipped, "The entry occurs more than once in the data file");
				continue;
			}
			try
			{
				group.QueueReplace(name, FReadText(gml));
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				failures.Add($"{name}: {ex.Message}");
				BatchRun.Failed(name, ex);
			}
		}
		var result = group.Import(CodeImportMode.Apply, cancellationToken: ModdingCancellation.Token);
		imported = 0;
		foreach (var operation in result.Operations)
		{
			if (operation.Status == CodeOperationStatus.Applied)
			{
				imported++;
				BatchRun.Item(operation.CodeEntry);
				continue;
			}
			failures.Add($"{operation.CodeEntry}: {operation.Message}");
			BatchRun.Item(operation.CodeEntry, ResultKind.Failed, operation.Message);
		}
		return failures;
	}
}
