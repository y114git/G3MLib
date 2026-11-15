using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Logging;

namespace G3MLib.Modding.Merging;

public static partial class MergeService
{
	/// <summary>
	/// Resource files that hold plain lists of names. Index-by-index merging loses entries when two patches
	/// each append to the same list, so these are merged by name against the original list.
	/// </summary>
	internal static byte[]? TryNameListMerge(string filePath, GameMakerData data, byte[] existingBytes, byte[] incomingBytes)
	{
		try
		{
			if (filePath.Equals("GlobalScripts/global_scripts.json", StringComparison.OrdinalIgnoreCase))
			{
				return MergeNameListFields(existingBytes, incomingBytes, new()
				{
					["globalInitScripts"] = Names(data.GlobalInitScripts, s => s?.Code?.Name?.Content),
					["gameEndScripts"] = Names(data.GameEndScripts, s => s?.Code?.Name?.Content)
				});
			}

			if (filePath.Equals("Tags/tags.json", StringComparison.OrdinalIgnoreCase))
				return MergeNameListFields(existingBytes, incomingBytes, new() { ["tags"] = Names(data.Tags?.Tags, t => t?.Content) });

			if (filePath.Equals("FeatureFlags/feature_flags.json", StringComparison.OrdinalIgnoreCase)
				&& MergedNames(Names(data.FeatureFlags?.List, f => f?.Content), ParseJson(existingBytes), ParseJson(incomingBytes)) is { } flags)
				return Serialize(flags);
		}
		catch (JsonException ex)
		{
			LogService.Warning($"[MergeService] {filePath} is not valid JSON, so its list is not merged by name: {ex.Message}");
		}
		return null;
	}

	private static byte[]? MergeNameListFields(byte[] existingBytes, byte[] incomingBytes, Dictionary<string, string[]> baseLists)
	{
		if (ParseJsonObject(existingBytes) is not { } existing || ParseJsonObject(incomingBytes) is not { } result)
			return null;

		var changed = false;
		foreach (var (field, baseList) in baseLists)
		{
			if (MergedNames(baseList, existing[field], result[field]) is not { } merged)
				continue;

			result[field] = merged;
			changed = true;
		}

		// Everything outside the name lists keeps the incoming value, as an overwrite would.
		return changed ? Serialize(result) : null;
	}

	/// <summary>
	/// Three-way merge of a list of unique names. Names either side added are kept, names either side removed from
	/// the original are dropped. When only one side changed the list, that side's list is the result. Otherwise the
	/// existing side keeps its order and the incoming side's additions go after the
	/// nearest preceding name both lists share, behind any additions the existing side made at the same spot.
	/// Returns null when a list has repeated names, where a name cannot identify an entry.
	/// </summary>
	internal static List<string>? MergeNameLists(IReadOnlyList<string> original, IReadOnlyList<string> ours, IReadOnlyList<string> theirs)
	{
		var inOriginal = new HashSet<string>(original, StringComparer.Ordinal);
		var inOurs = new HashSet<string>(ours, StringComparer.Ordinal);
		var inTheirs = new HashSet<string>(theirs, StringComparer.Ordinal);
		if (inOriginal.Count != original.Count || inOurs.Count != ours.Count || inTheirs.Count != theirs.Count)
			return null;

		// Only one side changed the list: that side's order stands, including a reorder.
		if (ours.SequenceEqual(original, StringComparer.Ordinal))
			return [.. theirs];
		if (theirs.SequenceEqual(original, StringComparer.Ordinal))
			return [.. ours];

		var list = new LinkedList<string>();
		var nodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.Ordinal);
		foreach (var name in ours)
		{
			if (!inOriginal.Contains(name) || inTheirs.Contains(name))
				nodes[name] = list.AddLast(name);
		}

		LinkedListNode<string>? anchor = null;
		var atSharedName = true;
		foreach (var name in theirs)
		{
			if (nodes.TryGetValue(name, out var shared))
			{
				anchor = shared;
				atSharedName = true;
			}
			else if (!inOriginal.Contains(name))
			{
				if (atSharedName)
				{
					// Additions the existing side made at this spot stay first.
					var next = anchor is null ? list.First : anchor.Next;
					while (next is not null && !inOriginal.Contains(next.Value) && !inTheirs.Contains(next.Value))
					{
						anchor = next;
						next = next.Next;
					}
				}

				anchor = anchor is null ? list.AddFirst(name) : list.AddAfter(anchor, name);
				atSharedName = false;
			}
		}

		return [.. list];
	}

	/// <summary>Replaces one list in an already merged object with the by-name merge of the three versions.</summary>
	private static void MergeNameListField(JsonObject original, JsonObject ours, JsonObject theirs, JsonObject merged, string field)
	{
		if (original[field] is JsonArray baseArray && TryNames(baseArray, out var baseNames) && MergedNames(baseNames, ours[field], theirs[field]) is { } names)
			merged[field] = names;
	}

	/// <summary>The by-name merge of two JSON string arrays against the original names, or null when either is not a plain list of strings.</summary>
	private static JsonArray? MergedNames(IReadOnlyList<string> original, JsonNode? ours, JsonNode? theirs)
	{
		if (ours is not JsonArray oursArray || theirs is not JsonArray theirsArray
			|| !TryNames(oursArray, out var oursNames) || !TryNames(theirsArray, out var theirsNames)
			|| MergeNameLists(original, oursNames, theirsNames) is not { } names)
			return null;

		return new JsonArray([.. names.Select(n => (JsonNode?)JsonValue.Create(n))]);
	}

	private static JsonNode? ParseJson(byte[] bytes) => JsonNode.Parse(bytes.AsSpan().StartsWith("\uFEFF"u8) ? bytes.AsSpan(3) : bytes.AsSpan());

	private static string[] Names<T>(IEnumerable<T>? items, Func<T, string?> name) => items?.Select(i => name(i) ?? "").ToArray() ?? [];

	private static bool TryNames(JsonArray array, out string[] names)
	{
		var list = new List<string>(array.Count);
		foreach (var item in array)
		{
			if (item is not JsonValue value || !value.TryGetValue<string>(out var text))
			{
				names = [];
				return false;
			}
			list.Add(text);
		}
		names = [.. list];
		return true;
	}

	private static byte[] Serialize(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}
