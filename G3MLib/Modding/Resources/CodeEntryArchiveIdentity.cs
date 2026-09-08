using System;
using System.Collections.Generic;
using System.Linq;
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
		List<CodeArchiveEntryDescriptor> result = new List<CodeArchiveEntryDescriptor>();
		Dictionary<string, int> topLevelByNameCount = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (GameMakerCode entry in data.Code)
		{
			if (entry != null && entry.Name?.Content != null && entry.ParentEntry == null && (topLevelFilterNames == null || topLevelFilterNames.Contains(entry.Name.Content)))
			{
				int occurrence = topLevelByNameCount.GetValueOrDefault(entry.Name.Content) + 1;
				topLevelByNameCount[entry.Name.Content] = occurrence;
				string safeName = ResourceExportService.SafeName(entry.Name.Content);
				string archiveKey = BuildOccurrenceKey(safeName, occurrence);
				result.Add(new CodeArchiveEntryDescriptor(archiveKey, entry.Name.Content, safeName, null, occurrence));
				DescribeChildren(entry, archiveKey, result);
			}
		}
		return result;
	}

	public static Dictionary<string, CodeArchiveEntryDescriptor> DescribeEntriesByKey(GameMakerData data, HashSet<string>? topLevelFilterNames = null)
	{
		return DescribeEntries(data, topLevelFilterNames).ToDictionary<CodeArchiveEntryDescriptor, string>((CodeArchiveEntryDescriptor x) => x.ArchiveKey, StringComparer.Ordinal);
	}

	private static void DescribeChildren(GameMakerCode parent, string parentArchiveKey, List<CodeArchiveEntryDescriptor> output)
	{
		if (parent.ChildEntries.Count == 0)
		{
			return;
		}
		Dictionary<string, int> childCountByName = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (GameMakerCode child in parent.ChildEntries)
		{
			if (child != null && child.Name?.Content != null)
			{
				int occurrence = childCountByName.GetValueOrDefault(child.Name.Content) + 1;
				childCountByName[child.Name.Content] = occurrence;
				string safeName = ResourceExportService.SafeName(child.Name.Content);
				string childLeafKey = BuildOccurrenceKey(safeName, occurrence);
				output.Add(new CodeArchiveEntryDescriptor(parentArchiveKey + "/" + childLeafKey, child.Name.Content, safeName, parentArchiveKey, occurrence));
			}
		}
	}
}

internal sealed record CodeArchiveEntryDescriptor(string ArchiveKey, string LogicalName, string SafeName, string? ParentArchiveKey, int Occurrence);
