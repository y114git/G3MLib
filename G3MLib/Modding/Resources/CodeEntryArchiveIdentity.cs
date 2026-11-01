using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Modding.Resources;

internal static class CodeEntryArchiveIdentity
{
	public static string BuildOccurrenceKey(string safeName, int occurrence)
	{
		if (occurrence > 1)
		{
			return $"{safeName}__occ{occurrence:D4}";
		}
		return safeName;
	}

	public static List<CodeArchiveEntryDescriptor> DescribeEntries(GameMakerData data, HashSet<string>? topLevelFilterNames = null)
	{
		List<CodeArchiveEntryDescriptor> result = [];
		var topLevelByNameCount = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var entry in data.Code)
		{
			if (entry != null && entry.Name?.Content != null && entry.ParentEntry == null && (topLevelFilterNames == null || topLevelFilterNames.Contains(entry.Name.Content)))
			{
				var occurrence = topLevelByNameCount.GetValueOrDefault(entry.Name.Content) + 1;
				topLevelByNameCount[entry.Name.Content] = occurrence;
				var safeName = ResourceExportService.SafeName(entry.Name.Content);
				var archiveKey = BuildOccurrenceKey(safeName, occurrence);
				result.Add(new CodeArchiveEntryDescriptor(archiveKey, entry.Name.Content, safeName, null, occurrence));
				DescribeChildren(entry, archiveKey, result);
			}
		}
		return result;
	}

	public static Dictionary<string, CodeArchiveEntryDescriptor> DescribeEntriesByKey(GameMakerData data, HashSet<string>? topLevelFilterNames = null)
	{
		return DescribeEntries(data, topLevelFilterNames).ToDictionary(x => x.ArchiveKey, StringComparer.Ordinal);
	}

	private static void DescribeChildren(GameMakerCode parent, string parentArchiveKey, List<CodeArchiveEntryDescriptor> output)
	{
		if (parent.ChildEntries.Count == 0)
		{
			return;
		}
		var childCountByName = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var child in parent.ChildEntries)
		{
			if (child != null && child.Name?.Content != null)
			{
				var occurrence = childCountByName.GetValueOrDefault(child.Name.Content) + 1;
				childCountByName[child.Name.Content] = occurrence;
				var safeName = ResourceExportService.SafeName(child.Name.Content);
				var childLeafKey = BuildOccurrenceKey(safeName, occurrence);
				output.Add(new CodeArchiveEntryDescriptor(parentArchiveKey + "/" + childLeafKey, child.Name.Content, safeName, parentArchiveKey, occurrence));
			}
		}
	}
}

internal sealed record CodeArchiveEntryDescriptor(string ArchiveKey, string LogicalName, string SafeName, string? ParentArchiveKey, int Occurrence);
