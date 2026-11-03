using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project;
using G3MLib.DataFile.Util;
using ImageMagick;

namespace G3MLib.Modding.Resources;

public static partial class ResourceImportService
{
	private sealed class RoomImportLookup
	{
		public Dictionary<string, GameMakerRoom> Rooms { get; } = new Dictionary<string, GameMakerRoom>(StringComparer.Ordinal);

		public Dictionary<string, GameMakerCode> Code { get; } = new Dictionary<string, GameMakerCode>(StringComparer.Ordinal);

		public Dictionary<string, GameMakerBackground> Backgrounds { get; } = new Dictionary<string, GameMakerBackground>(StringComparer.Ordinal);

		public Dictionary<string, GameMakerGameObject> GameObjects { get; } = new Dictionary<string, GameMakerGameObject>(StringComparer.Ordinal);

		public Dictionary<string, GameMakerSprite> Sprites { get; } = new Dictionary<string, GameMakerSprite>(StringComparer.Ordinal);

		public Dictionary<string, GameMakerSequence> Sequences { get; } = new Dictionary<string, GameMakerSequence>(StringComparer.Ordinal);

		public Dictionary<string, GameMakerString> Strings { get; } = new Dictionary<string, GameMakerString>(StringComparer.Ordinal);

		public static RoomImportLookup Build(GameMakerData data)
		{
			var lookup = new RoomImportLookup();
			foreach (var room in data.Rooms)
			{
				if (room != null && room.Name?.Content != null)
				{
					lookup.Rooms.TryAdd(room.Name.Content, room);
				}
			}
			foreach (var code in data.Code)
			{
				if (code != null && code.Name?.Content != null)
				{
					lookup.Code.TryAdd(code.Name.Content, code);
				}
			}
			foreach (var bg in data.Backgrounds)
			{
				if (bg != null && bg.Name?.Content != null)
				{
					lookup.Backgrounds.TryAdd(bg.Name.Content, bg);
				}
			}
			foreach (var obj in data.GameObjects)
			{
				if (obj != null && obj.Name?.Content != null)
				{
					lookup.GameObjects.TryAdd(obj.Name.Content, obj);
				}
			}
			foreach (var sprite in data.Sprites)
			{
				if (sprite != null && sprite.Name?.Content != null)
				{
					lookup.Sprites.TryAdd(sprite.Name.Content, sprite);
				}
			}
			foreach (var str in data.Strings)
			{
				if (str != null && str.Content != null)
				{
					lookup.Strings.TryAdd(str.Content, str);
				}
			}
			if (data.Sequences != null)
			{
				foreach (var sequence in data.Sequences)
				{
					if (sequence != null && sequence.Name?.Content != null)
					{
						lookup.Sequences.TryAdd(sequence.Name.Content, sequence);
					}
				}
			}
			return lookup;
		}
	}

	private sealed class SpriteImportData
	{
		public int TargetIndex;

		public string Name = "";

		public string FolderName = "";

		public string FolderPath = "";

		public JsonElement? Meta;

		public List<Dictionary<string, object>>? FrameData;

		public bool IsNew;

		public bool HasValidTextureIndex;
	}

	private sealed record PlacedFrame(string Source, GameMakerTexturePageItem Item);

	private static readonly AsyncLocal<PatchFileSystem?> PatchFileSystemContext = new();
	private static readonly AsyncLocal<(int Start, int End)?> ProgressRangeContext = new();
	private static readonly AsyncLocal<string?> LoadedDataPathContext = new();
	private static readonly AsyncLocal<string?> SavingDataPathContext = new();

	private static PatchFileSystem? _pfs { get => PatchFileSystemContext.Value; set => PatchFileSystemContext.Value = value; }
	private static (int Start, int End)? _progressRange { get => ProgressRangeContext.Value; set => ProgressRangeContext.Value = value; }
	private static string? _loadedDataPath { get => LoadedDataPathContext.Value; set => LoadedDataPathContext.Value = value; }
	private static string? _savingDataPath { get => SavingDataPathContext.Value; set => SavingDataPathContext.Value = value; }
	private sealed record OnlyFilter(HashSet<string> Names, HashSet<string> SafeNames);
	private static readonly AsyncLocal<OnlyFilter?> OnlyFilterContext = new();
	private static OnlyFilter? _only { get => OnlyFilterContext.Value; set => OnlyFilterContext.Value = value; }

	private static void ImportGameObjects(GameMakerData data, string inputDir)
	{
		var objDirs = GetTopLevelDirs(inputDir);
		if (objDirs.Length == 0)
		{
			return;
		}
		Log($"[ImportGameObjects] Found {objDirs.Length} game object folders to import");
		BatchRun.Expect(objDirs.Length);
		var lookup = DataLookup.Build(data);
		Dictionary<GameMakerGameObject, int> objectIndices = [];
		for (var j = 0; j < data.GameObjects.Count; j++)
		{
			if (data.GameObjects[j] != null)
			{
				objectIndices[data.GameObjects[j]] = j;
			}
		}
		List<(int, string, JsonElement, bool, int)> importDataList = [];
		var array = objDirs;
		foreach (var objDir in array)
		{
			var jsonFile = Path.Combine(objDir, "object.json");
			if (!FExists(jsonFile))
			{
				BatchRun.Item(Path.GetFileName(objDir), ResultKind.Skipped, "Missing object.json");
				continue;
			}
			try
			{
				using var jsonDoc = JsonDocument.Parse(FReadText(jsonFile));
				var root = jsonDoc.RootElement.Clone();
				var objName = "";
				var targetIndex = -1;
				if (root.TryGetProperty("index", out var indexElm))
				{
					targetIndex = indexElm.GetInt32();
				}
				var folderName = Path.GetFileName(objDir);
				var idxPos = folderName.LastIndexOf("__idx", StringComparison.Ordinal);
				if (idxPos > 0 && folderName.Length >= idxPos + 9 && int.TryParse(folderName.AsSpan(idxPos + 5, 4), out var parsedIdx))
				{
					targetIndex = parsedIdx;
				}
				if (root.TryGetProperty("name", out var nameElm))
				{
					objName = nameElm.GetString() ?? "";
				}
				else
				{
					objName = (idxPos > 0) ? folderName[..idxPos] : folderName;
				}
				if (!string.IsNullOrEmpty(objName))
				{
					var nameCount = importDataList.Count(((int TargetIndex, string Name, JsonElement Root, bool IsNew, int ActualIndex) d) => d.Name == objName);
					var isNew = FindObject(objName) == null || nameCount > 0;
					importDataList.Add((targetIndex, objName, root, isNew, -1));
				}
				else
				{
					BatchRun.Item(Path.GetFileName(objDir), ResultKind.Skipped, "Object has no name");
				}
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log("[ImportGameObjects] Parse error: " + Path.GetFileName(objDir) + ": " + ex.Message);
				BatchRun.Failed(Path.GetFileName(objDir), ex);
			}
		}
		var created = 0;
		var updated = 0;
		var imported = 0;
		for (var i2 = 0; i2 < importDataList.Count; i2++)
		{
			var (targetIndex2, name, root2, isNew2, _) = importDataList[i2];
			if (isNew2)
			{
				if (targetIndex2 >= 0 && targetIndex2 < data.GameObjects.Count && string.Equals(data.GameObjects[targetIndex2]?.Name?.Content, name, StringComparison.Ordinal))
				{
					importDataList[i2] = (targetIndex2, name, root2, isNew2, targetIndex2);
					updated++;
					continue;
				}
				var go = new GameMakerGameObject
				{
					Name = MakeObjectString(name)
				};
				lookup.Add(go);
				objectIndices[go] = data.GameObjects.Count - 1;
				importDataList[i2] = (targetIndex2, name, root2, isNew2, data.GameObjects.Count - 1);
				created++;
			}
		}
		foreach (var (targetIndex3, name2, root3, isNew3, actualIndex) in importDataList)
		{
			try
			{
				GameMakerGameObject? go2 = null;
				if (targetIndex3 >= 0 && targetIndex3 < data.GameObjects.Count && string.Equals(data.GameObjects[targetIndex3]?.Name?.Content, name2, StringComparison.Ordinal))
				{
					go2 = data.GameObjects[targetIndex3];
				}
				else if (isNew3 && actualIndex >= 0 && actualIndex < data.GameObjects.Count)
				{
					go2 = data.GameObjects[actualIndex];
				}
				else if (targetIndex3 >= 0 && targetIndex3 < data.GameObjects.Count)
				{
					go2 = data.GameObjects[targetIndex3];
					if (go2?.Name?.Content != name2)
					{
						go2 = FindObject(name2);
					}
				}
				else
				{
					go2 = FindObject(name2);
				}
				if (go2 == null)
				{
					BatchRun.Item(name2, ResultKind.Skipped, "Object not found in the target data");
					continue;
				}
				if (root3.TryGetProperty("parent", out var parentElm))
				{
					var pName = parentElm.GetString() ?? "";
					go2.ParentId = (pName.Length > 0) ? FindObject(pName) : null;
				}
				if (root3.TryGetProperty("sprite", out var sprElm))
				{
					var sn = sprElm.GetString() ?? "";
					if (sn.Length > 0)
					{
						var spr = FindSprite(sn);
						if (spr != null)
						{
							go2.Sprite = spr;
						}
					}
					else
					{
						go2.Sprite = null;
					}
				}
				if (root3.TryGetProperty("visible", out var visElm))
				{
					go2.Visible = visElm.GetBoolean();
				}
				if (root3.TryGetProperty("solid", out var solElm))
				{
					go2.Solid = solElm.GetBoolean();
				}
				if (root3.TryGetProperty("depth", out var depElm))
				{
					go2.Depth = depElm.GetInt32();
				}
				if (root3.TryGetProperty("persistent", out var perElm))
				{
					go2.Persistent = perElm.GetBoolean();
				}
				if (root3.TryGetProperty("textureMask", out var tmElm))
				{
					var tmName = tmElm.GetString() ?? "";
					go2.TextureMaskId = (tmName.Length > 0) ? FindSprite(tmName) : null;
				}
				if (data.IsVersionAtLeast(2022u, 5u) && root3.TryGetProperty("managed", out var mngElm))
				{
					go2.Managed = mngElm.GetBoolean();
				}
				if (root3.TryGetProperty("usesPhysics", out var upElm))
				{
					go2.UsesPhysics = upElm.GetBoolean();
				}
				if (root3.TryGetProperty("isSensor", out var isElm))
				{
					go2.IsSensor = isElm.GetBoolean();
				}
				if (root3.TryGetProperty("collisionShape", out var csElm))
				{
					go2.CollisionShape = (CollisionShapeFlags)csElm.GetInt32();
				}
				if (root3.TryGetProperty("density", out var denElm))
				{
					go2.Density = (float)denElm.GetDouble();
				}
				if (root3.TryGetProperty("restitution", out var resElm))
				{
					go2.Restitution = (float)resElm.GetDouble();
				}
				if (root3.TryGetProperty("group", out var grpElm))
				{
					go2.Group = (uint)grpElm.GetInt64();
				}
				if (root3.TryGetProperty("linearDamping", out var ldElm))
				{
					go2.LinearDamping = (float)ldElm.GetDouble();
				}
				if (root3.TryGetProperty("angularDamping", out var adElm))
				{
					go2.AngularDamping = (float)adElm.GetDouble();
				}
				if (root3.TryGetProperty("friction", out var frElm))
				{
					go2.Friction = (float)frElm.GetDouble();
				}
				if (root3.TryGetProperty("awake", out var awElm))
				{
					go2.Awake = awElm.GetBoolean();
				}
				if (root3.TryGetProperty("kinematic", out var kinElm))
				{
					go2.Kinematic = kinElm.GetBoolean();
				}
				if (root3.TryGetProperty("physicsVertices", out var pvElm) && pvElm.ValueKind == JsonValueKind.Array)
				{
					go2.PhysicsVertices.Clear();
					var collection = go2.PhysicsVertices;
					var addMethod = collection.GetType().GetMethod("Add");
					var vertexType = collection.GetType().GetGenericArguments()[0];
					foreach (var ve in pvElm.EnumerateArray())
					{
						var vertex = Activator.CreateInstance(vertexType);
						if (ve.TryGetProperty("x", out var vxElm))
						{
							vertexType.GetProperty("X").SetValue(vertex, (float)vxElm.GetDouble());
						}
						if (ve.TryGetProperty("y", out var vyElm))
						{
							vertexType.GetProperty("Y").SetValue(vertex, (float)vyElm.GetDouble());
						}
						addMethod.Invoke(collection, [vertex]);
					}
				}
				if (root3.TryGetProperty("events", out var evtsElm) && evtsElm.ValueKind == JsonValueKind.Array)
				{
					for (var ei = 0; ei < go2.Events.Count; ei++)
					{
						go2.Events[ei].Clear();
					}
					foreach (var evtElm in evtsElm.EnumerateArray())
					{
						if (!evtElm.TryGetProperty("eventType", out var etElm) || !evtElm.TryGetProperty("eventSubtype", out var esElm))
						{
							continue;
						}
						var eventType = etElm.GetInt32();
						var eventSubtype = (uint)esElm.GetInt64();
						if (eventType == 4 && evtElm.TryGetProperty("collisionObjectName", out var coElm))
						{
							var coName = coElm.GetString() ?? "";
							if (coName.Length > 0)
							{
								var co = FindObject(coName);
								if (co == null || !objectIndices.TryGetValue(co, out var coIndex))
								{
									continue;
								}
								eventSubtype = (uint)coIndex;
							}
						}
						if (eventType < 0 || eventType >= go2.Events.Count)
						{
							continue;
						}
						GameMakerGameObject.Event? existingEvt = null;
						foreach (var e in go2.Events[eventType])
						{
							if (e.EventSubtype == eventSubtype)
							{
								existingEvt = e;
								break;
							}
						}
						if (existingEvt == null)
						{
							existingEvt = new GameMakerGameObject.Event
							{
								EventSubtype = eventSubtype
							};
							go2.Events[eventType].Add(existingEvt);
						}
						if (!evtElm.TryGetProperty("actions", out var actsElm) || actsElm.ValueKind != JsonValueKind.Array)
						{
							continue;
						}
						var ai = 0;
						foreach (var aElm in actsElm.EnumerateArray())
						{
							GameMakerGameObject.EventAction action;
							if (ai < existingEvt.Actions.Count)
							{
								action = existingEvt.Actions[ai];
							}
							else
							{
								action = new GameMakerGameObject.EventAction();
								existingEvt.Actions.Add(action);
							}
							ApplyEventAction(data, action, aElm);
							ai++;
						}
					}
				}
				if (!isNew3)
				{
					updated++;
				}
				imported++;
				BatchRun.Item(name2);
			}
			catch (Exception ex2) when (!ModdingCancellation.IsCancellation(ex2))
			{
				Log("[ImportGameObjects] Error: " + name2 + ": " + ex2.Message);
				BatchRun.Failed(name2, ex2);
			}
		}
		Log($"[ImportGameObjects] Done. {imported} imported ({created} new, {updated} updated)");
		GameMakerGameObject? FindObject(string key)
		{
			return lookup.TryGet(key, out GameMakerGameObject obj2, out _) ? obj2 : null;
		}
		GameMakerSprite? FindSprite(string key)
		{
			return lookup.TryGet(key, out GameMakerSprite sprite2, out _) ? sprite2 : null;
		}
		GameMakerString MakeObjectString(string content)
		{
			return lookup.GetOrAddString(content);
		}
	}

	private static void ImportAssetOrderInternal(GameMakerData data, string inputDir)
	{
		var assetOrderPath = Path.Combine(inputDir, "asset_order.txt");
		if (!FExists(assetOrderPath))
		{
			Log("[ImportAssetOrder] No asset_order.txt found - skipping");
			return;
		}
		Log("[ImportAssetOrder] Loading asset order from: " + assetOrderPath);
		var array = FReadLines(assetOrderPath);
		var currentType = "";
		List<string> currentList = [];
		var totalReorganized = 0;
		Dictionary<string, int> targetCounts = [];
		var array2 = array;
		foreach (var line in array2)
		{
			if (line.StartsWith("@@", StringComparison.Ordinal) && line.EndsWith("@@", StringComparison.Ordinal))
			{
				SubmitList();
				var text = line;
				currentType = text[2..^2].ToLowerInvariant();
				currentList.Clear();
			}
			else if (currentType == "counts" && line.Contains('=', StringComparison.Ordinal))
			{
				var parts = line.Trim().Split('=', 2);
				if (parts.Length == 2 && int.TryParse(parts[1], out var count))
				{
					targetCounts[parts[0]] = count;
				}
			}
			else if (!string.IsNullOrWhiteSpace(line))
			{
				currentList.Add(line.Trim());
			}
		}
		SubmitList();
		Log($"[ImportAssetOrder] Reorganized {totalReorganized} asset types");
		var eventsPath = Path.Combine(inputDir, "object_events.json");
		if (FExists(eventsPath))
		{
			var targetEventsRoot = JsonSerializer.Deserialize<JsonElement>(FReadText(eventsPath));
			var eventsRemoved = 0;
			var objectsFixed = 0;
			foreach (var obj in data.GameObjects)
			{
				if (obj == null || obj.Name?.Content == null || !targetEventsRoot.TryGetProperty(obj.Name.Content, out var targetEvents))
				{
					continue;
				}
				HashSet<string> keys = [];
				var collisionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var evt in targetEvents.EnumerateArray())
				{
					var t = evt.GetProperty("t").GetInt32();
					if (t == 4 && evt.TryGetProperty("cn", out var cnElem))
					{
						var cn = cnElem.GetString();
						if (!string.IsNullOrEmpty(cn))
						{
							var occurrence = evt.TryGetProperty("co", out var coElem) ? coElem.GetInt32() : (-1);
							collisionNames.Add($"{cn}#{occurrence}");
							continue;
						}
					}
					keys.Add($"{t}_{evt.GetProperty("s").GetUInt32()}");
				}
				var fixedObj = false;
				for (var et = 0; et < obj.Events.Count; et++)
				{
					for (var j = obj.Events[et].Count - 1; j >= 0; j--)
					{
						bool shouldKeep;
						if (et == 4)
						{
							var collObjIdx = (int)obj.Events[et][j].EventSubtype;
							var collObjName = (collObjIdx < 0 || collObjIdx >= data.GameObjects.Count) ? null : data.GameObjects[collObjIdx]?.Name?.Content;
							var occurrence2 = (!string.IsNullOrEmpty(collObjName)) ? ResourceAssetUtil.GetGameObjectOccurrence(data, collObjIdx) : (-1);
							shouldKeep = !string.IsNullOrEmpty(collObjName) && (collisionNames.Contains($"{collObjName}#{occurrence2}") || collisionNames.Contains(collObjName + "#-1"));
						}
						else
						{
							shouldKeep = keys.Contains($"{et}_{obj.Events[et][j].EventSubtype}");
						}
						if (!shouldKeep)
						{
							var removedEvt = obj.Events[et][j];
							var evtCode = (removedEvt.Actions.Count <= 0) ? "?" : (removedEvt.Actions[0].CodeId?.Name?.Content ?? "?");
							Log($"  EventSync: removing {obj.Name.Content} event type={et} sub={removedEvt.EventSubtype} code={evtCode}");
							obj.Events[et].RemoveAt(j);
							eventsRemoved++;
							fixedObj = true;
						}
					}
				}
				if (fixedObj)
				{
					objectsFixed++;
				}
			}
			if (eventsRemoved > 0)
			{
				Log($"[ImportAssetOrder] Event sync: removed {eventsRemoved} events from {objectsFixed} objects");
			}
		}
		ImportEmbeddedTexturesForAssetOrder(data, inputDir);
		if (targetCounts.TryGetValue("EmbeddedTextures", out var targetTexCount))
		{
			var cur = data.EmbeddedTextures.Count;
			if (cur > targetTexCount)
			{
				for (var i2 = cur - 1; i2 >= targetTexCount; i2--)
				{
					data.EmbeddedTextures.RemoveAt(i2);
				}
				Log($"[ImportAssetOrder] EmbeddedTextures: trimmed from {cur} to {targetTexCount}");
			}
		}
		var tpiPath = Path.Combine(inputDir, "texture_page_items.json");
		var fmPath = Path.Combine(inputDir, "sprite_frame_map.json");
		if (FExists(tpiPath) && FExists(fmPath))
		{
			var oldCount = data.TexturePageItems.Count;
			var tpiData = JsonSerializer.Deserialize<List<int[]>>(FReadText(tpiPath));
			if (!CanResolveTexturePages(tpiData, data.EmbeddedTextures.Count))
			{
				Log("[ImportAssetOrder] Skipping TexturePageItems relink because texture pages are not present in this patch");
				return;
			}
			var newCount = tpiData.Count;
			var upd = Math.Min(oldCount, newCount);
			for (var k = 0; k < upd; k++)
			{
				ApplyTpi(data.TexturePageItems[k], tpiData[k]);
			}
			for (var l = oldCount; l < newCount; l++)
			{
				var tpi = new GameMakerTexturePageItem();
				ApplyTpi(tpi, tpiData[l]);
				data.TexturePageItems.Add(tpi);
			}
			for (var i3 = oldCount - 1; i3 >= newCount; i3--)
			{
				data.TexturePageItems.RemoveAt(i3);
			}
			Log($"[ImportAssetOrder] TexturePageItems: {oldCount} -> {data.TexturePageItems.Count}");
			var fmRoot = JsonSerializer.Deserialize<JsonElement>(FReadText(fmPath));
			var sprRelinked = 0;
			if (fmRoot.TryGetProperty("sprites", out var sprMap))
			{
				foreach (var spr in data.Sprites)
				{
					if (spr?.Textures == null)
					{
						continue;
					}
					var key = spr.Name?.Content ?? data.Sprites.IndexOf(spr).ToString(CultureInfo.InvariantCulture);
					if (!sprMap.TryGetProperty(key, out var indices))
					{
						continue;
					}
					int[] array3 = [.. from e in indices.EnumerateArray()
									select e.GetInt32()];
					spr.Textures.Clear();
					var array4 = array3;
					foreach (var tpiIdx in array4)
					{
						var entry = new GameMakerSprite.TextureEntry();
						if (tpiIdx >= 0 && tpiIdx < data.TexturePageItems.Count)
						{
							entry.Texture = data.TexturePageItems[tpiIdx];
						}
						spr.Textures.Add(entry);
					}
					sprRelinked++;
				}
			}
			var bgRelinked = 0;
			if (fmRoot.TryGetProperty("backgrounds", out var bgMap))
			{
				foreach (var bg in data.Backgrounds)
				{
					if (bg == null)
					{
						continue;
					}
					var key2 = bg.Name?.Content ?? data.Backgrounds.IndexOf(bg).ToString(CultureInfo.InvariantCulture);
					if (bgMap.TryGetProperty(key2, out var idx))
					{
						var ti = idx.GetInt32();
						if (ti >= 0 && ti < data.TexturePageItems.Count)
						{
							bg.Texture = data.TexturePageItems[ti];
							bgRelinked++;
						}
					}
				}
			}
			var fontRelinked = 0;
			if (fmRoot.TryGetProperty("fonts", out var fontMap))
			{
				foreach (var font in data.Fonts)
				{
					if (font == null)
					{
						continue;
					}
					var key3 = font.Name?.Content ?? data.Fonts.IndexOf(font).ToString(CultureInfo.InvariantCulture);
					if (fontMap.TryGetProperty(key3, out var idx2))
					{
						var ti2 = idx2.GetInt32();
						if (ti2 >= 0 && ti2 < data.TexturePageItems.Count)
						{
							font.Texture = data.TexturePageItems[ti2];
							fontRelinked++;
						}
					}
				}
			}
			Log($"[ImportAssetOrder] Relinked: {sprRelinked} sprites, {bgRelinked} backgrounds, {fontRelinked} fonts");
		}
		else
		{
			if (!targetCounts.TryGetValue("TexturePageItems", out var targetTpiCount))
			{
				return;
			}
			var cur2 = data.TexturePageItems.Count;
			if (cur2 > targetTpiCount)
			{
				for (var i4 = cur2 - 1; i4 >= targetTpiCount; i4--)
				{
					data.TexturePageItems.RemoveAt(i4);
				}
				Log($"[ImportAssetOrder] TexturePageItems: trimmed {cur2} to {targetTpiCount} (legacy)");
			}
		}
		void ApplyTpi(GameMakerTexturePageItem gameMakerTexturePageItem, int[] arr)
		{
			var texIdx = arr[0];
			gameMakerTexturePageItem.TexturePage = (texIdx >= 0 && texIdx < data.EmbeddedTextures.Count) ? data.EmbeddedTextures[texIdx] : ((data.EmbeddedTextures.Count > 0) ? data.EmbeddedTextures[0] : null);
			gameMakerTexturePageItem.SourceX = (ushort)arr[1];
			gameMakerTexturePageItem.SourceY = (ushort)arr[2];
			gameMakerTexturePageItem.SourceWidth = (ushort)arr[3];
			gameMakerTexturePageItem.SourceHeight = (ushort)arr[4];
			gameMakerTexturePageItem.TargetX = (ushort)arr[5];
			gameMakerTexturePageItem.TargetY = (ushort)arr[6];
			gameMakerTexturePageItem.TargetWidth = (ushort)arr[7];
			gameMakerTexturePageItem.TargetHeight = (ushort)arr[8];
			gameMakerTexturePageItem.BoundingWidth = (ushort)arr[9];
			gameMakerTexturePageItem.BoundingHeight = (ushort)arr[10];
		}
		void SubmitList()
		{
			if (currentList.Count != 0)
			{
				var text2 = currentType;
				if (text2 != null)
				{
					switch (text2.Length)
					{
						case 7:
							switch (text2[1])
							{
								case 'p':
									if (text2 == "sprites")
									{
										Reorganize(data.Sprites, currentList, "Sprites");
										totalReorganized++;
									}
									break;
								case 'c':
									if (text2 == "scripts")
									{
										var createdScripts = 0;
										foreach (var sn in currentList)
										{
											if (data.Scripts.ByName(sn) == null)
											{
												var script = new GameMakerScript
												{
													Name = data.Strings.MakeString(sn),
													Code = PatchService.ScriptCodeResolver.Resolve(data, sn)
												};
												data.Scripts.Add(script);
												createdScripts++;
											}
										}
										if (createdScripts > 0)
										{
											Log($"[ImportAssetOrder] Scripts: Created {createdScripts} new script entries");
										}
										Reorganize(data.Scripts, currentList, "Scripts");
										totalReorganized++;
										while (data.Scripts.Count > currentList.Count)
										{
											data.Scripts.RemoveAt(data.Scripts.Count - 1);
										}
									}
									break;
								case 'b':
									if (text2 == "objects")
									{
										List<(GameMakerGameObject, int, string)> collisionInfo = [];
										foreach (var obj2 in data.GameObjects)
										{
											if (obj2 != null)
											{
												var collisions = obj2.Events[4];
												for (var ci = 0; ci < collisions.Count; ci++)
												{
													var sub = collisions[ci].EventSubtype;
													if (sub < (uint)data.GameObjects.Count)
													{
														var target = data.GameObjects[(int)sub];
														if (target != null && target.Name?.Content != null)
														{
															collisionInfo.Add((obj2, ci, target.Name.Content));
														}
													}
												}
											}
										}
										Reorganize(data.GameObjects, currentList, "GameObjects");
										var objectIndicesByName = new Dictionary<string, uint>(StringComparer.Ordinal);
										for (var objectIndex = 0; objectIndex < data.GameObjects.Count; objectIndex++)
										{
											var objectName = data.GameObjects[objectIndex]?.Name?.Content;
											if (objectName != null)
											{
												objectIndicesByName.TryAdd(objectName, (uint)objectIndex);
											}
										}
										var fixedCollisions = 0;
										foreach (var (obj3, evtIdx, tName) in collisionInfo)
										{
											if (objectIndicesByName.TryGetValue(tName, out var newIdx) && evtIdx < obj3.Events[4].Count)
											{
												var collisions2 = obj3.Events[4];
												if (collisions2[evtIdx].EventSubtype != newIdx)
												{
													collisions2[evtIdx].EventSubtype = newIdx;
													fixedCollisions++;
												}
											}
										}
										if (fixedCollisions > 0)
										{
											Log($"[ImportAssetOrder] Fixed {fixedCollisions} collision event subtypes");
										}
										totalReorganized++;
									}
									break;
								case 'h':
									if (text2 == "shaders")
									{
										Reorganize(data.Shaders, currentList, "Shaders");
										totalReorganized++;
									}
									break;
							}
							break;
						case 11:
							switch (text2[0])
							{
								case 'b':
									if (text2 == "backgrounds")
									{
										Reorganize(data.Backgrounds, currentList, "Backgrounds");
										totalReorganized++;
									}
									break;
								case 'a':
									if (text2 == "audiogroups")
									{
										Reorganize(data.AudioGroups, currentList, "AudioGroups");
										totalReorganized++;
									}
									break;
							}
							break;
						case 5:
							switch (text2[0])
							{
								case 'p':
									if (text2 == "paths")
									{
										Reorganize(data.Paths, currentList, "Paths");
										totalReorganized++;
									}
									break;
								case 'f':
									if (text2 == "fonts")
									{
										Reorganize(data.Fonts, currentList, "Fonts");
										totalReorganized++;
									}
									break;
								case 'r':
									if (text2 == "rooms")
									{
										Reorganize(data.Rooms, currentList, "Rooms");
										totalReorganized++;
									}
									break;
							}
							break;
						case 6:
							if (text2 == "sounds")
							{
								Reorganize(data.Sounds, currentList, "Sounds");
								totalReorganized++;
							}
							break;
						case 9:
							if (text2 == "timelines")
							{
								Reorganize(data.Timelines, currentList, "Timelines");
								totalReorganized++;
							}
							break;
						case 10:
							if (text2 == "extensions")
							{
								Reorganize(data.Extensions, currentList, "Extensions");
								totalReorganized++;
							}
							break;
						case 8:
							break;
					}
				}
			}
		}
	}

	private static bool CanResolveTexturePages(List<int[]> tpiData, int embeddedTextureCount)
	{
		foreach (var tpi in tpiData)
		{
			if (tpi.Length != 0 && tpi[0] >= embeddedTextureCount)
			{
				return false;
			}
		}
		return true;
	}

	private static int ImportEmbeddedTexturesForAssetOrder(GameMakerData data, string inputDir)
	{
		var embeddedDir = Path.Combine(inputDir, "EmbeddedTextures");
		if (!DirExists(embeddedDir))
		{
			var parent = Path.GetDirectoryName(inputDir);
			embeddedDir = string.IsNullOrEmpty(parent) ? "EmbeddedTextures" : Path.Combine(parent, "EmbeddedTextures");
		}
		if (!DirExists(embeddedDir))
		{
			BatchRun.Item("EmbeddedTextures", ResultKind.Skipped, "Folder not found");
			return 0;
		}
		var imported = 0;
		var dirs = GetTopLevelDirs(embeddedDir);
		BatchRun.Expect(dirs.Length);
		foreach (var texDir in dirs)
		{
			var folderName = Path.GetFileName(texDir.TrimEnd(
			[
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar
			]));
			var jsonPath = Path.Combine(texDir, folderName + ".json");
			var pngPath = Path.Combine(texDir, folderName + ".png");
			var binPath = Path.Combine(texDir, folderName + ".bin");
			if (!FExists(jsonPath) || !FExists(binPath) && !FExists(pngPath))
			{
				BatchRun.Item(folderName, ResultKind.Skipped, "Missing json or image file");
				continue;
			}
			try
			{
				using var doc = JsonDocument.Parse(FReadText(jsonPath));
				var root = doc.RootElement;
				var folderIndex = ParseTrailingResourceIndex(folderName);
				var jsonIndex = (root.TryGetProperty("index", out var idxElm) && idxElm.ValueKind == JsonValueKind.Number) ? idxElm.GetInt32() : data.EmbeddedTextures.Count;
				var index = (folderIndex >= 0) ? folderIndex : jsonIndex;
				var name = (root.TryGetProperty("name", out var nameElm) && nameElm.ValueKind == JsonValueKind.String) ? (nameElm.GetString() ?? "") : "";
				var format = (root.TryGetProperty("format", out var formatElm) && formatElm.ValueKind == JsonValueKind.String) ? (formatElm.GetString() ?? "Png") : "Png";
				while (data.EmbeddedTextures.Count <= index)
				{
					data.EmbeddedTextures.Add(new GameMakerEmbeddedTexture
					{
						Name = new GameMakerString($"Texture {data.EmbeddedTextures.Count}")
					});
				}
				var texture = data.EmbeddedTextures[index];
				texture.Name = new GameMakerString(string.IsNullOrWhiteSpace(name) ? $"Texture {index}" : name);
				if (root.TryGetProperty("scaled", out var scaledElm))
				{
					texture.Scaled = (scaledElm.ValueKind == JsonValueKind.Number) ? scaledElm.GetUInt32() : (scaledElm.GetBoolean() ? 1u : 0u);
				}
				if (root.TryGetProperty("generatedMips", out var mipsElm))
				{
					texture.GeneratedMips = (mipsElm.ValueKind == JsonValueKind.Number) ? mipsElm.GetUInt32() : (mipsElm.GetBoolean() ? 1u : 0u);
				}
				var loadedImage = false;
				if (FExists(binPath))
				{
					try
					{
						using var stream = new MemoryStream(FReadBytes(binPath), writable: false);
						using var reader = new FileBinaryReader(stream);
						texture.TextureData.Image = GMImage.FromBinaryReader(reader, stream.Length, data.IsVersionAtLeast(2022u, 5u));
						loadedImage = true;
					}
					catch (Exception ex) when (FExists(pngPath))
					{
						Log("[ImportAssetOrder] EmbeddedTexture '" + folderName + "' bin import failed, using png: " + ex.Message);
					}
				}
				if (!loadedImage)
				{
					using var image = new MagickImage(FReadBytes(pngPath));
					var gmImage = GMImage.FromMagickImage(image);
					gmImage = (!Enum.TryParse<GMImage.ImageFormat>(format, out var targetFormat) || targetFormat == GMImage.ImageFormat.Png) ? gmImage.ConvertToPng() : gmImage.ConvertToFormat(targetFormat);
					texture.TextureData.Image = gmImage;
				}
				imported++;
				BatchRun.Item(folderName);
			}
			catch (Exception ex2) when (!ModdingCancellation.IsCancellation(ex2))
			{
				Log("[ImportAssetOrder] EmbeddedTexture import error '" + folderName + "': " + ex2.Message);
				BatchRun.Failed(folderName, ex2);
			}
		}
		if (imported > 0)
		{
			Log($"[ImportAssetOrder] Imported {imported} embedded texture page(s)");
		}
		return imported;
	}

	private static int ParseTrailingResourceIndex(string value)
	{
		var match = TrailingNumberRegex().Match(value);
		if (!match.Success || !int.TryParse(match.Groups[1].Value, out var index))
		{
			return -1;
		}
		return index;
	}

	[GeneratedRegex("(\\d+)$")]
	private static partial Regex TrailingNumberRegex();

	private static void Reorganize<T>(IList<T> list, List<string> order, string typeName) where T : GameMakerNamedResource, new()
	{
		if (order.Count == 0)
		{
			return;
		}
		Dictionary<string, List<int>> nameToIndices = [];
		List<int> emptyNameIndices = [];
		var nullIndices = new Queue<int>();
		for (var i = 0; i < list.Count; i++)
		{
			var asset = list[i];
			if (asset == null)
			{
				nullIndices.Enqueue(i);
				continue;
			}
			var name = asset.Name?.Content;
			if (string.IsNullOrEmpty(name))
			{
				emptyNameIndices.Add(i);
				continue;
			}
			if (!nameToIndices.TryGetValue(name, out var indices))
			{
				nameToIndices[name] = indices = [];
			}
			indices.Add(i);
		}
		List<T> newOrder = [];
		HashSet<int> usedIndices = [];
		var missingCount = 0;
		var emptyNameIdx = 0;
		foreach (var name2 in order)
		{
			if (name2 == "(null)")
			{
				if (nullIndices.Count > 0)
				{
					var idx = nullIndices.Dequeue();
					if (!usedIndices.Contains(idx))
					{
						newOrder.Add(default);
						usedIndices.Add(idx);
					}
				}
				else if (typeName.Equals("Sprites", StringComparison.OrdinalIgnoreCase))
				{
					newOrder.Add(default);
				}
			}
			else if (int.TryParse(name2, out var result))
			{
				if (emptyNameIdx < emptyNameIndices.Count)
				{
					var idx2 = emptyNameIndices[emptyNameIdx];
					if (!usedIndices.Contains(idx2))
					{
						newOrder.Add(list[idx2]);
						usedIndices.Add(idx2);
					}
					emptyNameIdx++;
				}
				else
				{
					newOrder.Add(default);
				}
			}
			else
			{
				if (string.IsNullOrWhiteSpace(name2))
				{
					continue;
				}
				if (nameToIndices.TryGetValue(name2, out var indices))
				{
					foreach (var idx3 in indices)
					{
						if (!usedIndices.Contains(idx3))
						{
							newOrder.Add(list[idx3]);
							usedIndices.Add(idx3);
							break;
						}
					}
				}
				else
				{
					missingCount++;
				}
			}
		}
		var preservedCount = 0;
		for (var j = 0; j < list.Count; j++)
		{
			if (!usedIndices.Contains(j) && list[j] != null)
			{
				newOrder.Add(list[j]);
				usedIndices.Add(j);
				preservedCount++;
			}
		}
		if (preservedCount > 0)
		{
			Log($"[ImportAssetOrder] {typeName}: Preserved {preservedCount} resource(s) not in TARGET order");
		}
		list.Clear();
		foreach (var asset2 in newOrder)
		{
			list.Add(asset2);
		}
		Log($"[ImportAssetOrder] {typeName}: Reorganized {list.Count} items (missing: {missingCount}, preserved: {preservedCount})");
	}

	public static void SetPatchFileSystem(PatchFileSystem? pfs)
	{
		_pfs = pfs;
	}

	public static void SetDataPaths(string? loadedDataPath, string? savingDataPath)
	{
		_loadedDataPath = loadedDataPath;
		_savingDataPath = savingDataPath;
	}

	public static PatchFileSystem? GetPatchFileSystem()
	{
		return _pfs;
	}

	public static void SetProgressRange(int? startPercent = null, int? endPercent = null)
	{
		_progressRange = (startPercent.HasValue && endPercent.HasValue) ? new (int, int)?((startPercent.Value, endPercent.Value)) : null;
	}

	private static void ReportProgress(int current, int total)
	{
		ModdingCancellation.Throw();
		(int, int)? progressRange = _progressRange;
		if (progressRange.HasValue)
		{
			var range = progressRange.GetValueOrDefault();
			LogService.ProgressRange(current, total, range.Item1, range.Item2);
		}
	}

	// Linux and macOS return directory entries in no fixed order; sorting keeps import order the same on every platform.
	private static string[] Sorted(string[] paths)
	{
		Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
		return paths;
	}

	private static string[] GetDirs(string path)
	{
		ModdingCancellation.Throw();
		return Sorted(_pfs == null ? Directory.GetDirectories(path) : _pfs.GetDirectories(path));
	}

	private static string[] GetTopLevelDirs(string path)
	{
		var dirs = GetDirs(path);
		return _only is null ? dirs : [.. dirs.Where(MatchesOnlyDir)];
	}

	private static string ResolveTypeDir(string inputDir, string resourceType)
	{
		// A folder that is already the type folder stays as it is, even when one of its entries has the name of the type,
		// unless that entry is its only content: then it is an outer folder that holds the real type folder (export writes outputDir/Type).
		if (Path.GetFileName(Path.TrimEndingDirectorySeparator(inputDir)).Equals(resourceType, StringComparison.OrdinalIgnoreCase)
			&& !(DirExists(Path.Combine(inputDir, resourceType)) && GetDirs(inputDir).Length == 1))
		{
			return inputDir;
		}

		var typeDir = Path.Combine(inputDir, resourceType);
		return DirExists(typeDir) ? typeDir : inputDir;
	}

	private static bool MatchesOnlyName(string name)
	{
		if (_only is not { } only)
		{
			return true;
		}
		if (only.Names.Contains(name) || only.SafeNames.Contains(name))
		{
			return true;
		}
		var idxPos = name.LastIndexOf("__idx", StringComparison.Ordinal);
		if (idxPos > 0)
		{
			var prefix = name[..idxPos];
			if (only.Names.Contains(prefix) || only.SafeNames.Contains(prefix))
			{
				return true;
			}
		}
		return false;
	}

	private static bool MatchesOnlyDir(string dir)
	{
		if (_only is not { } only)
		{
			return true;
		}
		if (only.Names.Count == 0)
		{
			return false;
		}
		var folderName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrEmpty(folderName))
		{
			return false;
		}
		if (MatchesOnlyName(folderName))
		{
			return true;
		}
		string[] candidates =
		[
			Path.Combine(dir, folderName + ".json"),
			Path.Combine(dir, "object.json"),
			Path.Combine(dir, "room.json"),
			Path.Combine(dir, "font.json"),
			Path.Combine(dir, "sprite_meta.json")
		];
		foreach (var candidate in candidates)
		{
			if (!FExists(candidate))
			{
				continue;
			}
			try
			{
				using var doc = JsonDocument.Parse(FReadText(candidate));
				var root = doc.RootElement;
				if (root.ValueKind != JsonValueKind.Object)
				{
					continue;
				}
				if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
					&& nameEl.GetString() is { Length: > 0 } jsonName && MatchesOnlyName(jsonName))
				{
					return true;
				}
				if (root.TryGetProperty("key", out var keyEl) && keyEl.ValueKind == JsonValueKind.String
					&& keyEl.GetString() is { Length: > 0 } jsonKey && MatchesOnlyName(jsonKey))
				{
					return true;
				}
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
			}
		}
		return false;
	}

	private static string[] GetFilesIn(string path, string pattern = "*")
	{
		ModdingCancellation.Throw();
		return Sorted(_pfs == null ? Directory.GetFiles(path, pattern) : _pfs.GetFiles(path, pattern));
	}

	private static bool FExists(string path)
	{
		if (_pfs == null)
		{
			return File.Exists(path);
		}
		return _pfs.FileExists(path);
	}

	private static string FReadText(string path)
	{
		ModdingCancellation.Throw();
		if (_pfs == null)
		{
			return File.ReadAllText(path, Encoding.UTF8);
		}
		return _pfs.ReadAllText(path);
	}

	private static byte[] FReadBytes(string path)
	{
		ModdingCancellation.Throw();
		if (_pfs == null)
		{
			return File.ReadAllBytes(path);
		}
		return _pfs.ReadAllBytes(path);
	}

	private static string[] FReadLines(string path)
	{
		ModdingCancellation.Throw();
		if (_pfs == null)
		{
			return File.ReadAllLines(path);
		}
		return _pfs.ReadAllLines(path);
	}

	private static bool DirExists(string path)
	{
		if (_pfs == null)
		{
			return Directory.Exists(path);
		}
		return _pfs.DirectoryExists(path);
	}

	private static string SafeName(string name)
	{
		return ResourceExportService.SafeName(name);
	}

	/// <summary>Cancelling throws <see cref="OperationCanceledException"/>. The input folder is never changed; <paramref name="data"/> may hold part of the import, so discard it.</summary>
	public static bool Import(string resourceType, GameMakerData data, string inputDir, CancellationToken cancellationToken)
	{
		using var cancellationScope = ModdingCancellation.Begin(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			var imported = Import(resourceType, data, inputDir);
			cancellationToken.ThrowIfCancellationRequested();
			return imported;
		}
		catch (Exception ex) when (ModdingCancellation.IsCancellation(ex))
		{
			ModdingCancellation.Rethrow(ex, cancellationToken);
			throw;
		}
	}

	/// <summary>Imports one resource type and returns one result per item. The input folder is never changed; cancelling throws <see cref="OperationCanceledException"/> and <paramref name="data"/> may hold part of the import.</summary>
	public static IReadOnlyList<ItemResult> Import(string resourceType, GameMakerData data, string inputDir, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
	{
		var targetDir = ResolveTypeDir(inputDir, resourceType);
		return BatchRun.RunImport(progress, cancellationToken, delegate
		{
			try
			{
				if (!Import(resourceType, data, targetDir))
				{
					throw new ArgumentException("Unknown resource type: " + resourceType, nameof(resourceType));
				}
			}
			catch (Exception ex) when (ex is not ArgumentException && !ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(resourceType, ex);
			}
		});
	}

	/// <summary>Imports only the named entries of one resource type and returns one result per item. When <paramref name="only"/> is null, every entry in <paramref name="inputDir"/> is imported. Cancelling throws <see cref="OperationCanceledException"/> and <paramref name="data"/> may hold part of the import.</summary>
	public static IReadOnlyList<ItemResult> Import(string resourceType, GameMakerData data, string inputDir, IEnumerable<string>? only, IProgress<BatchProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		if (only is null)
		{
			return Import(resourceType, data, inputDir, progress, cancellationToken);
		}
		if (IsFilterUnsupported(resourceType))
		{
			throw new NotSupportedException($"Resource type '{resourceType}' does not support filtering.");
		}
		var targetDir = ResolveTypeDir(inputDir, resourceType);
		var names = new HashSet<string>(only, StringComparer.Ordinal);
		var safeNames = new HashSet<string>(names.Select(SafeName), StringComparer.Ordinal);
		return BatchRun.RunImport(progress, cancellationToken, delegate
		{
			var previous = _only;
			_only = new OnlyFilter(names, safeNames);
			try
			{
				if (!Import(resourceType, data, targetDir))
				{
					throw new ArgumentException("Unknown resource type: " + resourceType, nameof(resourceType));
				}
			}
			catch (Exception ex) when (ex is not ArgumentException && ex is not NotSupportedException && !ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(resourceType, ex);
			}
			finally
			{
				_only = previous;
			}
		});
	}

	private static bool IsFilterUnsupported(string resourceType) =>
		resourceType is "Language" or "Options" or "GlobalScripts" or "FeatureFlags" or "Tags" or "GeneralInfo" or "TexturePageItems";

	/// <summary>Imports only the named entries per resource type from a folder of exported resource folders, in dependency order. Cancelling throws <see cref="OperationCanceledException"/> and <paramref name="data"/> may hold part of the import.</summary>
	public static IReadOnlyList<ItemResult> Import(GameMakerData data, string inputDir, IReadOnlyDictionary<string, IReadOnlySet<string>> only, IProgress<BatchProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(data);
		ArgumentNullException.ThrowIfNull(only);
		foreach (var pair in only)
		{
			if (pair.Value is not null && IsFilterUnsupported(pair.Key))
			{
				throw new NotSupportedException($"Resource type '{pair.Key}' does not support filtering.");
			}
		}
		return BatchRun.RunImport(progress, cancellationToken, delegate
		{
			HashSet<string> ran = new(StringComparer.Ordinal);
			foreach (var type in G3MLib.Modding.Internal.ResourceTypeRegistry.ImportOrder)
			{
				if (only.TryGetValue(type, out var names))
				{
					ran.Add(type);
					ImportFilteredType(type, data, inputDir, names);
				}
			}
			foreach (var pair in only)
			{
				if (ran.Add(pair.Key))
				{
					ImportFilteredType(pair.Key, data, inputDir, pair.Value);
				}
			}
		});
	}

	private static void ImportFilteredType(string resourceType, GameMakerData data, string inputDir, IEnumerable<string>? only)
	{
		var targetDir = ResolveTypeDir(inputDir, resourceType);
		var previous = _only;
		if (only is not null)
		{
			var names = new HashSet<string>(only, StringComparer.Ordinal);
			var safeNames = new HashSet<string>(names.Select(SafeName), StringComparer.Ordinal);
			_only = new OnlyFilter(names, safeNames);
		}
		else
		{
			_only = null;
		}
		try
		{
			if (!Import(resourceType, data, targetDir))
			{
				throw new ArgumentException("Unknown resource type: " + resourceType, nameof(resourceType));
			}
		}
		catch (Exception ex) when (ex is not ArgumentException && ex is not NotSupportedException && !ModdingCancellation.IsCancellation(ex))
		{
			BatchRun.Failed(resourceType, ex);
		}
		finally
		{
			_only = previous;
		}
	}

	public static bool Import(string resourceType, GameMakerData data, string inputDir)
	{
		ArgumentNullException.ThrowIfNull(data);
		switch (resourceType)
		{
			case "Language":
				if (_only is not null)
					throw new NotSupportedException($"Resource type '{resourceType}' does not support filtering.");
				ImportLanguage(data, inputDir);
				return true;
			case "Options":
				if (_only is not null)
					throw new NotSupportedException($"Resource type '{resourceType}' does not support filtering.");
				ImportOptions(data, inputDir);
				return true;
			case "GlobalScripts":
				if (_only is not null)
					throw new NotSupportedException($"Resource type '{resourceType}' does not support filtering.");
				ImportGlobalScripts(data, inputDir);
				return true;
			case "Scripts":
				ImportScripts(data, inputDir);
				return true;
			case "FeatureFlags":
				if (_only is not null)
					throw new NotSupportedException($"Resource type '{resourceType}' does not support filtering.");
				ImportFeatureFlags(data, inputDir);
				return true;
			case "Tags":
				if (_only is not null)
					throw new NotSupportedException($"Resource type '{resourceType}' does not support filtering.");
				ImportTags(data, inputDir);
				return true;
			case "FilterEffects":
				ImportFilterEffects(data, inputDir);
				return true;
			case "AudioGroups":
				ImportAudioGroups(data, inputDir);
				return true;
			case "EmbeddedAudio":
				ImportEmbeddedAudio(data, inputDir);
				return true;
			case "TextureGroupInfo":
				ImportTextureGroupInfo(data, inputDir);
				return true;
			case "EmbeddedTextures":
				ImportEmbeddedTexturesForAssetOrder(data, inputDir);
				return true;
			case "Sprites":
				ImportSprites(data, inputDir);
				return true;
			case "EmbeddedImages":
				ImportEmbeddedImages(data, inputDir);
				return true;
			case "Fonts":
				ImportFonts(data, inputDir);
				return true;
			case "Sounds":
				ImportSounds(data, inputDir);
				return true;
			case "Paths":
				ImportPaths(data, inputDir);
				return true;
			case "Shaders":
				ImportShaders(data, inputDir);
				return true;
			case "GameObjects":
				ImportGameObjects(data, inputDir);
				return true;
			case "Rooms":
				ImportRooms(data, inputDir);
				return true;
			case "Sequences":
				ImportSequences(data, inputDir);
				return true;
			case "Tilesets":
				ImportTilesets(data, inputDir);
				return true;
			case "Backgrounds":
				ImportBackgrounds(data, inputDir);
				return true;
			case "Extensions":
				ImportExtensions(data, inputDir);
				return true;
			case "Timelines":
				ImportTimelines(data, inputDir);
				return true;
			case "AnimationCurves":
				ImportAnimationCurves(data, inputDir);
				return true;
			case "ParticleSystemEmitters":
				ImportParticleSystemEmitters(data, inputDir);
				return true;
			case "ParticleSystems":
				ImportParticleSystems(data, inputDir);
				return true;
			case "GeneralInfo":
				if (_only is not null)
					throw new NotSupportedException($"Resource type '{resourceType}' does not support filtering.");
				ImportGeneralInfo(data, inputDir);
				return true;
			case "TexturePageItems":
				if (_only is not null)
					throw new NotSupportedException($"Resource type '{resourceType}' does not support filtering.");
				ImportTexturePageItems(data, inputDir);
				return true;
			default:
				return false;
		}
	}

	public static void ImportAssetOrder(GameMakerData data, string inputDir, CancellationToken cancellationToken)
	{
		using var cancellationScope = ModdingCancellation.Begin(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			ImportAssetOrder(data, inputDir);
			cancellationToken.ThrowIfCancellationRequested();
		}
		catch (Exception ex) when (ModdingCancellation.IsCancellation(ex))
		{
			ModdingCancellation.Rethrow(ex, cancellationToken);
			throw;
		}
	}

	public static void ImportAssetOrder(GameMakerData data, string inputDir)
	{
		ArgumentNullException.ThrowIfNull(data);
		ImportAssetOrderInternal(data, inputDir);
	}

	private static void Log(string msg)
	{
		LogService.Log(msg);
	}

	private static void ImportNamedJsonResources(string inputDir, string operation, Action<string, JsonElement> import)
	{
		var dirs = GetTopLevelDirs(inputDir);
		var files = GetFilesIn(inputDir, "*.json");
		List<(string Name, string Path)> entries = [];
		foreach (var dir in dirs)
		{
			var name = Path.GetFileName(dir);
			var jsonPath = Path.Combine(dir, name + ".json");
			if (FExists(jsonPath))
			{
				entries.Add((name, jsonPath));
			}
			else
			{
				BatchRun.Item(name, ResultKind.Skipped, "Missing json file");
			}
		}
		var known = new HashSet<string>(entries.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
		foreach (var file in files)
		{
			var name = Path.GetFileNameWithoutExtension(file);
			// A resource stored both as a folder and as a loose file is imported once, from its folder.
			if (!name.Equals("config", StringComparison.OrdinalIgnoreCase) && known.Add(name))
			{
				entries.Add((name, file));
			}
		}
		BatchRun.Expect(entries.Count);
		foreach (var entry in entries)
		{
			var name = entry.Name;
			try
			{
				using var jsonDoc = JsonDocument.Parse(FReadText(entry.Path));
				var root = jsonDoc.RootElement;
				if (root.TryGetProperty("name", out var nameElm) && nameElm.GetString() is { Length: > 0 } customName)
				{
					name = customName;
				}
				if (!MatchesOnlyName(name) && !MatchesOnlyName(entry.Name))
				{
					continue;
				}
				import(name, root);
				BatchRun.Item(name);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log($"[{operation}] Error: {name}: {ex.Message}");
				BatchRun.Failed(name, ex);
			}
		}
	}

	private static void ImportOptions(GameMakerData data, string inputDir)
	{
		if (data.Options == null)
		{
			BatchRun.Item("Options", ResultKind.Skipped, "Target data has no OPTN chunk");
			return;
		}
		var jsonPath = Path.Combine(inputDir, "options.json");
		if (!FExists(jsonPath))
		{
			BatchRun.Item("Options", ResultKind.Skipped, "File not found");
			return;
		}
		using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
		var root = jsonDoc.RootElement;
		var options = data.Options;
		if (root.TryGetProperty("newFormat", out var nf))
		{
			options.NewFormat = nf.GetBoolean();
		}
		if (root.TryGetProperty("shaderExtensionFlag", out var sef))
		{
			options.ShaderExtensionFlag = sef.GetInt32();
		}
		if (root.TryGetProperty("shaderExtensionVersion", out var sev))
		{
			options.ShaderExtensionVersion = sev.GetInt32();
		}
		if (root.TryGetProperty("info", out var info))
		{
			options.Info = (GameMakerOptions.OptionsFlags)info.GetUInt64();
		}
		if (root.TryGetProperty("scale", out var scale))
		{
			options.Scale = scale.GetInt32();
		}
		if (root.TryGetProperty("windowColor", out var wc))
		{
			options.WindowColor = wc.GetUInt32();
		}
		if (root.TryGetProperty("colorDepth", out var cd))
		{
			options.ColorDepth = cd.GetUInt32();
		}
		if (root.TryGetProperty("resolution", out var res))
		{
			options.Resolution = res.GetUInt32();
		}
		if (root.TryGetProperty("frequency", out var freq))
		{
			options.Frequency = freq.GetUInt32();
		}
		if (root.TryGetProperty("vertexSync", out var vs))
		{
			options.VertexSync = vs.GetUInt32();
		}
		if (root.TryGetProperty("priority", out var prio))
		{
			options.Priority = prio.GetUInt32();
		}
		if (root.TryGetProperty("loadAlpha", out var la))
		{
			options.LoadAlpha = la.GetUInt32();
		}
		if (!root.TryGetProperty("constants", out var constants) || constants.ValueKind != JsonValueKind.Array)
		{
			BatchRun.Item("Options");
			return;
		}
		options.Constants.Clear();
		foreach (var constantJson in constants.EnumerateArray())
		{
			var name = constantJson.TryGetProperty("name", out var nameEl) ? (nameEl.GetString() ?? "") : "";
			var value = constantJson.TryGetProperty("value", out var valueEl) ? (valueEl.GetString() ?? "") : "";
			options.Constants.Add(new GameMakerOptions.Constant
			{
				Name = data.Strings.MakeString(name),
				Value = data.Strings.MakeString(value)
			});
		}
		BatchRun.Item("Options");
	}

	private static void ImportGlobalScripts(GameMakerData data, string inputDir)
	{
		var jsonPath = Path.Combine(inputDir, "global_scripts.json");
		if (!FExists(jsonPath))
		{
			BatchRun.Item("GlobalScripts", ResultKind.Skipped, "File not found");
			return;
		}
		using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
		var root = jsonDoc.RootElement;
		if (data.GlobalInitScripts != null && root.TryGetProperty("globalInitScripts", out var globalInit))
		{
			ImportCodeRefList(data, data.GlobalInitScripts, globalInit);
		}
		if (data.GameEndScripts != null && root.TryGetProperty("gameEndScripts", out var gameEnd))
		{
			ImportCodeRefList(data, data.GameEndScripts, gameEnd);
		}
		BatchRun.Item("GlobalScripts");
	}

	private static void ImportCodeRefList(GameMakerData data, IList<GameMakerGlobalInit> target, JsonElement array)
	{
		if (array.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		target.Clear();
		foreach (var item2 in array.EnumerateArray())
		{
			var codeName = item2.GetString();
			if (!string.IsNullOrWhiteSpace(codeName))
			{
				var code = data.Code?.ByName(codeName);
				if (code == null)
				{
					Log("[ImportGlobalScripts] Missing code ref '" + codeName + "', skipped.");
					continue;
				}
				target.Add(new GameMakerGlobalInit
				{
					Code = code
				});
			}
		}
	}

	private static void ImportScripts(GameMakerData data, string inputDir)
	{
		if (data.Scripts == null)
		{
			BatchRun.Item("Scripts", ResultKind.Skipped, "Target data has no SCPT chunk");
			return;
		}
		var scriptByExportFolder = BuildScriptExportFolderMap(data);
		HashSet<GameMakerScript> usedScripts = [];
		var imported = 0;
		var created = 0;
		var dirs = GetTopLevelDirs(inputDir);
		var files = GetFilesIn(inputDir, "*.json");
		List<(string FolderName, string JsonPath)> scriptEntries = [];
		foreach (var dir in dirs)
		{
			var folderName = Path.GetFileName(dir);
			if (string.IsNullOrWhiteSpace(folderName))
			{
				BatchRun.Item(dir, ResultKind.Skipped, "Empty folder name");
				continue;
			}
			var jsonPath = Path.Combine(dir, folderName + ".json");
			if (!FExists(jsonPath))
			{
				BatchRun.Item(folderName, ResultKind.Skipped, "Missing json file");
				continue;
			}
			scriptEntries.Add((folderName, jsonPath));
		}
		foreach (var file in files)
		{
			var fileName = Path.GetFileNameWithoutExtension(file);
			if (!fileName.Equals("config", StringComparison.OrdinalIgnoreCase))
			{
				scriptEntries.Add((fileName, file));
			}
		}
		BatchRun.Expect(scriptEntries.Count);
		foreach (var (folderName, jsonPath) in scriptEntries)
		{
			try
			{
				using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
				var root = jsonDoc.RootElement;
				var name = root.TryGetProperty("name", out var nameEl) ? (nameEl.GetString() ?? folderName) : folderName;
				if (!MatchesOnlyName(name) && !MatchesOnlyName(folderName))
				{
					continue;
				}
				var codeName = root.TryGetProperty("code", out var codeEl) ? (codeEl.GetString() ?? "") : "";
				var isConstructor = root.TryGetProperty("isConstructor", out var ctorEl) && ctorEl.GetBoolean();
				var resolvedCode = PatchService.ScriptCodeResolver.Resolve(data, name, codeName);
				var script = FindUnusedScriptByCode(data, resolvedCode, usedScripts);
				if (script == null)
				{
					scriptByExportFolder.TryGetValue(folderName, out script);
				}
				if (script != null && usedScripts.Contains(script))
				{
					script = null;
				}
				script ??= FindUnusedScriptByName(data, name, usedScripts);
				if (script == null)
				{
					script = new GameMakerScript
					{
						Name = data.Strings.MakeString(name)
					};
					data.Scripts.Add(script);
					scriptByExportFolder[folderName] = script;
					created++;
				}
				usedScripts.Add(script);
				script.IsConstructor = isConstructor;
				script.Code = resolvedCode;
				if (script.Code == null && !string.IsNullOrWhiteSpace(codeName))
				{
					Log($"[ImportScripts] Missing code ref '{codeName}' for script '{name}'.");
				}
				imported++;
				BatchRun.Item(folderName);
			}
			catch (Exception itemEx) when (BatchRun.Active && !ModdingCancellation.IsCancellation(itemEx))
			{
				Log($"[ImportScripts] Error: {folderName}: {itemEx.Message}");
				BatchRun.Failed(folderName, itemEx);
			}
		}
		Log($"[ImportScripts] Done. {imported} imported ({created} new).");
	}

	private static GameMakerScript? FindUnusedScriptByCode(GameMakerData data, GameMakerCode? code, HashSet<GameMakerScript> usedScripts)
	{
		if (code == null)
		{
			return null;
		}
		foreach (var script in data.Scripts)
		{
			if (script != null && !usedScripts.Contains(script) && script.Code == code)
			{
				return script;
			}
		}
		return null;
	}

	private static GameMakerScript? FindUnusedScriptByName(GameMakerData data, string name, HashSet<GameMakerScript> usedScripts)
	{
		foreach (var script in data.Scripts)
		{
			if (script?.Name?.Content == name && !usedScripts.Contains(script))
			{
				return script;
			}
		}
		return null;
	}

	private static Dictionary<string, GameMakerScript> BuildScriptExportFolderMap(GameMakerData data)
	{
		var result = new Dictionary<string, GameMakerScript>(StringComparer.OrdinalIgnoreCase);
		if (data.Scripts == null || data.Scripts.Count == 0)
		{
			return result;
		}
		var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var script in data.Scripts)
		{
			var originalName = script?.Name?.Content;
			if (string.IsNullOrWhiteSpace(originalName) || script == null)
			{
				continue;
			}
			var safeName = SafeName(originalName);
			if (string.IsNullOrWhiteSpace(safeName))
			{
				safeName = "_";
			}
			string exportName;
			if (!used.TryGetValue(safeName, out var count))
			{
				used[safeName] = 1;
				exportName = safeName;
			}
			else
			{
				do
				{
					count++;
					exportName = $"{safeName}__{count}";
				}
				while (used.ContainsKey(exportName));
				used[safeName] = count;
				used[exportName] = 1;
			}
			result[exportName] = script;
		}
		return result;
	}

	private static void ImportEmbeddedImages(GameMakerData data, string inputDir)
	{
		if (data.EmbeddedImages == null)
		{
			BatchRun.Item("EmbeddedImages", ResultKind.Skipped, "Target data has no EMBI chunk");
			return;
		}
		var imported = 0;
		var created = 0;
		var dirs = GetTopLevelDirs(inputDir);
		BatchRun.Expect(dirs.Length);
		foreach (var dir in dirs)
		{
			var folderName = Path.GetFileName(dir);
			if (string.IsNullOrWhiteSpace(folderName))
			{
				BatchRun.Item(dir, ResultKind.Skipped, "Empty folder name");
				continue;
			}
			var jsonPath = Path.Combine(dir, folderName + ".json");
			if (!FExists(jsonPath))
			{
				BatchRun.Item(folderName, ResultKind.Skipped, "Missing json file");
				continue;
			}
			try
			{
				using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
				var root = jsonDoc.RootElement;
				var name = root.TryGetProperty("name", out var nameEl) ? (nameEl.GetString() ?? folderName) : folderName;
				var tpiIndex = root.TryGetProperty("texturePageItemIndex", out var indexEl) ? indexEl.GetInt32() : (-1);
				var tpiName = root.TryGetProperty("texturePageItemName", out var tpiNameEl) ? (tpiNameEl.GetString() ?? "") : "";
				var image = data.EmbeddedImages.ByName(name);
				if (image == null)
				{
					image = new GameMakerEmbeddedImage
					{
						Name = data.Strings.MakeString(name)
					};
					data.EmbeddedImages.Add(image);
					created++;
				}
				image.TextureEntry = ResolveTexturePageItem(data, tpiIndex, tpiName);
				imported++;
				BatchRun.Item(folderName);
			}
			catch (Exception itemEx) when (BatchRun.Active && !ModdingCancellation.IsCancellation(itemEx))
			{
				Log($"[ImportEmbeddedImages] Error: {folderName}: {itemEx.Message}");
				BatchRun.Failed(folderName, itemEx);
			}
		}
		Log($"[ImportEmbeddedImages] Done. {imported} imported ({created} new).");
	}

	private static GameMakerTexturePageItem? ResolveTexturePageItem(GameMakerData data, int index, string name)
	{
		if (data.TexturePageItems == null)
		{
			return null;
		}
		if (!string.IsNullOrWhiteSpace(name))
		{
			var byName = data.TexturePageItems.ByName(name);
			if (byName != null)
			{
				return byName;
			}
		}
		if (index >= 0 && index < data.TexturePageItems.Count)
		{
			return data.TexturePageItems[index];
		}
		return null;
	}

	private static void ImportFeatureFlags(GameMakerData data, string inputDir)
	{
		var jsonPath = Path.Combine(inputDir, "feature_flags.json");
		if (!FExists(jsonPath))
		{
			BatchRun.Item("FeatureFlags", ResultKind.Skipped, "File not found");
			return;
		}
		if (data.FeatureFlags?.List == null)
		{
			Log("[ImportFeatureFlags] Skipped: target data has no FEAT chunk.");
			BatchRun.Item("FeatureFlags", ResultKind.Skipped, "Target data has no FEAT chunk");
			return;
		}
		using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
		if (jsonDoc.RootElement.ValueKind != JsonValueKind.Array)
		{
			BatchRun.Item("FeatureFlags", ResultKind.Skipped, "Unexpected file format");
			return;
		}
		data.FeatureFlags.List.Clear();
		foreach (var item in jsonDoc.RootElement.EnumerateArray())
		{
			var flag = item.GetString();
			if (!string.IsNullOrEmpty(flag))
			{
				data.FeatureFlags.List.Add(data.Strings.MakeString(flag));
			}
		}
		Log($"[ImportFeatureFlags] Done. {data.FeatureFlags.List.Count} flags.");
		BatchRun.Item("FeatureFlags");
	}

	private static void ImportLanguage(GameMakerData data, string inputDir)
	{
		var jsonPath = Path.Combine(inputDir, "language.json");
		if (!FExists(jsonPath))
		{
			BatchRun.Item("Language", ResultKind.Skipped, "File not found");
			return;
		}
		if (data.Language == null)
		{
			Log("[ImportLanguage] Skipped: target data has no LANG chunk.");
			BatchRun.Item("Language", ResultKind.Skipped, "Target data has no LANG chunk");
			return;
		}
		using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
		var root = jsonDoc.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
		{
			BatchRun.Item("Language", ResultKind.Skipped, "Unexpected file format");
			return;
		}
		data.Language.Unknown1 = GetJsonValue(root, "unknown1", data.Language.Unknown1);
		data.Language.EntryIDs = [];
		if (root.TryGetProperty("entryIds", out var entryIdsElm) && entryIdsElm.ValueKind == JsonValueKind.Array)
		{
			foreach (var entryElm in entryIdsElm.EnumerateArray())
			{
				data.Language.EntryIDs.Add(data.Strings.MakeString(entryElm.GetString() ?? ""));
			}
		}
		data.Language.Languages = [];
		if (root.TryGetProperty("languages", out var languagesElm) && languagesElm.ValueKind == JsonValueKind.Array)
		{
			foreach (var languageElm in languagesElm.EnumerateArray())
			{
				if (languageElm.ValueKind != JsonValueKind.Object)
				{
					continue;
				}
				var language = new GameMakerLanguage.LanguageData
				{
					Name = data.Strings.MakeString(languageElm.TryGetProperty("name", out var nameElm) ? (nameElm.GetString() ?? "") : ""),
					Region = data.Strings.MakeString(languageElm.TryGetProperty("region", out var regionElm) ? (regionElm.GetString() ?? "") : ""),
					Entries = []
				};
				if (languageElm.TryGetProperty("entries", out var entriesElm) && entriesElm.ValueKind == JsonValueKind.Array)
				{
					foreach (var entryElm2 in entriesElm.EnumerateArray())
					{
						language.Entries.Add(data.Strings.MakeString(entryElm2.GetString() ?? ""));
					}
				}
				while (language.Entries.Count < data.Language.EntryIDs.Count)
				{
					language.Entries.Add(data.Strings.MakeString(""));
				}
				if (language.Entries.Count > data.Language.EntryIDs.Count)
				{
					language.Entries = [.. language.Entries.Take(data.Language.EntryIDs.Count)];
				}
				data.Language.Languages.Add(language);
			}
		}
		data.Language.EntryCount = (uint)data.Language.EntryIDs.Count;
		data.Language.LanguageCount = (uint)data.Language.Languages.Count;
		Log($"[ImportLanguage] Done. {data.Language.LanguageCount} languages, {data.Language.EntryCount} entries.");
		BatchRun.Item("Language");
	}

	private static void ImportTags(GameMakerData data, string inputDir)
	{
		var jsonPath = Path.Combine(inputDir, "tags.json");
		if (!FExists(jsonPath))
		{
			BatchRun.Item("Tags", ResultKind.Skipped, "File not found");
			return;
		}
		if (data.Tags == null)
		{
			Log("[ImportTags] Skipped: target data has no TAGS chunk.");
			BatchRun.Item("Tags", ResultKind.Skipped, "Target data has no TAGS chunk");
			return;
		}
		using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
		var root = jsonDoc.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
		{
			BatchRun.Item("Tags", ResultKind.Skipped, "Unexpected file format");
			return;
		}
		var tags = data.Tags;
		if (tags.Tags == null)
		{
			var gameMakerSimpleListString = tags.Tags = [];
		}
		data.Tags.Tags.Clear();
		if (root.TryGetProperty("tags", out var tagsElm) && tagsElm.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in tagsElm.EnumerateArray())
			{
				var tag = item.GetString();
				if (!string.IsNullOrEmpty(tag))
				{
					data.Tags.Tags.Add(data.Strings.MakeString(tag));
				}
			}
		}
		tags = data.Tags;
		if (tags.AssetTags == null)
		{
			var dictionary = tags.AssetTags = [];
		}
		data.Tags.AssetTags.Clear();
		if (root.TryGetProperty("assetTags", out var assetTagsElm) && assetTagsElm.ValueKind == JsonValueKind.Array)
		{
			foreach (var assetTagElm in assetTagsElm.EnumerateArray())
			{
				if (!TryResolveAssetTagId(data, assetTagElm, out var assetId))
				{
					Log("[ImportTags] Skipped asset tag entry: unresolved asset identity.");
					continue;
				}
				GameMakerSimpleListString list = [];
				if (assetTagElm.TryGetProperty("tags", out var itemTagsElm) && itemTagsElm.ValueKind == JsonValueKind.Array)
				{
					foreach (var item2 in itemTagsElm.EnumerateArray())
					{
						var tag2 = item2.GetString();
						if (!string.IsNullOrEmpty(tag2))
						{
							list.Add(data.Strings.MakeString(tag2));
						}
					}
				}
				data.Tags.AssetTags[assetId] = list;
			}
		}
		Log($"[ImportTags] Done. {data.Tags.Tags.Count} global tags, {data.Tags.AssetTags.Count} asset tag entries.");
		BatchRun.Item("Tags");
	}

	private static void ImportFilterEffects(GameMakerData data, string inputDir)
	{
		if (data.FilterEffects == null)
		{
			Log("[ImportFilterEffects] Skipped: target data has no FEDS chunk.");
			BatchRun.Item("FilterEffects", ResultKind.Skipped, "Target data has no FEDS chunk");
			return;
		}
		var created = 0;
		var updated = 0;
		ImportNamedJsonResources(inputDir, "ImportFilterEffects", delegate (string name, JsonElement root)
		{
			var gameMakerFilterEffect = data.FilterEffects.ByName(name);
			if (gameMakerFilterEffect == null)
			{
				gameMakerFilterEffect = new GameMakerFilterEffect
				{
					Name = data.Strings.MakeString(name)
				};
				data.FilterEffects.Add(gameMakerFilterEffect);
				created++;
			}
			else
			{
				updated++;
			}
			gameMakerFilterEffect.Value = data.Strings.MakeString(root.TryGetProperty("value", out var value) ? (value.GetString() ?? "") : "");
		});
		Log($"[ImportFilterEffects] Done. Created: {created}, Updated: {updated}");
	}

	private static bool TryResolveAssetTagId(GameMakerData data, JsonElement assetTagElm, out int assetId)
	{
		assetId = 0;
		var assetType = assetTagElm.TryGetProperty("assetType", out var typeElm) ? (typeElm.GetString() ?? "") : "";
		var assetName = assetTagElm.TryGetProperty("assetName", out var nameElm) ? (nameElm.GetString() ?? "") : "";
		if (!string.IsNullOrEmpty(assetType) && !string.IsNullOrEmpty(assetName) && TryEncodeAssetTagId(data, assetType, assetName, out assetId))
		{
			return true;
		}
		if (assetTagElm.TryGetProperty("assetId", out var assetIdElm))
		{
			var rawAssetId = assetIdElm.GetInt32();
			if (IsAssetTagIdValid(data, rawAssetId))
			{
				assetId = rawAssetId;
				return true;
			}
		}
		return false;
	}

	private static bool TryEncodeAssetTagId(GameMakerData data, string assetType, string assetName, out int assetId)
	{
		assetId = 0;
		if (!Enum.TryParse<ResourceType>(assetType, ignoreCase: true, out var type))
		{
			return false;
		}
		var index = GetAssetIndexByTagType(data, type, assetName);
		if (index < 0)
		{
			return false;
		}
		var storedIndex = (type == ResourceType.Script) ? (index + 100000) : index;
		assetId = ((int)type << 24) | (storedIndex & 0xFFFFFF);
		return true;
	}

	private static bool IsAssetTagIdValid(GameMakerData data, int assetId)
	{
		var type = (ResourceType)(assetId >> 24);
		var rawIndex = assetId & 0xFFFFFF;
		var index = (type == ResourceType.Script) ? (rawIndex - 100000) : rawIndex;
		return ResourceAssetUtil.GetAssetNameByTagType(data, type, index) != null;
	}

	private static int GetAssetIndexByTagType(GameMakerData data, ResourceType type, string assetName)
	{
		switch (type)
		{
			case ResourceType.Object:
				return data.GameObjects.IndexOfName(assetName);
			case ResourceType.Sprite:
				return data.Sprites.IndexOfName(assetName);
			case ResourceType.Sound:
				return data.Sounds.IndexOfName(assetName);
			case ResourceType.Room:
				return data.Rooms.IndexOfName(assetName);
			case ResourceType.Path:
				return data.Paths.IndexOfName(assetName);
			case ResourceType.Script:
				return data.Scripts.IndexOfName(assetName);
			case ResourceType.Font:
				return data.Fonts.IndexOfName(assetName);
			case ResourceType.Timeline:
				return data.Timelines.IndexOfName(assetName);
			case ResourceType.Background:
				return data.Backgrounds.IndexOfName(assetName);
			case ResourceType.Shader:
				return data.Shaders.IndexOfName(assetName);
			case ResourceType.Sequence:
				if (data.Sequences != null)
				{
					return data.Sequences.IndexOfName(assetName);
				}
				break;
			case ResourceType.AnimCurve:
				if (data.AnimationCurves != null)
				{
					return data.AnimationCurves.IndexOfName(assetName);
				}
				break;
			case ResourceType.ParticleSystem:
				if (data.ParticleSystems != null)
				{
					return data.ParticleSystems.IndexOfName(assetName);
				}
				break;
		}
		return -1;
	}

	private static T GetJsonValue<T>(JsonElement root, string propertyName, T defaultValue)
	{
		if (root.TryGetProperty(propertyName, out var elm))
		{
			try
			{
				if (typeof(T) == typeof(uint))
				{
					return (T)(object)(uint)elm.GetInt64();
				}
				if (typeof(T) == typeof(int))
				{
					return (T)(object)elm.GetInt32();
				}
				if (typeof(T) == typeof(long))
				{
					return (T)(object)elm.GetInt64();
				}
				if (typeof(T) == typeof(bool))
				{
					if (elm.ValueKind == JsonValueKind.True)
					{
						return (T)(object)true;
					}
					if (elm.ValueKind == JsonValueKind.False)
					{
						return (T)(object)false;
					}
					if (elm.ValueKind == JsonValueKind.Number)
					{
						return (T)(object)(elm.GetInt32() != 0);
					}
					return (T)(object)elm.GetBoolean();
				}
				if (typeof(T) == typeof(float))
				{
					return (T)(object)(float)elm.GetDouble();
				}
				if (typeof(T) == typeof(string))
				{
					return (T)(object)(elm.GetString() ?? "");
				}
			}
			catch
			{
			}
		}
		return defaultValue;
	}

	private static void ImportAudioGroups(GameMakerData data, string inputDir)
	{
		var dirs = GetTopLevelDirs(inputDir);
		if (dirs.Length == 0)
		{
			return;
		}
		Log($"[ImportAudioGroups] Found {dirs.Length} audio group(s) to import.");
		BatchRun.Expect(dirs.Length);
		var created = 0;
		var updated = 0;
		var array = dirs;
		foreach (var obj in array)
		{
			var name = Path.GetFileName(obj);
			var jsonFile = Path.Combine(obj, name + ".json");
			if (!FExists(jsonFile))
			{
				BatchRun.Item(name, ResultKind.Skipped, "Missing json file");
				continue;
			}
			try
			{
				using var jsonDoc = JsonDocument.Parse(FReadText(jsonFile));
				var root = jsonDoc.RootElement;
				var ag = data.AudioGroups?.ByName(name);
				var num = ag == null;
				if (num)
				{
					ag = new GameMakerAudioGroup
					{
						Name = data.Strings.MakeString(name)
					};
				}
				if (root.TryGetProperty("path", out var pathElm))
				{
					var path = pathElm.GetString() ?? "";
					if (!string.IsNullOrEmpty(path))
					{
						ag.Path = data.Strings.MakeString(path);
					}
				}
				if (num)
				{
					data.AudioGroups.Add(ag);
					created++;
				}
				else
				{
					updated++;
				}
				BatchRun.Item(name);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log("[ImportAudioGroups] Error: " + name + ": " + ex.Message);
				BatchRun.Failed(name, ex);
			}
		}
		Log($"[ImportAudioGroups] Done. Created: {created}, Updated: {updated}");
	}

	private static void ImportPaths(GameMakerData data, string inputDir)
	{
		var dirs = GetTopLevelDirs(inputDir);
		if (dirs.Length == 0)
		{
			return;
		}
		BatchRun.Expect(dirs.Length);
		var array = dirs;
		foreach (var obj in array)
		{
			var name = Path.GetFileName(obj);
			var jsonFile = Path.Combine(obj, name + ".json");
			if (!FExists(jsonFile))
			{
				BatchRun.Item(name, ResultKind.Skipped, "Missing json file");
				continue;
			}
			try
			{
				using var jsonDoc = JsonDocument.Parse(FReadText(jsonFile));
				var root = jsonDoc.RootElement;
				var path = data.Paths?.ByName(name);
				var isNew = path == null;
				if (isNew)
				{
					path = new GameMakerPath
					{
						Name = data.Strings.MakeString(name),
						Points = [],
						IsSmooth = false,
						IsClosed = false,
						Precision = 4u
					};
				}
				if (root.TryGetProperty("isSmooth", out var isSmoothElm))
				{
					path.IsSmooth = isSmoothElm.GetBoolean();
				}
				if (root.TryGetProperty("isClosed", out var isClosedElm))
				{
					path.IsClosed = isClosedElm.GetBoolean();
				}
				if (root.TryGetProperty("precision", out var precisionElm))
				{
					path.Precision = (uint)precisionElm.GetInt32();
				}
				if (root.TryGetProperty("points", out var pointsElm) && pointsElm.ValueKind == JsonValueKind.Array)
				{
					path.Points.Clear();
					foreach (var ptElm in pointsElm.EnumerateArray())
					{
						var pt = new GameMakerPath.PathPoint();
						if (ptElm.TryGetProperty("x", out var xElm))
						{
							pt.X = (float)xElm.GetDouble();
						}
						if (ptElm.TryGetProperty("y", out var yElm))
						{
							pt.Y = (float)yElm.GetDouble();
						}
						if (ptElm.TryGetProperty("speed", out var sElm))
						{
							pt.Speed = (float)sElm.GetDouble();
						}
						path.Points.Add(pt);
					}
				}
				if (isNew)
				{
					data.Paths.Add(path);
				}
				BatchRun.Item(name);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log("[ImportPaths] Error: " + name + ": " + ex.Message);
				BatchRun.Failed(name, ex);
			}
		}
	}

	private static void ImportTexturePageItems(GameMakerData data, string inputDir)
	{
		var jsonPath = Path.Combine(inputDir, "texture_page_items.json");
		if (!FExists(jsonPath))
		{
			BatchRun.Item("TexturePageItems", ResultKind.Skipped, "File not found");
			return;
		}
		using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
		if (jsonDoc.RootElement.ValueKind != JsonValueKind.Array)
		{
			BatchRun.Item("TexturePageItems", ResultKind.Skipped, "Unexpected file format");
			return;
		}
		List<JsonElement> items = [.. jsonDoc.RootElement.EnumerateArray()];
		var updated = 0;
		var created = 0;
		var itemIndex = 0;
		BatchRun.Expect(items.Count);
		foreach (var itemElm in items)
		{
			var itemLabel = "PageItem " + itemIndex++;
			try
			{
				if (!itemElm.TryGetProperty("isNull", out var nullElm) || !nullElm.GetBoolean())
				{
					var index = itemElm.TryGetProperty("index", out var idxElm) ? idxElm.GetInt32() : (-1);
					var name = itemElm.TryGetProperty("name", out var nameElm) ? (nameElm.GetString() ?? "") : "";
					var sourceX = (ushort)(itemElm.TryGetProperty("sourceX", out var sxElm) ? ((ushort)sxElm.GetInt32()) : 0);
					var sourceY = (ushort)(itemElm.TryGetProperty("sourceY", out var syElm) ? ((ushort)syElm.GetInt32()) : 0);
					var sourceWidth = (ushort)(itemElm.TryGetProperty("sourceWidth", out var swElm) ? ((ushort)swElm.GetInt32()) : 0);
					var sourceHeight = (ushort)(itemElm.TryGetProperty("sourceHeight", out var shElm) ? ((ushort)shElm.GetInt32()) : 0);
					var targetX = (ushort)(itemElm.TryGetProperty("targetX", out var txElm) ? ((ushort)txElm.GetInt32()) : 0);
					var targetY = (ushort)(itemElm.TryGetProperty("targetY", out var tyElm) ? ((ushort)tyElm.GetInt32()) : 0);
					var targetWidth = (ushort)(itemElm.TryGetProperty("targetWidth", out var twElm) ? ((ushort)twElm.GetInt32()) : 0);
					var targetHeight = (ushort)(itemElm.TryGetProperty("targetHeight", out var thElm) ? ((ushort)thElm.GetInt32()) : 0);
					var boundingWidth = (ushort)(itemElm.TryGetProperty("boundingWidth", out var bwElm) ? ((ushort)bwElm.GetInt32()) : 0);
					var boundingHeight = (ushort)(itemElm.TryGetProperty("boundingHeight", out var bhElm) ? ((ushort)bhElm.GetInt32()) : 0);
					var texturePageIndex = itemElm.TryGetProperty("texturePageIndex", out var tpElm) ? tpElm.GetInt32() : (-1);
					GameMakerTexturePageItem item;
					if (index >= 0 && index < data.TexturePageItems.Count)
					{
						item = data.TexturePageItems[index];
						updated++;
					}
					else
					{
						item = new GameMakerTexturePageItem
						{
							Name = new GameMakerString((name.Length > 0) ? name : $"PageItem {data.TexturePageItems.Count}")
						};
						data.TexturePageItems.Add(item);
						created++;
					}
					item.SourceX = sourceX;
					item.SourceY = sourceY;
					item.SourceWidth = sourceWidth;
					item.SourceHeight = sourceHeight;
					item.TargetX = targetX;
					item.TargetY = targetY;
					item.TargetWidth = targetWidth;
					item.TargetHeight = targetHeight;
					item.BoundingWidth = boundingWidth;
					item.BoundingHeight = boundingHeight;
					if (texturePageIndex >= 0 && texturePageIndex < data.EmbeddedTextures.Count)
					{
						item.TexturePage = data.EmbeddedTextures[texturePageIndex];
					}
					BatchRun.Item(name.Length > 0 ? name : itemLabel);
				}
				else
				{
					BatchRun.Item(itemLabel, ResultKind.Skipped, "Null entry");
				}
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log("[ImportTexturePageItems] Error: " + ex.Message);
				BatchRun.Failed(itemLabel, ex);
			}
		}
		for (var i = data.TexturePageItems.Count - 1; i >= items.Count; i--)
		{
			data.TexturePageItems.RemoveAt(i);
		}
		Log($"[ImportTexturePageItems] Done. {updated} updated, {created} created.");
	}

	private static GameMakerTexturePageItem AddTexturePageItem(GameMakerData data, MagickImage image)
	{
		var texture = new GameMakerEmbeddedTexture
		{
			Name = new GameMakerString($"Texture {data.EmbeddedTextures.Count}")
		};
		texture.TextureData.Image = GMImage.FromMagickImage(image).ConvertToPng();
		data.EmbeddedTextures.Add(texture);
		var item = new GameMakerTexturePageItem
		{
			Name = new GameMakerString($"PageItem {data.TexturePageItems.Count}"),
			SourceWidth = (ushort)image.Width,
			SourceHeight = (ushort)image.Height,
			TargetWidth = (ushort)image.Width,
			TargetHeight = (ushort)image.Height,
			BoundingWidth = (ushort)image.Width,
			BoundingHeight = (ushort)image.Height,
			TexturePage = texture
		};
		data.TexturePageItems.Add(item);
		return item;
	}

	private static void ImportBackgrounds(GameMakerData data, string inputDir)
	{
		var dirs = GetTopLevelDirs(inputDir);
		if (dirs.Length == 0)
		{
			return;
		}
		BatchRun.Expect(dirs.Length);
		var imported = 0;
		var created = 0;
		using (new TextureWorker())
		{
			var array = dirs;
			foreach (var obj in array)
			{
				var name = Path.GetFileName(obj);
				var pngPath = Path.Combine(obj, name + ".png");
				var jsonPath = Path.Combine(obj, name + ".json");
				if (!FExists(pngPath) && !FExists(jsonPath))
				{
					BatchRun.Item(name, ResultKind.Skipped, "Missing png and json files");
					continue;
				}
				try
				{
					var bg = data.Backgrounds.ByName(name);
					var isNew = bg == null;
					if (isNew)
					{
						bg = new GameMakerBackground
						{
							Name = data.Strings.MakeString(name),
							Transparent = false,
							Smooth = false,
							Preload = false
						};
						if (data.IsGameMaker2())
						{
							bg.GMS2TileWidth = 0u;
							bg.GMS2TileHeight = 0u;
							bg.GMS2TileColumns = 0u;
							bg.GMS2TileCount = 0u;
							bg.GMS2OutputBorderX = 0u;
							bg.GMS2OutputBorderY = 0u;
							bg.GMS2FrameLength = 0L;
						}
						created++;
					}
					if (FExists(pngPath))
					{
						using var img = new MagickImage(FReadBytes(pngPath));
						bg.Texture = AddTexturePageItem(data, img);
					}
					if (FExists(jsonPath))
					{
						using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
						var root = jsonDoc.RootElement;
						bg.Transparent = GetJsonValue(root, "transparent", bg.Transparent);
						bg.Smooth = GetJsonValue(root, "smooth", bg.Smooth);
						bg.Preload = GetJsonValue(root, "preload", bg.Preload);
						if (data.IsGameMaker2() && root.TryGetProperty("gms2UnknownAlways2", out var _))
						{
							bg.GMS2UnknownAlways2 = GetJsonValue(root, "gms2UnknownAlways2", bg.GMS2UnknownAlways2);
						}
					}
					if (isNew)
					{
						data.Backgrounds.Add(bg);
					}
					imported++;
					BatchRun.Item(name);
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					Log("[ImportBackgrounds] Failed: " + name + ": " + ex.Message);
					BatchRun.Failed(name, ex);
				}
			}
			Log($"[ImportBackgrounds] Done. {imported} processed ({created} new).");
		}
	}

	private static void ImportTimelines(GameMakerData data, string inputDir)
	{
		var created = 0;
		var updated = 0;
		ImportNamedJsonResources(inputDir, "ImportTimelines", delegate (string name, JsonElement root)
		{
			var tl = data.Timelines?.ByName(name);
			var isNew = tl == null;
			if (isNew)
			{
				tl = new GameMakerTimeline
				{
					Name = data.Strings.MakeString(name),
					Moments = []
				};
				created++;
			}
			else
			{
				updated++;
			}
			if (root.TryGetProperty("moments", out var momentsElm) && momentsElm.ValueKind == JsonValueKind.Array)
			{
				JsonElement[] arr = [.. momentsElm.EnumerateArray()];
				for (var mi = 0; mi < arr.Length; mi++)
				{
					var mElm = arr[mi];
					GameMakerTimeline.GameMakerTimelineMoment moment;
					if (mi < tl.Moments.Count)
					{
						moment = tl.Moments[mi];
					}
					else
					{
						moment = new GameMakerTimeline.GameMakerTimelineMoment
						{
							Event = []
						};
						tl.Moments.Add(moment);
					}
					if (mElm.TryGetProperty("step", out var stepElm))
					{
						moment.Step = (uint)stepElm.GetInt64();
					}
					if (!mElm.TryGetProperty("actions", out var actionsElm) || actionsElm.ValueKind != JsonValueKind.Array)
					{
						continue;
					}
					var gameMakerTimelineMoment = moment;
					if (gameMakerTimelineMoment.Event == null)
					{
						var gameMakerPointerList = gameMakerTimelineMoment.Event = [];
					}
					JsonElement[] actArr = [.. actionsElm.EnumerateArray()];
					for (var ai = 0; ai < actArr.Length; ai++)
					{
						var aElm = actArr[ai];
						GameMakerGameObject.EventAction action;
						if (ai < moment.Event.Count)
						{
							action = moment.Event[ai];
						}
						else
						{
							action = new GameMakerGameObject.EventAction();
							moment.Event.Add(action);
						}
						ApplyEventAction(data, action, aElm);
					}
					for (var ai2 = moment.Event.Count - 1; ai2 >= actArr.Length; ai2--)
					{
						moment.Event.RemoveAt(ai2);
					}
				}
				for (var mi2 = tl.Moments.Count - 1; mi2 >= arr.Length; mi2--)
				{
					tl.Moments.RemoveAt(mi2);
				}
			}
			if (isNew)
			{
				data.Timelines.Add(tl);
			}
		});
		Log($"[ImportTimelines] Done. Created: {created}, Updated: {updated}");
	}

	private static void ImportAnimationCurves(GameMakerData data, string inputDir)
	{
		if (data.AnimationCurves == null)
		{
			Log("[ImportAnimationCurves] Skipped: target data has no ACRV chunk.");
			BatchRun.Item("AnimationCurves", ResultKind.Skipped, "Target data has no ACRV chunk");
			return;
		}
		var created = 0;
		var updated = 0;
		ImportNamedJsonResources(inputDir, "ImportAnimationCurves", delegate (string name, JsonElement root)
		{
			var gameMakerAnimationCurve = data.AnimationCurves.ByName(name);
			if (gameMakerAnimationCurve == null)
			{
				gameMakerAnimationCurve = new GameMakerAnimationCurve
				{
					Name = data.Strings.MakeString(name),
					Channels = []
				};
				data.AnimationCurves.Add(gameMakerAnimationCurve);
				created++;
			}
			else
			{
				updated++;
			}
			gameMakerAnimationCurve.GraphType = (GameMakerAnimationCurve.GraphTypeEnum)GetJsonValue(root, "graphType", (int)gameMakerAnimationCurve.GraphType);
			var gameMakerAnimationCurve2 = gameMakerAnimationCurve;
			if (gameMakerAnimationCurve2.Channels == null)
			{
				var gameMakerSimpleList = gameMakerAnimationCurve2.Channels = [];
			}
			gameMakerAnimationCurve.Channels.Clear();
			if (root.TryGetProperty("channels", out var value) && value.ValueKind == JsonValueKind.Array)
			{
				foreach (var current in value.EnumerateArray())
				{
					if (current.ValueKind == JsonValueKind.Object)
					{
						var channel = new GameMakerAnimationCurve.Channel
						{
							Name = data.Strings.MakeString(current.TryGetProperty("name", out var value2) ? (value2.GetString() ?? "") : ""),
							Curve = (GameMakerAnimationCurve.Channel.CurveType)GetJsonValue(current, "curve", 0),
							Iterations = GetJsonValue(current, "iterations", 0u),
							Points = []
						};
						if (current.TryGetProperty("points", out var value3) && value3.ValueKind == JsonValueKind.Array)
						{
							foreach (var current2 in value3.EnumerateArray())
							{
								if (current2.ValueKind == JsonValueKind.Object)
								{
									channel.Points.Add(new GameMakerAnimationCurve.Channel.Point
									{
										X = GetJsonValue(current2, "x", 0f),
										Value = GetJsonValue(current2, "value", 0f),
										BezierX0 = GetJsonValue(current2, "bezierX0", 0f),
										BezierY0 = GetJsonValue(current2, "bezierY0", 0f),
										BezierX1 = GetJsonValue(current2, "bezierX1", 0f),
										BezierY1 = GetJsonValue(current2, "bezierY1", 0f)
									});
								}
							}
						}
						gameMakerAnimationCurve.Channels.Add(channel);
					}
				}
			}
		});
		Log($"[ImportAnimationCurves] Done. Created: {created}, Updated: {updated}");
	}

	private static void ImportSequences(GameMakerData data, string inputDir)
	{
		if (data.Sequences == null)
		{
			Log("[ImportSequences] Skipped: target data has no SEQN chunk.");
			BatchRun.Item("Sequences", ResultKind.Skipped, "Target data has no SEQN chunk");
			return;
		}
		var dirs = GetTopLevelDirs(inputDir);
		if (dirs.Length == 0)
		{
			return;
		}
		BatchRun.Expect(dirs.Length);
		List<string> files = [];
		var array = dirs;
		foreach (var obj in array)
		{
			var name = Path.GetFileName(obj);
			var jsonFile = Path.Combine(obj, name + ".json");
			if (FExists(jsonFile))
			{
				files.Add(jsonFile);
			}
			else
			{
				BatchRun.Item(name, ResultKind.Skipped, "Missing json file");
			}
		}
		if (files.Count == 0)
		{
			return;
		}
		var tempRoot = Path.Combine(TempRoot.Path, "g3mlib_seq_import", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempRoot);
		try
		{
			var localFiles = new List<string>(files.Count);
			foreach (var source in files)
			{
				var local = Path.Combine(tempRoot, Path.GetFileName(Path.GetDirectoryName(source)) + ".json");
				File.WriteAllText(local, FReadText(source), Encoding.UTF8);
				localFiles.Add(local);
			}
			SerializableProjectAssetBridge.ImportMany(ResourceAssetUtil.CreateProjectContext(data, Path.Combine(tempRoot, ".project_context")), localFiles);
			Log($"[ImportSequences] Done. Imported: {localFiles.Count}");
			foreach (var file in files)
			{
				BatchRun.Item(Path.GetFileName(Path.GetDirectoryName(file)));
			}
		}
		catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
		{
			Log("[ImportSequences] Error: " + ex.Message);
			foreach (var file in files)
			{
				BatchRun.Failed(Path.GetFileName(Path.GetDirectoryName(file)), ex);
			}
		}
		finally
		{
			try
			{
				Directory.Delete(tempRoot, recursive: true);
			}
			catch
			{
			}
		}
	}

	private static void ImportParticleSystems(GameMakerData data, string inputDir)
	{
		if (data.ParticleSystems == null)
		{
			Log("[ImportParticleSystems] Skipped: target data has no PSYS chunk.");
			BatchRun.Item("ParticleSystems", ResultKind.Skipped, "Target data has no PSYS chunk");
			return;
		}
		var dirs = GetTopLevelDirs(inputDir);
		BatchRun.Expect(dirs.Length);
		foreach (var obj in dirs)
		{
			var name = Path.GetFileName(obj);
			var jsonFile = Path.Combine(obj, name + ".json");
			if (!FExists(jsonFile))
			{
				BatchRun.Item(name, ResultKind.Skipped, "Missing json file");
				continue;
			}
			try
			{
				using var jsonDoc = JsonDocument.Parse(FReadText(jsonFile));
				var root = jsonDoc.RootElement;
				if (root.TryGetProperty("name", out var nameElm))
				{
					name = nameElm.GetString() ?? name;
				}
				var ps = data.ParticleSystems.ByName(name);
				if (ps == null)
				{
					ps = new GameMakerParticleSystem
					{
						Name = data.Strings.MakeString(name)
					};
					data.ParticleSystems.Add(ps);
				}
				ps.OriginX = GetJsonValue(root, "originX", ps.OriginX);
				ps.OriginY = GetJsonValue(root, "originY", ps.OriginY);
				ps.DrawOrder = (GameMakerParticleSystem.DrawOrderEnum)GetJsonValue(root, "drawOrder", (int)ps.DrawOrder);
				ps.GlobalSpaceParticles = GetJsonValue(root, "globalSpaceParticles", ps.GlobalSpaceParticles);
				ps.Emitters = [];
				if (!root.TryGetProperty("emitters", out var emittersElm) || emittersElm.ValueKind != JsonValueKind.Array)
				{
					BatchRun.Item(name);
					continue;
				}
				foreach (var item in emittersElm.EnumerateArray())
				{
					var emitterName = item.GetString();
					var emitter = (string.IsNullOrEmpty(emitterName) || data.ParticleSystemEmitters == null) ? null : FindEmitter(data, emitterName);
					if (emitter != null)
					{
						ps.Emitters.Add(new GameMakerResourceById<GameMakerParticleSystemEmitter, GameMakerChunkPSEM>(emitter));
					}
				}
				BatchRun.Item(name);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log("[ImportParticleSystems] Error: " + name + ": " + ex.Message);
				BatchRun.Failed(name, ex);
			}
		}
	}

	private static void ImportParticleSystemEmitters(GameMakerData data, string inputDir)
	{
		if (data.ParticleSystemEmitters == null)
		{
			Log("[ImportParticleSystemEmitters] Skipped: target data has no PSEM chunk.");
			BatchRun.Item("ParticleSystemEmitters", ResultKind.Skipped, "Target data has no PSEM chunk");
			return;
		}
		List<(GameMakerParticleSystemEmitter, string, string, string)> pendingRefs = [];
		var dirs = GetTopLevelDirs(inputDir);
		BatchRun.Expect(dirs.Length);
		foreach (var obj in dirs)
		{
			var name = Path.GetFileName(obj);
			var jsonFile = Path.Combine(obj, name + ".json");
			if (!FExists(jsonFile))
			{
				BatchRun.Item(name, ResultKind.Skipped, "Missing json file");
				continue;
			}
			try
			{
				using var jsonDoc = JsonDocument.Parse(FReadText(jsonFile));
				var root = jsonDoc.RootElement;
				if (root.TryGetProperty("name", out var nameElm))
				{
					name = nameElm.GetString() ?? name;
				}
				var emitter = FindEmitter(data, root.TryGetProperty("key", out var keyElm) ? (keyElm.GetString() ?? name) : name);
				if (emitter == null)
				{
					emitter = new GameMakerParticleSystemEmitter
					{
						Name = data.Strings.MakeString(name)
					};
					data.ParticleSystemEmitters.Add(emitter);
				}
				ApplyParticleEmitterScalars(emitter, root);
				var sprite = root.TryGetProperty("sprite", out var spriteElm) ? (spriteElm.GetString() ?? "") : "";
				var sod = root.TryGetProperty("spawnOnDeath", out var sodElm) ? (sodElm.GetString() ?? "") : "";
				var sou = root.TryGetProperty("spawnOnUpdate", out var souElm) ? (souElm.GetString() ?? "") : "";
				pendingRefs.Add((emitter, sprite, sod, sou));
				BatchRun.Item(name);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log("[ImportParticleSystemEmitters] Error: " + name + ": " + ex.Message);
				BatchRun.Failed(name, ex);
			}
		}
		foreach (var (emitter2, sprite2, spawnOnDeath, spawnOnUpdate) in pendingRefs)
		{
			if (!string.IsNullOrEmpty(sprite2))
			{
				emitter2.Sprite = data.Sprites.ByName(sprite2);
			}
			if (!string.IsNullOrEmpty(spawnOnDeath))
			{
				emitter2.SpawnOnDeath = FindEmitter(data, spawnOnDeath);
			}
			if (!string.IsNullOrEmpty(spawnOnUpdate))
			{
				emitter2.SpawnOnUpdate = FindEmitter(data, spawnOnUpdate);
			}
		}
	}

	// Resolves an emitter key: "name", or "name__idxNNNN" for one of several emitters sharing a name.
	private static GameMakerParticleSystemEmitter FindEmitter(GameMakerData data, string key)
	{
		var marker = key.LastIndexOf("__idx", StringComparison.Ordinal);
		if (marker > 0 && int.TryParse(key.AsSpan(marker + 5), out var index) && index >= 0 && index < data.ParticleSystemEmitters.Count
			&& data.ParticleSystemEmitters[index]?.Name?.Content == key[..marker])
		{
			return data.ParticleSystemEmitters[index];
		}
		return data.ParticleSystemEmitters.ByName(key);
	}

	private static void ApplyParticleEmitterScalars(GameMakerParticleSystemEmitter emitter, JsonElement root)
	{
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
			if (!flag && root.TryGetProperty(prop.Name, out var value))
			{
				var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
				if (type.IsEnum)
				{
					prop.SetValue(emitter, Enum.ToObject(type, value.GetInt32()));
				}
				else if (type == typeof(bool))
				{
					prop.SetValue(emitter, value.GetBoolean());
				}
				else if (type == typeof(int))
				{
					prop.SetValue(emitter, value.GetInt32());
				}
				else if (type == typeof(uint))
				{
					prop.SetValue(emitter, (uint)value.GetInt64());
				}
				else if (type == typeof(float))
				{
					prop.SetValue(emitter, (float)value.GetDouble());
				}
			}
		}
	}

	private static void ApplyEventAction(GameMakerData data, GameMakerGameObject.EventAction action, JsonElement elm)
	{
		if (elm.TryGetProperty("libId", out var libIdElm))
		{
			action.LibID = (uint)libIdElm.GetInt64();
		}
		if (elm.TryGetProperty("id", out var idElm))
		{
			action.ID = (uint)idElm.GetInt64();
		}
		if (elm.TryGetProperty("kind", out var kindElm))
		{
			action.Kind = (uint)kindElm.GetInt64();
		}
		if (elm.TryGetProperty("useRelative", out var useRelativeElm))
		{
			action.UseRelative = useRelativeElm.GetBoolean();
		}
		if (elm.TryGetProperty("isQuestion", out var isQuestionElm))
		{
			action.IsQuestion = isQuestionElm.GetBoolean();
		}
		if (elm.TryGetProperty("useApplyTo", out var useApplyToElm))
		{
			action.UseApplyTo = useApplyToElm.GetBoolean();
		}
		if (elm.TryGetProperty("exeType", out var exeTypeElm))
		{
			action.ExeType = (uint)exeTypeElm.GetInt64();
		}
		if (elm.TryGetProperty("actionName", out var actionNameElm))
		{
			var actionName = actionNameElm.GetString() ?? "";
			action.ActionName = (!string.IsNullOrEmpty(actionName)) ? data.Strings.MakeString(actionName) : null;
		}
		if (elm.TryGetProperty("codeId", out var codeIdElm))
		{
			if (codeIdElm.ValueKind == JsonValueKind.String)
			{
				var codeName = codeIdElm.GetString() ?? "";
				if (!string.IsNullOrEmpty(codeName))
				{
					action.CodeId = data.Code.ByName(codeName);
				}
			}
			else if (codeIdElm.ValueKind == JsonValueKind.Null)
			{
				action.CodeId = null;
			}
		}
		if (elm.TryGetProperty("argumentCount", out var argumentCountElm))
		{
			action.ArgumentCount = (uint)argumentCountElm.GetInt64();
		}
		if (elm.TryGetProperty("who", out var whoElm))
		{
			action.Who = whoElm.GetInt32();
		}
		if (elm.TryGetProperty("relative", out var relativeElm))
		{
			action.Relative = relativeElm.GetBoolean();
		}
		if (elm.TryGetProperty("isNot", out var isNotElm))
		{
			action.IsNot = isNotElm.GetBoolean();
		}
	}

	private static void ImportShaders(GameMakerData data, string inputDir)
	{
		var dirs = GetTopLevelDirs(inputDir);
		BatchRun.Expect(dirs.Length);
		var imported = 0;
		var updated = 0;
		var array = dirs;
		foreach (var dir in array)
		{
			var name = Path.GetFileName(dir);
			try
			{
				var num = data.Shaders.ByName(name) != null;
				ImportShaderSingle(data, dir);
				if (num)
				{
					updated++;
				}
				else
				{
					imported++;
				}
				BatchRun.Item(name);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log("[ImportShaders] Error: " + name + ": " + ex.Message);
				BatchRun.Failed(name, ex);
			}
		}
		Log($"[ImportShaders] Done. {imported} new, {updated} updated.");
	}

	private static void ImportShaderSingle(GameMakerData data, string shaderDir)
	{
		var name = Path.GetFileName(shaderDir);
		if (string.IsNullOrEmpty(name))
		{
			return;
		}
		var shader = data.Shaders.ByName(name);
		if (shader == null)
		{
			shader = new GameMakerShader
			{
				Name = new GameMakerString(name)
			};
			data.Strings.Add(shader.Name);
			data.Shaders.Add(shader);
		}
		var typeFile = Path.Combine(shaderDir, "Type.txt");
		if (FExists(typeFile))
		{
			var typeStr = FReadText(typeFile);
			if (!string.IsNullOrEmpty(typeStr) && Enum.TryParse<GameMakerShader.ShaderType>(typeStr, out var st))
			{
				shader.Type = st;
			}
		}
		string[] array = ["GLSL_ES_Fragment.txt", "GLSL_ES_Vertex.txt", "GLSL_Fragment.txt", "GLSL_Vertex.txt", "HLSL9_Fragment.txt", "HLSL9_Vertex.txt"];
		foreach (var fileName in array)
		{
			var fp = Path.Combine(shaderDir, fileName);
			if (!FExists(fp))
			{
				continue;
			}
			try
			{
				var code = FReadText(fp);
				GameMakerString? shaderString = null;
				switch (fileName)
				{
					case "GLSL_ES_Fragment.txt":
						shader.GLSL_ES_Fragment = data.Strings.MakeString(code);
						shaderString = shader.GLSL_ES_Fragment;
						break;
					case "GLSL_ES_Vertex.txt":
						shader.GLSL_ES_Vertex = data.Strings.MakeString(code);
						shaderString = shader.GLSL_ES_Vertex;
						break;
					case "GLSL_Fragment.txt":
						shader.GLSL_Fragment = data.Strings.MakeString(code);
						shaderString = shader.GLSL_Fragment;
						break;
					case "GLSL_Vertex.txt":
						shader.GLSL_Vertex = data.Strings.MakeString(code);
						shaderString = shader.GLSL_Vertex;
						break;
					case "HLSL9_Fragment.txt":
						shader.HLSL9_Fragment = data.Strings.MakeString(code);
						shaderString = shader.HLSL9_Fragment;
						break;
					case "HLSL9_Vertex.txt":
						shader.HLSL9_Vertex = data.Strings.MakeString(code);
						shaderString = shader.HLSL9_Vertex;
						break;
				}
				if (shaderString != null && !data.Strings.Any(s => s == shaderString))
				{
					data.Strings.Add(shaderString);
				}
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(name + "/" + fileName, ex);
			}
		}
		array = ["HLSL11_VertexData.bin", "HLSL11_PixelData.bin", "PSSL_VertexData.bin", "PSSL_PixelData.bin", "Cg_PSVita_VertexData.bin", "Cg_PSVita_PixelData.bin", "Cg_PS3_VertexData.bin", "Cg_PS3_PixelData.bin"];
		foreach (var fileName2 in array)
		{
			var fp2 = Path.Combine(shaderDir, fileName2);
			if (!FExists(fp2))
			{
				continue;
			}
			try
			{
				var bytes = FReadBytes(fp2);
				if (bytes.Length == 0 || fileName2 == null)
				{
					continue;
				}
				switch (fileName2.Length)
				{
					case 21:
						switch (fileName2[0])
						{
							case 'H':
								if (fileName2 == "HLSL11_VertexData.bin")
								{
									var gameMakerShader = shader;
									var obj7 = gameMakerShader.HLSL11_VertexData ??= new GameMakerShader.GameMakerRawShaderData();
									obj7.Data = bytes;
									obj7.IsNull = false;
								}
								break;
							case 'C':
								if (fileName2 == "Cg_PS3_VertexData.bin")
								{
									var gameMakerShader = shader;
									var obj6 = gameMakerShader.Cg_PS3_VertexData ??= new GameMakerShader.GameMakerRawShaderData();
									obj6.Data = bytes;
									obj6.IsNull = false;
								}
								break;
						}
						break;
					case 20:
						switch (fileName2[0])
						{
							case 'H':
								if (fileName2 == "HLSL11_PixelData.bin")
								{
									var gameMakerShader = shader;
									var obj4 = gameMakerShader.HLSL11_PixelData ??= new GameMakerShader.GameMakerRawShaderData();
									obj4.Data = bytes;
									obj4.IsNull = false;
								}
								break;
							case 'C':
								if (fileName2 == "Cg_PS3_PixelData.bin")
								{
									var gameMakerShader = shader;
									var obj3 = gameMakerShader.Cg_PS3_PixelData ??= new GameMakerShader.GameMakerRawShaderData();
									obj3.Data = bytes;
									obj3.IsNull = false;
								}
								break;
						}
						break;
					case 19:
						if (fileName2 == "PSSL_VertexData.bin")
						{
							var gameMakerShader = shader;
							var obj9 = gameMakerShader.PSSL_VertexData ??= new GameMakerShader.GameMakerRawShaderData();
							obj9.Data = bytes;
							obj9.IsNull = false;
						}
						break;
					case 18:
						if (fileName2 == "PSSL_PixelData.bin")
						{
							var gameMakerShader = shader;
							var obj5 = gameMakerShader.PSSL_PixelData ??= new GameMakerShader.GameMakerRawShaderData();
							obj5.Data = bytes;
							obj5.IsNull = false;
						}
						break;
					case 24:
						if (fileName2 == "Cg_PSVita_VertexData.bin")
						{
							var gameMakerShader = shader;
							var obj8 = gameMakerShader.Cg_PSVita_VertexData ??= new GameMakerShader.GameMakerRawShaderData();
							obj8.Data = bytes;
							obj8.IsNull = false;
						}
						break;
					case 23:
						if (fileName2 == "Cg_PSVita_PixelData.bin")
						{
							var gameMakerShader = shader;
							var obj2 = gameMakerShader.Cg_PSVita_PixelData ??= new GameMakerShader.GameMakerRawShaderData();
							obj2.Data = bytes;
							obj2.IsNull = false;
						}
						break;
					case 22:
						break;
				}
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				BatchRun.Failed(name + "/" + fileName2, ex);
			}
		}
		var attrsFile = Path.Combine(shaderDir, "VertexShaderAttributes.txt");
		if (!FExists(attrsFile))
		{
			return;
		}
		try
		{
			var text = FReadText(attrsFile);
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			var gameMakerShader = shader;
			if (gameMakerShader.VertexShaderAttributes == null)
			{
				var gameMakerSimpleList = gameMakerShader.VertexShaderAttributes = [];
			}
			shader.VertexShaderAttributes.Clear();
			array = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
			foreach (var line in array)
			{
				if (!string.IsNullOrWhiteSpace(line))
				{
					var attrName = data.Strings.MakeString(line.Trim());
					shader.VertexShaderAttributes.Add(new GameMakerShader.VertexShaderAttribute
					{
						Name = attrName
					});
				}
			}
		}
		catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
		{
			BatchRun.Failed(name + "/VertexShaderAttributes.txt", ex);
		}
	}

	private static void ImportGeneralInfo(GameMakerData data, string inputDir)
	{
		if (data.GeneralInfo == null)
		{
			BatchRun.Item("GeneralInfo", ResultKind.Skipped, "Target data has no GEN8 chunk");
			return;
		}
		var jsonPath = Path.Combine(inputDir, "GeneralInfo.json");
		if (!FExists(jsonPath))
		{
			jsonPath = Path.Combine(inputDir, "GeneralInfo", "GeneralInfo.json");
		}
		if (!FExists(jsonPath))
		{
			BatchRun.Item("GeneralInfo", ResultKind.Skipped, "File not found");
			return;
		}
		try
		{
			using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
			var root = jsonDoc.RootElement;
			if (root.TryGetProperty("isDebuggerDisabled", out var dbgElm))
			{
				data.GeneralInfo.IsDebuggerDisabled = dbgElm.GetBoolean();
			}
			if (root.TryGetProperty("bytecodeVersion", out var bcElm))
			{
				data.GeneralInfo.BytecodeVersion = (byte)bcElm.GetInt32();
			}
			if (root.TryGetProperty("padding", out var padElm))
			{
				data.GeneralInfo.Padding = (ushort)padElm.GetInt32();
			}
			if (root.TryGetProperty("fileName", out var fnElm))
			{
				var fn = fnElm.GetString() ?? "";
				if (fn.Length > 0)
				{
					data.GeneralInfo.FileName = data.Strings.MakeString(fn);
				}
			}
			if (root.TryGetProperty("config", out var cfgElm))
			{
				var cfg = cfgElm.GetString() ?? "";
				if (cfg.Length > 0)
				{
					data.GeneralInfo.Config = data.Strings.MakeString(cfg);
				}
			}
			if (root.TryGetProperty("lastObj", out var loElm))
			{
				data.GeneralInfo.LastObj = (uint)loElm.GetInt64();
			}
			if (root.TryGetProperty("lastTile", out var ltElm))
			{
				data.GeneralInfo.LastTile = (uint)ltElm.GetInt64();
			}
			if (root.TryGetProperty("gameID", out var giElm))
			{
				data.GeneralInfo.GameID = (uint)giElm.GetInt64();
			}
			if (root.TryGetProperty("directPlayGuid", out var dpElm) && Guid.TryParse(dpElm.GetString() ?? "", out var g))
			{
				data.GeneralInfo.DirectPlayGuid = g;
			}
			if (root.TryGetProperty("name", out var nmElm))
			{
				var nm = nmElm.GetString() ?? "";
				if (nm.Length > 0)
				{
					data.GeneralInfo.Name = data.Strings.MakeString(nm);
				}
			}
			if (root.TryGetProperty("major", out var majElm))
			{
				data.GeneralInfo.Major = (uint)majElm.GetInt64();
			}
			if (root.TryGetProperty("minor", out var minElm))
			{
				data.GeneralInfo.Minor = (uint)minElm.GetInt64();
			}
			if (root.TryGetProperty("release", out var relElm))
			{
				data.GeneralInfo.Release = (uint)relElm.GetInt64();
			}
			if (root.TryGetProperty("build", out var bldElm))
			{
				data.GeneralInfo.Build = (uint)bldElm.GetInt64();
			}
			if (root.TryGetProperty("branch", out var brElm) && brElm.ValueKind == JsonValueKind.String && Enum.TryParse<GameMakerGeneralInfo.BranchType>(brElm.GetString(), out var branch))
			{
				data.GeneralInfo.Branch = branch;
			}
			if (root.TryGetProperty("defaultWindowWidth", out var wwElm))
			{
				data.GeneralInfo.DefaultWindowWidth = (uint)wwElm.GetInt64();
			}
			if (root.TryGetProperty("defaultWindowHeight", out var whElm))
			{
				data.GeneralInfo.DefaultWindowHeight = (uint)whElm.GetInt64();
			}
			if (root.TryGetProperty("infoFlags", out var ifElm))
			{
				data.GeneralInfo.Info = (GameMakerGeneralInfo.InfoFlags)ifElm.GetUInt32();
			}
			if (root.TryGetProperty("licenseCRC32", out var lcElm))
			{
				data.GeneralInfo.LicenseCRC32 = (uint)lcElm.GetInt64();
			}
			if (root.TryGetProperty("licenseMD5", out var lmElm) && lmElm.ValueKind == JsonValueKind.Array)
			{
				data.GeneralInfo.LicenseMD5 = [.. from e in lmElm.EnumerateArray()
											   select (byte)e.GetInt32()];
			}
			if (root.TryGetProperty("timestamp", out var tsElm))
			{
				data.GeneralInfo.Timestamp = tsElm.GetUInt64();
			}
			if (root.TryGetProperty("displayName", out var dnElm))
			{
				var dn = dnElm.GetString() ?? "";
				if (dn.Length > 0)
				{
					data.GeneralInfo.DisplayName = data.Strings.MakeString(dn);
				}
			}
			if (root.TryGetProperty("activeTargets", out var atElm))
			{
				data.GeneralInfo.ActiveTargets = atElm.GetUInt64();
			}
			if (root.TryGetProperty("functionClassifications", out var fcElm))
			{
				data.GeneralInfo.FunctionClassifications = (GameMakerGeneralInfo.FunctionClassification)fcElm.GetUInt64();
			}
			if (root.TryGetProperty("steamAppID", out var saElm))
			{
				data.GeneralInfo.SteamAppID = saElm.GetInt32();
			}
			if (data.GeneralInfo.BytecodeVersion >= 14 && root.TryGetProperty("debuggerPort", out var dprtElm))
			{
				data.GeneralInfo.DebuggerPort = (uint)dprtElm.GetInt64();
			}
			if (root.TryGetProperty("roomOrder", out var roElm) && roElm.ValueKind == JsonValueKind.Array)
			{
				data.GeneralInfo.RoomOrder.Clear();
				foreach (var rElm in roElm.EnumerateArray())
				{
					if (rElm.ValueKind == JsonValueKind.Null)
					{
						continue;
					}
					var rn = rElm.GetString() ?? "";
					if (!string.IsNullOrEmpty(rn))
					{
						var room = data.Rooms.ByName(rn);
						if (room != null)
						{
							data.GeneralInfo.RoomOrder.Add(new GameMakerResourceById<GameMakerRoom, GameMakerChunkROOM>
							{
								Resource = room
							});
						}
					}
				}
			}
			if (data.GeneralInfo.Major >= 2)
			{
				if (root.TryGetProperty("gms2RandomUID", out var uidElm) && uidElm.ValueKind == JsonValueKind.Array)
				{
					data.GeneralInfo.GMS2RandomUID = [.. from e in uidElm.EnumerateArray()
													  select e.GetInt64()];
				}
				if (root.TryGetProperty("gms2FPS", out var fpsElm))
				{
					data.GeneralInfo.GMS2FPS = (float)fpsElm.GetDouble();
				}
				if (root.TryGetProperty("gms2AllowStatistics", out var asElm))
				{
					data.GeneralInfo.GMS2AllowStatistics = asElm.GetBoolean();
				}
				if (root.TryGetProperty("gms2GameGUID", out var ggElm) && ggElm.ValueKind == JsonValueKind.Array)
				{
					data.GeneralInfo.GMS2GameGUID = [.. from e in ggElm.EnumerateArray()
													 select (byte)e.GetInt32()];
				}
			}
			Log("[ImportGeneralInfo] Done. Game: " + (data.GeneralInfo.DisplayName?.Content ?? "N/A"));
			BatchRun.Item("GeneralInfo");
		}
		catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
		{
			Log("[ImportGeneralInfo] Failed: " + ex.Message);
			throw;
		}
	}

	private static void ImportTextureGroupInfo(GameMakerData data, string inputDir)
	{
		if (data.TextureGroupInfo == null)
		{
			BatchRun.Item("TextureGroupInfo", ResultKind.Skipped, "Target data has no TGIN chunk");
			return;
		}
		var dirs = GetTopLevelDirs(inputDir);
		if (dirs.Length == 0)
		{
			return;
		}
		BatchRun.Expect(dirs.Length);
		var array = dirs;
		foreach (var dir in array)
		{
			var name = Path.GetFileName(dir);
			var jsonFile = Path.Combine(dir, name + ".json");
			if (!FExists(jsonFile))
			{
				BatchRun.Item(name, ResultKind.Skipped, "Missing json file");
				continue;
			}
			try
			{
				using var jsonDoc = JsonDocument.Parse(FReadText(jsonFile));
				var root = jsonDoc.RootElement;
				var tg = data.TextureGroupInfo.FirstOrDefault(t => t.Name?.Content == name);
				if (tg == null)
				{
					tg = new GameMakerTextureGroupInfo
					{
						Name = data.Strings.MakeString(name)
					};
					data.TextureGroupInfo.Add(tg);
				}
				if (root.TryGetProperty("name", out var nmElm) && nmElm.ValueKind == JsonValueKind.String)
				{
					tg.Name = data.Strings.MakeString(nmElm.GetString());
				}
				if (data.IsVersionAtLeast(2022u, 9u))
				{
					if (root.TryGetProperty("directory", out var dirElm) && dirElm.ValueKind == JsonValueKind.String)
					{
						tg.Directory = data.Strings.MakeString(dirElm.GetString());
					}
					if (root.TryGetProperty("extension", out var extElm) && extElm.ValueKind == JsonValueKind.String)
					{
						tg.Extension = data.Strings.MakeString(extElm.GetString());
					}
					if (root.TryGetProperty("loadType", out var ltElm) && ltElm.ValueKind == JsonValueKind.Number)
					{
						tg.LoadType = (GameMakerTextureGroupInfo.TextureGroupLoadType)ltElm.GetInt32();
					}
				}
				if (root.TryGetProperty("texturePages", out var tpElm) && tpElm.ValueKind == JsonValueKind.Array)
				{
					tg.TexturePages.Clear();
					foreach (var e in tpElm.EnumerateArray())
					{
						if (e.ValueKind != JsonValueKind.String)
						{
							continue;
						}
						var tpName = e.GetString() ?? "";
						if (!string.IsNullOrEmpty(tpName))
						{
							var tp = data.EmbeddedTextures.FirstOrDefault(t => t.Name?.Content == tpName);
							if (tp != null)
							{
								tg.TexturePages.Add(new GameMakerResourceById<GameMakerEmbeddedTexture, GameMakerChunkTXTR>(tp));
							}
						}
					}
				}
				if (root.TryGetProperty("sprites", out var spElm) && spElm.ValueKind == JsonValueKind.Array)
				{
					tg.Sprites.Clear();
					foreach (var e2 in spElm.EnumerateArray())
					{
						if (e2.ValueKind == JsonValueKind.String)
						{
							var sn = e2.GetString() ?? "";
							var spr = data.Sprites.ByName(sn);
							if (spr != null)
							{
								tg.Sprites.Add(new GameMakerResourceById<GameMakerSprite, GameMakerChunkSPRT>(spr));
							}
						}
					}
				}
				if (!data.IsNonLTSVersionAtLeast(2023u, 1u) && root.TryGetProperty("spineSprites", out var ssElm) && ssElm.ValueKind == JsonValueKind.Array)
				{
					tg.SpineSprites.Clear();
					foreach (var e3 in ssElm.EnumerateArray())
					{
						if (e3.ValueKind == JsonValueKind.String)
						{
							var sn2 = e3.GetString() ?? "";
							var spr2 = data.Sprites.ByName(sn2);
							if (spr2 != null)
							{
								tg.SpineSprites.Add(new GameMakerResourceById<GameMakerSprite, GameMakerChunkSPRT>(spr2));
							}
						}
					}
				}
				if (root.TryGetProperty("fonts", out var fnElm) && fnElm.ValueKind == JsonValueKind.Array)
				{
					tg.Fonts.Clear();
					foreach (var e4 in fnElm.EnumerateArray())
					{
						if (e4.ValueKind == JsonValueKind.String)
						{
							var fn = e4.GetString() ?? "";
							var font = data.Fonts.ByName(fn);
							if (font != null)
							{
								tg.Fonts.Add(new GameMakerResourceById<GameMakerFont, GameMakerChunkFONT>(font));
							}
						}
					}
				}
				if (!root.TryGetProperty("tilesets", out var tsElm) || tsElm.ValueKind != JsonValueKind.Array)
				{
					BatchRun.Item(name);
					continue;
				}
				tg.Tilesets.Clear();
				foreach (var e5 in tsElm.EnumerateArray())
				{
					if (e5.ValueKind == JsonValueKind.String)
					{
						var tn = e5.GetString() ?? "";
						var tileset = data.Backgrounds.ByName(tn);
						if (tileset != null)
						{
							tg.Tilesets.Add(new GameMakerResourceById<GameMakerBackground, GameMakerChunkBGND>(tileset));
						}
					}
				}
				BatchRun.Item(name);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log("[ImportTextureGroupInfo] Error: " + name + ": " + ex.Message);
				BatchRun.Failed(name, ex);
			}
		}
		Log("[ImportTextureGroupInfo] Done.");
	}

	private static void ImportFonts(GameMakerData data, string inputDir)
	{
		var dirs = GetTopLevelDirs(inputDir);
		if (dirs.Length == 0)
		{
			return;
		}
		BatchRun.Expect(dirs.Length);
		var imported = 0;
		var created = 0;
		var skipped = 0;
		var array = dirs;
		foreach (var obj in array)
		{
			var safeName = Path.GetFileName(obj);
			var jsonPath = Path.Combine(obj, "font.json");
			var pngPath = Path.Combine(obj, "texture.png");
			if (!FExists(jsonPath))
			{
				skipped++;
				BatchRun.Item(safeName, ResultKind.Skipped, "Missing font.json");
				continue;
			}
			var fontName = safeName;
			try
			{
				using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
				var root = jsonDoc.RootElement;
				if (root.TryGetProperty("name", out var nmElm))
				{
					fontName = nmElm.GetString() ?? safeName;
				}
				var font = data.Fonts.ByName(fontName);
				var isNew = font == null;
				if (isNew)
				{
					font = new GameMakerFont
					{
						Name = data.Strings.MakeString(fontName),
						DisplayName = data.Strings.MakeString(fontName),
						Glyphs = [],
						EmSize = 12f,
						Bold = false,
						Italic = false,
						RangeStart = 32,
						RangeEnd = 127u,
						Charset = 1,
						AntiAliasing = 1,
						ScaleX = 1f,
						ScaleY = 1f
					};
					created++;
				}
				if (FExists(pngPath) && !FontTextureUnchanged(font, pngPath))
				{
					using var img = new MagickImage(FReadBytes(pngPath));
					var newTex = new GameMakerEmbeddedTexture
					{
						Name = new GameMakerString($"Texture {data.EmbeddedTextures.Count}")
					};
					newTex.TextureData.Image = GMImage.FromMagickImage(img).ConvertToPng();
					data.EmbeddedTextures.Add(newTex);
					var origTX = font.Texture?.TargetX ?? 0;
					var origTY = font.Texture?.TargetY ?? 0;
					var origBW = font.Texture?.BoundingWidth ?? ((ushort)img.Width);
					var origBH = font.Texture?.BoundingHeight ?? ((ushort)img.Height);
					var newTPI = new GameMakerTexturePageItem
					{
						Name = new GameMakerString($"PageItem {data.TexturePageItems.Count}"),
						SourceX = 0,
						SourceY = 0,
						SourceWidth = (ushort)img.Width,
						SourceHeight = (ushort)img.Height,
						TargetX = origTX,
						TargetY = origTY,
						TargetWidth = (ushort)img.Width,
						TargetHeight = (ushort)img.Height,
						BoundingWidth = origBW,
						BoundingHeight = origBH,
						TexturePage = newTex
					};
					data.TexturePageItems.Add(newTPI);
					font.Texture = newTPI;
				}
				if (root.TryGetProperty("displayName", out var dnElm))
				{
					var dn = dnElm.GetString() ?? "";
					if (dn.Length > 0)
					{
						font.DisplayName = data.Strings.MakeString(dn);
					}
				}
				if (root.TryGetProperty("emSize", out var esElm))
				{
					font.EmSize = (float)esElm.GetDouble();
				}
				if (root.TryGetProperty("emSizeIsFloat", out var esfElm))
				{
					font.EmSizeIsFloat = esfElm.GetBoolean();
				}
				if (root.TryGetProperty("bold", out var bElm))
				{
					font.Bold = bElm.GetBoolean();
				}
				if (root.TryGetProperty("italic", out var iElm))
				{
					font.Italic = iElm.GetBoolean();
				}
				if (root.TryGetProperty("rangeStart", out var rsElm))
				{
					font.RangeStart = (ushort)rsElm.GetInt32();
				}
				if (root.TryGetProperty("rangeEnd", out var reElm))
				{
					font.RangeEnd = (uint)reElm.GetInt64();
				}
				if (root.TryGetProperty("charset", out var csElm))
				{
					font.Charset = (byte)csElm.GetInt32();
				}
				if (root.TryGetProperty("antiAliasing", out var aaElm))
				{
					font.AntiAliasing = (byte)aaElm.GetInt32();
				}
				if (root.TryGetProperty("scaleX", out var sxElm))
				{
					font.ScaleX = (float)sxElm.GetDouble();
				}
				if (root.TryGetProperty("scaleY", out var syElm))
				{
					font.ScaleY = (float)syElm.GetDouble();
				}
				if (data.GeneralInfo?.BytecodeVersion >= 17 && root.TryGetProperty("ascenderOffset", out var aoElm))
				{
					font.AscenderOffset = aoElm.GetInt32();
				}
				if (data.IsVersionAtLeast(2022u, 2u) && root.TryGetProperty("ascender", out var ascElm))
				{
					font.Ascender = (uint)ascElm.GetInt64();
				}
				if (data.IsVersionAtLeast(2023u, 2u) && root.TryGetProperty("sdfSpread", out var sdfElm))
				{
					font.SDFSpread = (uint)sdfElm.GetInt64();
				}
				if (data.IsVersionAtLeast(2023u, 6u) && root.TryGetProperty("lineHeight", out var lhElm))
				{
					font.LineHeight = (uint)lhElm.GetInt64();
				}
				if (root.TryGetProperty("glyphs", out var gElm) && gElm.ValueKind == JsonValueKind.Array)
				{
					font.Glyphs.Clear();
					foreach (var ge in gElm.EnumerateArray())
					{
						var glyph = new GameMakerFont.Glyph();
						if (ge.TryGetProperty("character", out var cElm2))
						{
							glyph.Character = (ushort)cElm2.GetInt32();
						}
						if (ge.TryGetProperty("sourceX", out var sx2))
						{
							glyph.SourceX = (ushort)sx2.GetInt32();
						}
						if (ge.TryGetProperty("sourceY", out var sy2))
						{
							glyph.SourceY = (ushort)sy2.GetInt32();
						}
						if (ge.TryGetProperty("sourceWidth", out var sw2))
						{
							glyph.SourceWidth = (ushort)sw2.GetInt32();
						}
						if (ge.TryGetProperty("sourceHeight", out var sh2))
						{
							glyph.SourceHeight = (ushort)sh2.GetInt32();
						}
						if (ge.TryGetProperty("shift", out var shElm))
						{
							glyph.Shift = (short)shElm.GetInt32();
						}
						if (ge.TryGetProperty("offset", out var ofElm))
						{
							glyph.Offset = (short)ofElm.GetInt32();
						}
						if (ge.TryGetProperty("kerning", out var kElm) && kElm.ValueKind == JsonValueKind.Array)
						{
							glyph.Kerning = [];
							foreach (var ke in kElm.EnumerateArray())
							{
								var kern = new GameMakerFont.Glyph.GlyphKerning();
								if (ke.TryGetProperty("character", out var kc))
								{
									kern.Character = (short)kc.GetInt32();
								}
								if (ke.TryGetProperty("shiftModifier", out var ks))
								{
									kern.ShiftModifier = (short)ks.GetInt32();
								}
								glyph.Kerning.Add(kern);
							}
						}
						font.Glyphs.Add(glyph);
					}
				}
				if (isNew)
				{
					data.Fonts.Add(font);
				}
				imported++;
				BatchRun.Item(fontName);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log("[ImportFonts] Failed: " + fontName + ": " + ex.Message);
				BatchRun.Failed(fontName, ex);
				skipped++;
			}
		}
		Log($"[ImportFonts] Done. {imported} imported ({created} new), {skipped} skipped.");
	}

	private static void ImportSounds(GameMakerData data, string inputDir)
	{
		List<string> soundDirs = [.. GetTopLevelDirs(inputDir).Select(Path.GetFileName)];
		if (soundDirs.Count == 0)
		{
			return;
		}
		BatchRun.Expect(soundDirs.Count);
		var imported = 0;
		var created = 0;
		var skipped = 0;
		var metadataApplied = 0;
		using var groupBatch = new SoundGroupBatch();
		foreach (var soundName in soundDirs)
		{
			if (soundName == null)
			{
				BatchRun.Item("(unnamed)", ResultKind.Skipped, "Empty folder name");
				continue;
			}
			var path = Path.Combine(inputDir, soundName);
			var oggFile = Path.Combine(path, soundName + ".ogg");
			var wavFile = Path.Combine(path, soundName + ".wav");
			var metaFile = Path.Combine(path, soundName + ".json");
			string? audioFile = null;
			var isOGG = false;
			if (FExists(oggFile))
			{
				audioFile = oggFile;
				isOGG = true;
			}
			else if (FExists(wavFile))
			{
				audioFile = wavFile;
			}
			if (audioFile == null && !FExists(metaFile))
			{
				skipped++;
				BatchRun.Item(soundName, ResultKind.Skipped, "No audio or metadata file");
				continue;
			}
			if (audioFile == null && FExists(metaFile))
			{
				var existing = data.Sounds.ByName(soundName);
				if (existing == null)
				{
					var tuple = ReadSoundAudioPlacement(metaFile);
					var (metadataAudioId, _) = tuple;
					if (tuple.GroupId > 0 || metadataAudioId < 0)
					{
						skipped++;
						BatchRun.Item(soundName, ResultKind.Skipped, "Audio is stored outside the main data file");
						continue;
					}
					existing = new GameMakerSound
					{
						Name = data.Strings.MakeString(soundName),
						File = data.Strings.MakeString(soundName),
						Type = data.Strings.MakeString(".ogg"),
						Volume = 1f,
						Pitch = 1f,
						Preload = true,
						Flags = GameMakerSound.AudioEntryFlags.Regular | GameMakerSound.AudioEntryFlags.IsEmbedded,
						AudioID = metadataAudioId,
						AudioFile = EnsureEmbeddedAudioSlot(data, metadataAudioId)
					};
					data.Sounds.Add(existing);
					created++;
				}
				try
				{
					ApplySoundMetadata(data, existing, metaFile);
					metadataApplied++;
					imported++;
					BatchRun.Item(soundName);
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					Log("[ImportSounds] Metadata error: " + soundName + ": " + ex.Message);
					BatchRun.Failed(soundName, ex);
				}
				continue;
			}
			try
			{
				var audioData = FReadBytes(audioFile);
				if (audioData.Length == 0)
				{
					skipped++;
					BatchRun.Item(soundName, ResultKind.Skipped, "Empty audio file");
					continue;
				}
				(var AudioId, var GroupId) = ReadSoundAudioPlacement(metaFile);
				var metadataAudioId2 = AudioId;
				var metadataGroupId = GroupId;
				var sound = data.Sounds.ByName(soundName);
				var isNew = sound == null;
				if (isNew)
				{
					sound = new GameMakerSound
					{
						Name = data.Strings.MakeString(soundName),
						File = data.Strings.MakeString(soundName + (isOGG ? ".ogg" : ".wav")),
						Type = data.Strings.MakeString(isOGG ? ".ogg" : ".wav"),
						Volume = 1f,
						Pitch = 1f,
						Preload = true,
						Flags = GameMakerSound.AudioEntryFlags.Regular | GameMakerSound.AudioEntryFlags.IsEmbedded
					};
					if (isOGG)
					{
						sound.Flags |= GameMakerSound.AudioEntryFlags.IsCompressed;
					}
					if (metadataGroupId <= 0 && metadataAudioId2 >= 0)
					{
						sound.AudioID = metadataAudioId2;
						sound.AudioFile = EnsureEmbeddedAudioSlot(data, metadataAudioId2);
					}
					else
					{
						sound.AudioFile = new GameMakerEmbeddedAudio();
						data.EmbeddedAudio.Add(sound.AudioFile);
						sound.AudioID = data.EmbeddedAudio.Count - 1;
					}
					sound.AudioFile.Data = audioData;
					var audioGroups = data.AudioGroups;
					if (audioGroups != null && audioGroups.Count > 0)
					{
						sound.AudioGroup = data.AudioGroups[0];
						sound.GroupID = 0;
					}
					created++;
				}
				else
				{
					if (metadataGroupId <= 0 && metadataAudioId2 >= 0)
					{
						sound.AudioID = metadataAudioId2;
						sound.AudioFile = EnsureEmbeddedAudioSlot(data, metadataAudioId2);
					}
					else if (sound.AudioFile == null)
					{
						sound.AudioFile = new GameMakerEmbeddedAudio();
						data.EmbeddedAudio.Add(sound.AudioFile);
						sound.AudioID = data.EmbeddedAudio.Count - 1;
					}
					sound.AudioFile.Data = audioData;
					sound.Flags |= GameMakerSound.AudioEntryFlags.Regular | GameMakerSound.AudioEntryFlags.IsEmbedded;
					if (isOGG)
					{
						sound.Flags |= GameMakerSound.AudioEntryFlags.IsCompressed;
					}
					else
					{
						sound.Flags &= ~GameMakerSound.AudioEntryFlags.IsCompressed;
					}
				}
				string? metadataError = null;
				if (FExists(metaFile))
				{
					try
					{
						ApplySoundMetadata(data, sound, metaFile);
						metadataApplied++;
					}
					catch (Exception metaEx) when (!ModdingCancellation.IsCancellation(metaEx))
					{
						metadataError = "Metadata not applied: " + metaEx.Message;
					}
				}
				if (groupBatch.TryQueue(data, sound, audioData))
				{
					sound.AudioFile = null;
					sound.Flags |= GameMakerSound.AudioEntryFlags.Regular;
				}
				if (isNew)
				{
					data.Sounds.Add(sound);
				}
				imported++;
				BatchRun.Item(soundName, metadataError == null ? ResultKind.Written : ResultKind.Warning, metadataError);
			}
			catch (Exception ex2) when (!ModdingCancellation.IsCancellation(ex2))
			{
				Log("[ImportSounds] Failed: " + soundName + ": " + ex2.Message);
				BatchRun.Failed(soundName, ex2);
				skipped++;
			}
		}
		groupBatch.Flush(data);
		Log($"[ImportSounds] Done. {imported} imported ({created} new, {metadataApplied} with metadata), {skipped} skipped.");
	}

	// Re-adding an identical glyph texture would append a new page and change the file for nothing.
	private static bool FontTextureUnchanged(GameMakerFont font, string pngPath)
	{
		if (font.Texture == null)
		{
			return false;
		}
		try
		{
			using var worker = new TextureWorker();
			using var current = worker.GetTextureFor(font.Texture, font.Name?.Content);
			using var proposed = new MagickImage(FReadBytes(pngPath));
			return current.Width == proposed.Width && current.Height == proposed.Height
				&& current.ToByteArray(MagickFormat.Rgba).AsSpan().SequenceEqual(proposed.ToByteArray(MagickFormat.Rgba));
		}
		catch
		{
			return false;
		}
	}

	private static GameMakerEmbeddedAudio EnsureEmbeddedAudioSlot(GameMakerData data, int audioId)
	{
		while (data.EmbeddedAudio.Count <= audioId)
		{
			data.EmbeddedAudio.Add(new GameMakerEmbeddedAudio());
		}
		return data.EmbeddedAudio[audioId];
	}

	private static void ImportEmbeddedAudio(GameMakerData data, string inputDir)
	{
		var audioDirs = GetTopLevelDirs(inputDir);
		if (audioDirs.Length == 0)
		{
			return;
		}
		BatchRun.Expect(audioDirs.Length);
		var imported = 0;
		var array = audioDirs;
		foreach (var audioDir in array)
		{
			var folderName = Path.GetFileName(audioDir.TrimEnd(
			[
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar
			]));
			var jsonPath = Path.Combine(audioDir, folderName + ".json");
			var binPath = Path.Combine(audioDir, folderName + ".bin");
			if (!FExists(jsonPath) || !FExists(binPath))
			{
				BatchRun.Item(folderName, ResultKind.Skipped, "Missing json or bin file");
				continue;
			}
			try
			{
				using var doc = JsonDocument.Parse(FReadText(jsonPath));
				var index = (doc.RootElement.TryGetProperty("index", out var idxElm) && idxElm.ValueKind == JsonValueKind.Number) ? idxElm.GetInt32() : data.EmbeddedAudio.Count;
				while (data.EmbeddedAudio.Count <= index)
				{
					data.EmbeddedAudio.Add(new GameMakerEmbeddedAudio());
				}
				data.EmbeddedAudio[index].Data = FReadBytes(binPath);
				imported++;
				BatchRun.Item(folderName);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log("[ImportEmbeddedAudio] Failed: " + folderName + ": " + ex.Message);
				BatchRun.Failed(folderName, ex);
			}
		}
		Log($"[ImportEmbeddedAudio] Imported {imported} embedded audio slot(s).");
	}

	private static (int AudioId, int GroupId) ReadSoundAudioPlacement(string metaFile)
	{
		if (!FExists(metaFile))
		{
			return (AudioId: -1, GroupId: -1);
		}
		try
		{
			using var jsonDoc = JsonDocument.Parse(FReadText(metaFile));
			var root = jsonDoc.RootElement;
			var item = root.TryGetProperty("audioID", out var audioIdElm) ? audioIdElm.GetInt32() : (-1);
			var groupId = root.TryGetProperty("groupID", out var groupIdElm) ? groupIdElm.GetInt32() : (-1);
			return (AudioId: item, GroupId: groupId);
		}
		catch
		{
			return (AudioId: -1, GroupId: -1);
		}
	}

	// A missing string is exported as "", so an empty value must not turn a missing string into an empty one.
	private static GameMakerString KeepOrMakeString(GameMakerData data, GameMakerString current, string content)
	{
		content ??= "";
		return (current?.Content ?? "") == content ? current : data.Strings.MakeString(content);
	}

	private static void ApplySoundMetadata(GameMakerData data, GameMakerSound sound, string metaFile)
	{
		using var jsonDoc = JsonDocument.Parse(FReadText(metaFile));
		var root = jsonDoc.RootElement;
		if (root.TryGetProperty("file", out var fileElm))
		{
			sound.File = KeepOrMakeString(data, sound.File, fileElm.GetString());
		}
		if (root.TryGetProperty("type", out var typeElm))
		{
			sound.Type = KeepOrMakeString(data, sound.Type, typeElm.GetString());
		}
		if (root.TryGetProperty("volume", out var vElm))
		{
			sound.Volume = (float)vElm.GetDouble();
		}
		if (root.TryGetProperty("pitch", out var pElm))
		{
			sound.Pitch = (float)pElm.GetDouble();
		}
		if (root.TryGetProperty("preload", out var prElm))
		{
			sound.Preload = prElm.GetBoolean();
		}
		if (root.TryGetProperty("effects", out var eElm))
		{
			sound.Effects = (uint)eElm.GetInt32();
		}
		if (root.TryGetProperty("flags", out var fElm))
		{
			sound.Flags = (GameMakerSound.AudioEntryFlags)fElm.GetInt32();
		}
		if (root.TryGetProperty("audioID", out var audioIdElm))
		{
			sound.AudioID = audioIdElm.GetInt32();
		}
		if (root.TryGetProperty("groupID", out var groupIdElm))
		{
			sound.GroupID = groupIdElm.GetInt32();
			if (data.AudioGroups != null && sound.GroupID >= 0 && sound.GroupID < data.AudioGroups.Count)
			{
				sound.AudioGroup = data.AudioGroups[sound.GroupID];
			}
		}
		if (root.TryGetProperty("audioGroupName", out var agElm))
		{
			var agName = agElm.GetString() ?? "";
			if (!string.IsNullOrEmpty(agName) && data.AudioGroups != null)
			{
				var ag = data.AudioGroups.ByName(agName);
				if (ag != null)
				{
					sound.AudioGroup = ag;
					sound.GroupID = data.AudioGroups.IndexOf(ag);
				}
			}
		}
		if (root.TryGetProperty("audioLength", out var alElm) && data.IsVersionAtLeast(2024u, 6u))
		{
			sound.AudioLength = (float)alElm.GetDouble();
		}
		if (sound.GroupID <= 0 && sound.AudioID >= 0)
		{
			sound.AudioFile = EnsureEmbeddedAudioSlot(data, sound.AudioID);
		}
		sound.Flags |= GameMakerSound.AudioEntryFlags.Regular;
	}

	// Audio group files are opened once per import and written once at the end, not once per sound.
	private sealed class SoundGroupBatch : IDisposable
	{
		private sealed class Group
		{
			public AudioGroupFile? File;
			public string SavePath = "";
			public string? Error;
			public List<(GameMakerSound Sound, byte[] Data)> Sounds = [];
		}

		private readonly Dictionary<int, Group> _groups = [];

		public bool TryQueue(GameMakerData data, GameMakerSound sound, byte[] audioData)
		{
			if (sound.GroupID <= 0 || string.IsNullOrWhiteSpace(_loadedDataPath) || string.IsNullOrWhiteSpace(_savingDataPath))
			{
				return false;
			}
			if (!_groups.TryGetValue(sound.GroupID, out var group))
			{
				group = Open(data, sound.GroupID);
				_groups[sound.GroupID] = group;
			}
			if (group == null)
			{
				return false;
			}
			if (group.File == null)
			{
				Log("[ImportSounds] External audiogroup write skipped for " + sound.Name?.Content + ": " + group.Error);
				return false;
			}
			if (sound.AudioID < 0 || sound.AudioID >= group.File.Count)
			{
				sound.AudioID = group.File.Add(audioData);
			}
			else
			{
				group.File.Replace(sound.AudioID, audioData);
			}
			group.Sounds.Add((sound, audioData));
			if (sound.AudioFile != null)
			{
				var embeddedAudio = data.EmbeddedAudio;
				if (embeddedAudio != null && embeddedAudio.Contains(sound.AudioFile))
				{
					data.EmbeddedAudio.Remove(sound.AudioFile);
				}
			}
			return true;
		}

		private static Group? Open(GameMakerData data, int groupId)
		{
			var loadPath = ResourceAssetUtil.ResolveAudioGroupPathFromDataFile(data, _loadedDataPath, groupId);
			var savePath = ResourceAssetUtil.ResolveAudioGroupPathFromDataFile(data, _savingDataPath, groupId);
			if (loadPath == null || savePath == null)
			{
				return null;
			}
			var sourcePath = File.Exists(savePath) ? savePath : loadPath;
			if (!File.Exists(sourcePath))
			{
				return null;
			}
			var group = new Group { SavePath = savePath };
			try
			{
				group.File = AudioGroupFile.Open(sourcePath);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				group.Error = ex.Message;
			}
			return group;
		}

		/// <summary>Writes every touched group file. When a write fails, its sounds keep their audio embedded in the data file.</summary>
		public void Flush(GameMakerData data)
		{
			foreach (var group in _groups.Values)
			{
				if (group?.File == null || group.Sounds.Count == 0)
				{
					continue;
				}
				try
				{
					var outDir = Path.GetDirectoryName(group.SavePath);
					if (!string.IsNullOrWhiteSpace(outDir))
					{
						Directory.CreateDirectory(outDir);
					}
					group.File.Save(group.SavePath, backup: false);
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					foreach ((var sound, var audio) in group.Sounds)
					{
						Log("[ImportSounds] External audiogroup write skipped for " + sound.Name?.Content + ": " + ex.Message);
						sound.AudioFile = new GameMakerEmbeddedAudio { Data = audio };
						data.EmbeddedAudio.Add(sound.AudioFile);
					}
				}
			}
		}

		public void Dispose()
		{
			foreach (var group in _groups.Values)
			{
				group?.File?.Dispose();
			}
		}
	}

	private static void ImportTilesets(GameMakerData data, string inputDir)
	{
		if (!data.IsGameMaker2())
		{
			BatchRun.Item("Tilesets", ResultKind.Skipped, "Target data is not GameMaker 2");
			return;
		}
		HashSet<string> names = [];
		var filesIn = GetFilesIn(inputDir, "*.png");
		foreach (var f in filesIn)
		{
			var pngName = Path.GetFileNameWithoutExtension(f);
			if (MatchesOnlyName(pngName))
			{
				names.Add(pngName);
			}
		}
		filesIn = GetFilesIn(inputDir, "*.json");
		for (var i = 0; i < filesIn.Length; i++)
		{
			var n = Path.GetFileNameWithoutExtension(filesIn[i]);
			if (!n.Equals("config", StringComparison.OrdinalIgnoreCase) && MatchesOnlyName(n))
			{
				names.Add(n);
			}
		}
		if (names.Count == 0)
		{
			return;
		}
		BatchRun.Expect(names.Count);
		var imported = 0;
		var created = 0;
		using (new TextureWorker())
		{
			foreach (var tsName in names)
			{
				var pngPath = Path.Combine(inputDir, tsName + ".png");
				var jsonPath = Path.Combine(inputDir, tsName + ".json");
				if (!FExists(pngPath) && !FExists(jsonPath))
				{
					BatchRun.Item(tsName, ResultKind.Skipped, "Missing png and json files");
					continue;
				}
				try
				{
					var ts = data.Backgrounds.ByName(tsName);
					var isNew = ts == null;
					if (isNew)
					{
						ts = new GameMakerBackground
						{
							Name = data.Strings.MakeString(tsName),
							Transparent = false,
							Smooth = false,
							Preload = false
						};
						created++;
					}
					if (FExists(pngPath))
					{
						using var img = new MagickImage(FReadBytes(pngPath));
						if (!isNew && ts.Texture != null)
						{
							ts.Texture.SourceWidth = (ushort)img.Width;
							ts.Texture.SourceHeight = (ushort)img.Height;
							ts.Texture.TargetWidth = (ushort)img.Width;
							ts.Texture.TargetHeight = (ushort)img.Height;
							ts.Texture.BoundingWidth = (ushort)img.Width;
							ts.Texture.BoundingHeight = (ushort)img.Height;
						}
						else
						{
							ts.Texture = AddTexturePageItem(data, img);
						}
					}
					if (FExists(jsonPath))
					{
						using var jsonDoc = JsonDocument.Parse(FReadText(jsonPath));
						var root = jsonDoc.RootElement;
						ts.Transparent = GetJsonValue(root, "transparent", ts.Transparent);
						ts.Smooth = GetJsonValue(root, "smooth", ts.Smooth);
						ts.Preload = GetJsonValue(root, "preload", ts.Preload);
						if (root.TryGetProperty("gms2UnknownAlways2", out var value))
						{
							ts.GMS2UnknownAlways2 = GetJsonValue(root, "gms2UnknownAlways2", ts.GMS2UnknownAlways2);
						}
						ts.GMS2TileWidth = GetJsonValue(root, "gms2TileWidth", ts.GMS2TileWidth);
						ts.GMS2TileHeight = GetJsonValue(root, "gms2TileHeight", ts.GMS2TileHeight);
						ts.GMS2OutputBorderX = GetJsonValue(root, "gms2OutputBorderX", ts.GMS2OutputBorderX);
						ts.GMS2OutputBorderY = GetJsonValue(root, "gms2OutputBorderY", ts.GMS2OutputBorderY);
						ts.GMS2TileColumns = GetJsonValue(root, "gms2TileColumns", ts.GMS2TileColumns);
						ts.GMS2ItemsPerTileCount = GetJsonValue(root, "gms2ItemsPerTileCount", ts.GMS2ItemsPerTileCount);
						ts.GMS2TileCount = GetJsonValue(root, "gms2TileCount", ts.GMS2TileCount);
						if (root.TryGetProperty("gms2ExportedSpriteIndex", out value))
						{
							ts.GMS2ExportedSpriteIndex = GetJsonValue(root, "gms2ExportedSpriteIndex", ts.GMS2ExportedSpriteIndex);
						}
						ts.GMS2FrameLength = GetJsonValue(root, "gms2FrameLength", ts.GMS2FrameLength);
						if (data.IsVersionAtLeast(2024u, 14u, 1u))
						{
							if (root.TryGetProperty("gms2TileSeparationX", out value))
							{
								ts.GMS2TileSeparationX = GetJsonValue(root, "gms2TileSeparationX", ts.GMS2TileSeparationX);
							}
							if (root.TryGetProperty("gms2TileSeparationY", out value))
							{
								ts.GMS2TileSeparationY = GetJsonValue(root, "gms2TileSeparationY", ts.GMS2TileSeparationY);
							}
						}
						if (root.TryGetProperty("gms2TileIds", out var tileIdsElm) && tileIdsElm.ValueKind == JsonValueKind.Array)
						{
							var expected = (int)(ts.GMS2TileCount * ts.GMS2ItemsPerTileCount);
							List<JsonElement> ids = [.. tileIdsElm.EnumerateArray()];
							if (ids.Count == expected)
							{
								ts.GMS2TileIds.Clear();
								foreach (var id in ids)
								{
									ts.GMS2TileIds.Add(new GameMakerBackground.TileID
									{
										ID = (uint)id.GetInt64()
									});
								}
							}
						}
					}
					if (isNew)
					{
						data.Backgrounds.Add(ts);
					}
					imported++;
					BatchRun.Item(tsName);
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					Log("[ImportTilesets] Failed: " + tsName + ": " + ex.Message);
					BatchRun.Failed(tsName, ex);
				}
			}
			Log($"[ImportTilesets] Done. {imported} processed ({created} new).");
		}
	}

	private static void ImportExtensions(GameMakerData data, string inputDir)
	{
		var created = 0;
		var updated = 0;
		ImportNamedJsonResources(inputDir, "ImportExtensions", delegate (string name, JsonElement root)
		{
			var ext = data.Extensions?.ByName(name);
			var isNew = ext == null;
			if (isNew)
			{
				ext = new GameMakerExtension
				{
					Name = data.Strings.MakeString(name),
					FolderName = data.Strings.MakeString(""),
					Files = [],
					Options = []
				};
				created++;
			}
			else
			{
				updated++;
			}
			if (root.TryGetProperty("folderName", out var fnElm))
			{
				var fn = fnElm.GetString() ?? "";
				if (fn.Length > 0)
				{
					ext.FolderName = data.Strings.MakeString(fn);
				}
			}
			if (root.TryGetProperty("version", out var verElm))
			{
				var ver = verElm.GetString() ?? "";
				if (ver.Length > 0)
				{
					ext.Version = data.Strings.MakeString(ver);
				}
			}
			if (root.TryGetProperty("className", out var cnElm))
			{
				var cn = cnElm.GetString() ?? "";
				if (cn.Length > 0)
				{
					ext.ClassName = data.Strings.MakeString(cn);
				}
			}
			if (root.TryGetProperty("files", out var filesElm) && filesElm.ValueKind == JsonValueKind.Array)
			{
				JsonElement[] arr = [.. filesElm.EnumerateArray()];
				for (var fi = 0; fi < arr.Length; fi++)
				{
					var fElm = arr[fi];
					GameMakerExtensionFile file;
					if (fi < ext.Files.Count)
					{
						file = ext.Files[fi];
					}
					else
					{
						file = new GameMakerExtensionFile
						{
							Functions = []
						};
						ext.Files.Add(file);
					}
					if (fElm.TryGetProperty("filename", out var ffnElm))
					{
						var ffn = ffnElm.GetString() ?? "";
						if (ffn.Length > 0)
						{
							file.Filename = data.Strings.MakeString(ffn);
						}
					}
					if (fElm.TryGetProperty("kind", out var kElm))
					{
						file.Kind = (GameMakerExtensionKind)kElm.GetInt32();
					}
					if (fElm.TryGetProperty("initScript", out var isElm))
					{
						var s = isElm.GetString() ?? "";
						file.InitScript = (s.Length > 0) ? data.Strings.MakeString(s) : null;
					}
					if (fElm.TryGetProperty("cleanupScript", out var csElm))
					{
						var s2 = csElm.GetString() ?? "";
						file.CleanupScript = (s2.Length > 0) ? data.Strings.MakeString(s2) : null;
					}
					if (!fElm.TryGetProperty("functions", out var funsElm) || funsElm.ValueKind != JsonValueKind.Array)
					{
						continue;
					}
					JsonElement[] funArr = [.. funsElm.EnumerateArray()];
					for (var fui = 0; fui < funArr.Length; fui++)
					{
						var fuElm = funArr[fui];
						GameMakerExtensionFunction func;
						if (fui < file.Functions.Count)
						{
							func = file.Functions[fui];
						}
						else
						{
							func = new GameMakerExtensionFunction
							{
								Arguments = []
							};
							file.Functions.Add(func);
						}
						if (fuElm.TryGetProperty("name", out var funcNm))
						{
							var n = funcNm.GetString() ?? "";
							if (n.Length > 0)
							{
								func.Name = data.Strings.MakeString(n);
							}
						}
						if (fuElm.TryGetProperty("extName", out var enElm))
						{
							var n2 = enElm.GetString() ?? "";
							if (n2.Length > 0)
							{
								func.ExtName = data.Strings.MakeString(n2);
							}
						}
						if (fuElm.TryGetProperty("id", out var idElm))
						{
							func.ID = (uint)idElm.GetInt64();
						}
						if (fuElm.TryGetProperty("kind", out var fkElm))
						{
							func.Kind = (uint)fkElm.GetInt64();
						}
						if (fuElm.TryGetProperty("retType", out var rtElm))
						{
							func.RetType = (GameMakerExtensionVarType)rtElm.GetInt32();
						}
						if (!fuElm.TryGetProperty("arguments", out var argsElm) || argsElm.ValueKind != JsonValueKind.Array)
						{
							continue;
						}
						JsonElement[] argArr = [.. argsElm.EnumerateArray()];
						for (var ai = 0; ai < argArr.Length; ai++)
						{
							var aElm = argArr[ai];
							GameMakerExtensionFunctionArg arg;
							if (ai < func.Arguments.Count)
							{
								arg = func.Arguments[ai];
							}
							else
							{
								arg = new GameMakerExtensionFunctionArg();
								func.Arguments.Add(arg);
							}
							if (aElm.TryGetProperty("type", out var atElm))
							{
								arg.Type = (GameMakerExtensionVarType)atElm.GetInt32();
							}
						}
					}
				}
			}
			if (root.TryGetProperty("options", out var optsElm) && optsElm.ValueKind == JsonValueKind.Array)
			{
				JsonElement[] optArr = [.. optsElm.EnumerateArray()];
				for (var oi = 0; oi < optArr.Length; oi++)
				{
					var oElm = optArr[oi];
					GameMakerExtensionOption opt;
					if (oi < ext.Options.Count)
					{
						opt = ext.Options[oi];
					}
					else
					{
						opt = new GameMakerExtensionOption();
						ext.Options.Add(opt);
					}
					if (oElm.TryGetProperty("name", out var onElm))
					{
						var on = onElm.GetString() ?? "";
						if (on.Length > 0)
						{
							opt.Name = data.Strings.MakeString(on);
						}
					}
					if (oElm.TryGetProperty("value", out var ovElm))
					{
						opt.Value = data.Strings.MakeString(ovElm.GetString() ?? "");
					}
					if (oElm.TryGetProperty("kind", out var okElm))
					{
						opt.Kind = (GameMakerExtensionOption.OptionKind)okElm.GetInt32();
					}
				}
			}
			if (isNew)
			{
				data.Extensions.Add(ext);
			}
		});
		Log($"[ImportExtensions] Done. Created: {created}, Updated: {updated}");
	}

	private static bool TryGetUInt32(JsonElement element, out uint value)
	{
		if (element.ValueKind != JsonValueKind.Number)
		{
			value = 0u;
			return false;
		}
		if (element.TryGetUInt32(out value))
		{
			return true;
		}
		if (element.TryGetInt64(out var signed))
		{
			value = (uint)signed;
			return true;
		}
		if (element.TryGetDouble(out var dbl))
		{
			value = (uint)dbl;
			return true;
		}
		value = 0u;
		return false;
	}

	private static void ImportRooms(GameMakerData data, string inputDir)
	{
		var roomDirs = GetTopLevelDirs(inputDir);
		if (roomDirs.Length == 0)
		{
			return;
		}
		List<string> patchRoomOrder = [];
		var hasPatchRoomOrder = false;
		var giPath = Path.Combine(Path.GetDirectoryName(inputDir), "GeneralInfo", "GeneralInfo.json");
		if (FExists(giPath))
		{
			try
			{
				using var giDoc = JsonDocument.Parse(FReadText(giPath));
				if (giDoc.RootElement.TryGetProperty("roomOrder", out var roElm) && roElm.ValueKind == JsonValueKind.Array)
				{
					hasPatchRoomOrder = true;
					foreach (var e in roElm.EnumerateArray())
					{
						patchRoomOrder.Add(e.GetString() ?? "");
					}
				}
			}
			catch
			{
			}
		}
		string[] sorted = [.. roomDirs.OrderBy(delegate (string d)
		{
			var num2 = patchRoomOrder.IndexOf(Path.GetFileName(d));
			return (num2 < 0) ? int.MaxValue : num2;
		})];
		var lookup = RoomImportLookup.Build(data);
		Log($"[ImportRooms] Found {sorted.Length} room folder(s) to import.");
		BatchRun.Expect(sorted.Length);
		var array = sorted;
		foreach (var roomDir in array)
		{
			var roomFile = Path.Combine(roomDir, "room.json");
			if (!FExists(roomFile))
			{
				BatchRun.Item(Path.GetFileName(roomDir), ResultKind.Skipped, "Missing room.json");
				continue;
			}
			try
			{
				var roomName = Path.GetFileName(roomDir);
				using var jsonDoc = JsonDocument.Parse(FReadText(roomFile));
				var root = jsonDoc.RootElement;
				if (!lookup.Rooms.TryGetValue(roomName, out var room))
				{
					room = new GameMakerRoom
					{
						Name = MakeRoomString(data, lookup, roomName)
					};
					data.Rooms.Add(room);
					lookup.Rooms[roomName] = room;
				}
				UpdateRoomFromJson(data, room, root, lookup);
				BatchRun.Item(roomName);
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				Log("[ImportRooms] Error: " + Path.GetFileName(roomDir) + ": " + ex.Message);
				BatchRun.Failed(Path.GetFileName(roomDir), ex);
			}
		}
		if (hasPatchRoomOrder)
		{
			var generalInfo = data.GeneralInfo;
			if (generalInfo != null)
			{
				generalInfo.RoomOrder.Clear();
				foreach (var roomName2 in patchRoomOrder)
				{
					if (lookup.Rooms.TryGetValue(roomName2, out var room2))
					{
						generalInfo.RoomOrder.Add(new GameMakerResourceById<GameMakerRoom, GameMakerChunkROOM>
						{
							Resource = room2
						});
					}
				}
			}
		}
		Log("[ImportRooms] Done.");
	}

	private static void UpdateRoomFromJson(GameMakerData data, GameMakerRoom room, JsonElement d, RoomImportLookup lookup)
	{
		if (d.TryGetProperty("caption", out var captionElm) && captionElm.ValueKind == JsonValueKind.String)
		{
			var caption = captionElm.GetString() ?? "";
			if ((room.Caption?.Content ?? "") != caption)
			{
				room.Caption = MakeRoomString(data, lookup, caption);
			}
		}
		if (d.TryGetProperty("width", out var wElm) && wElm.ValueKind == JsonValueKind.Number)
		{
			room.Width = (uint)Math.Max(0, wElm.GetInt32());
		}
		if (d.TryGetProperty("height", out var hElm) && hElm.ValueKind == JsonValueKind.Number)
		{
			room.Height = (uint)Math.Max(0, hElm.GetInt32());
		}
		if (d.TryGetProperty("speed", out var spElm) && spElm.ValueKind == JsonValueKind.Number)
		{
			room.Speed = (uint)Math.Max(0, spElm.GetInt32());
		}
		if (d.TryGetProperty("persistent", out var perElm) && (perElm.ValueKind == JsonValueKind.True || perElm.ValueKind == JsonValueKind.False))
		{
			room.Persistent = perElm.GetBoolean();
		}
		if (d.TryGetProperty("backgroundColor", out var bgcElm) && TryGetUInt32(bgcElm, out var backgroundColor))
		{
			room.BackgroundColor = backgroundColor;
		}
		if (d.TryGetProperty("drawBackgroundColor", out var dbcElm) && (dbcElm.ValueKind == JsonValueKind.True || dbcElm.ValueKind == JsonValueKind.False))
		{
			room.DrawBackgroundColor = dbcElm.GetBoolean();
		}
		if (d.TryGetProperty("creationCodeId", out var ccElm) && ccElm.ValueKind == JsonValueKind.String)
		{
			var cn = ccElm.GetString() ?? "";
			if (cn.Length > 0)
			{
				if (!lookup.Code.TryGetValue(cn, out var code))
				{
					code = new GameMakerCode
					{
						Name = MakeRoomString(data, lookup, cn)
					};
					data.Code.Add(code);
					lookup.Code[cn] = code;
					var cl = new GameMakerCodeLocals
					{
						Name = code.Name
					};
					data.CodeLocals.Add(cl);
					code.LocalsCount = 0u;
				}
				room.CreationCodeId = code;
			}
		}
		if (d.TryGetProperty("flags", out var flElm) && flElm.ValueKind == JsonValueKind.Number)
		{
			room.Flags = (GameMakerRoom.RoomEntryFlags)flElm.GetInt32();
		}
		if (d.TryGetProperty("world", out var wrElm) && (wrElm.ValueKind == JsonValueKind.True || wrElm.ValueKind == JsonValueKind.False))
		{
			room.World = wrElm.GetBoolean();
		}
		if (d.TryGetProperty("top", out var topElm) && topElm.ValueKind == JsonValueKind.Number)
		{
			room.Top = (uint)Math.Max(0, topElm.GetInt32());
		}
		if (d.TryGetProperty("left", out var leftElm) && leftElm.ValueKind == JsonValueKind.Number)
		{
			room.Left = (uint)Math.Max(0, leftElm.GetInt32());
		}
		if (d.TryGetProperty("right", out var rightElm) && rightElm.ValueKind == JsonValueKind.Number)
		{
			room.Right = (uint)Math.Max(0, rightElm.GetInt32());
		}
		if (d.TryGetProperty("bottom", out var bottomElm) && bottomElm.ValueKind == JsonValueKind.Number)
		{
			room.Bottom = (uint)Math.Max(0, bottomElm.GetInt32());
		}
		if (d.TryGetProperty("gravityX", out var gxElm) && gxElm.ValueKind == JsonValueKind.Number)
		{
			room.GravityX = (float)gxElm.GetDouble();
		}
		if (d.TryGetProperty("gravityY", out var gyElm) && gyElm.ValueKind == JsonValueKind.Number)
		{
			room.GravityY = (float)gyElm.GetDouble();
		}
		if (d.TryGetProperty("metersPerPixel", out var mppElm) && mppElm.ValueKind == JsonValueKind.Number)
		{
			room.MetersPerPixel = (float)mppElm.GetDouble();
		}
		if (d.TryGetProperty("gridWidth", out var gwElm) && gwElm.ValueKind == JsonValueKind.Number)
		{
			room.GridWidth = gwElm.GetDouble();
		}
		if (d.TryGetProperty("gridHeight", out var ghElm) && ghElm.ValueKind == JsonValueKind.Number)
		{
			room.GridHeight = ghElm.GetDouble();
		}
		if (d.TryGetProperty("gridThicknessPx", out var gtElm) && gtElm.ValueKind == JsonValueKind.Number)
		{
			room.GridThicknessPx = gtElm.GetDouble();
		}
		if (d.TryGetProperty("backgrounds", out var bgsElm) && bgsElm.ValueKind == JsonValueKind.Array)
		{
			room.Backgrounds.Clear();
			foreach (var bgElm in bgsElm.EnumerateArray())
			{
				var bg = new GameMakerRoom.Background
				{
					ParentRoom = room
				};
				if (bgElm.TryGetProperty("enabled", out var enElm) && (enElm.ValueKind == JsonValueKind.True || enElm.ValueKind == JsonValueKind.False))
				{
					bg.Enabled = enElm.GetBoolean();
				}
				if (bgElm.TryGetProperty("foreground", out var fgElm) && (fgElm.ValueKind == JsonValueKind.True || fgElm.ValueKind == JsonValueKind.False))
				{
					bg.Foreground = fgElm.GetBoolean();
				}
				if (bgElm.TryGetProperty("backgroundDefinition", out var bdElm) && bdElm.ValueKind == JsonValueKind.String)
				{
					var bn = bdElm.GetString() ?? "";
					if (bn.Length > 0 && lookup.Backgrounds.TryGetValue(bn, out var bgDef))
					{
						bg.BackgroundDefinition = bgDef;
					}
				}
				if (bgElm.TryGetProperty("x", out var xElm) && xElm.ValueKind == JsonValueKind.Number)
				{
					bg.X = xElm.GetInt32();
				}
				if (bgElm.TryGetProperty("y", out var yElm) && yElm.ValueKind == JsonValueKind.Number)
				{
					bg.Y = yElm.GetInt32();
				}
				if (bgElm.TryGetProperty("tiledHorizontally", out var thElm) && (thElm.ValueKind == JsonValueKind.True || thElm.ValueKind == JsonValueKind.False))
				{
					bg.TiledHorizontally = thElm.GetBoolean();
				}
				if (bgElm.TryGetProperty("tiledVertically", out var tvElm) && (tvElm.ValueKind == JsonValueKind.True || tvElm.ValueKind == JsonValueKind.False))
				{
					bg.TiledVertically = tvElm.GetBoolean();
				}
				if (bgElm.TryGetProperty("speedX", out var sxElm) && sxElm.ValueKind == JsonValueKind.Number)
				{
					bg.SpeedX = sxElm.GetInt32();
				}
				if (bgElm.TryGetProperty("speedY", out var syElm) && syElm.ValueKind == JsonValueKind.Number)
				{
					bg.SpeedY = syElm.GetInt32();
				}
				if (bgElm.TryGetProperty("stretch", out var stElm) && (stElm.ValueKind == JsonValueKind.True || stElm.ValueKind == JsonValueKind.False))
				{
					bg.Stretch = stElm.GetBoolean();
				}
				room.Backgrounds.Add(bg);
			}
		}
		if (d.TryGetProperty("views", out var viewsElm) && viewsElm.ValueKind == JsonValueKind.Array)
		{
			room.Views.Clear();
			foreach (var vElm in viewsElm.EnumerateArray())
			{
				var view = new GameMakerRoom.View();
				if (vElm.TryGetProperty("enabled", out var enElm2) && (enElm2.ValueKind == JsonValueKind.True || enElm2.ValueKind == JsonValueKind.False))
				{
					view.Enabled = enElm2.GetBoolean();
				}
				if (vElm.TryGetProperty("viewX", out var vxElm) && vxElm.ValueKind == JsonValueKind.Number)
				{
					view.ViewX = vxElm.GetInt32();
				}
				if (vElm.TryGetProperty("viewY", out var vyElm) && vyElm.ValueKind == JsonValueKind.Number)
				{
					view.ViewY = vyElm.GetInt32();
				}
				if (vElm.TryGetProperty("viewWidth", out var vwElm) && vwElm.ValueKind == JsonValueKind.Number)
				{
					view.ViewWidth = vwElm.GetInt32();
				}
				if (vElm.TryGetProperty("viewHeight", out var vhElm) && vhElm.ValueKind == JsonValueKind.Number)
				{
					view.ViewHeight = vhElm.GetInt32();
				}
				if (vElm.TryGetProperty("portX", out var pxElm) && pxElm.ValueKind == JsonValueKind.Number)
				{
					view.PortX = pxElm.GetInt32();
				}
				if (vElm.TryGetProperty("portY", out var pyElm) && pyElm.ValueKind == JsonValueKind.Number)
				{
					view.PortY = pyElm.GetInt32();
				}
				if (vElm.TryGetProperty("portWidth", out var pwElm) && pwElm.ValueKind == JsonValueKind.Number)
				{
					view.PortWidth = pwElm.GetInt32();
				}
				if (vElm.TryGetProperty("portHeight", out var phElm) && phElm.ValueKind == JsonValueKind.Number)
				{
					view.PortHeight = phElm.GetInt32();
				}
				if (vElm.TryGetProperty("borderX", out var bxElm) && TryGetUInt32(bxElm, out var borderX))
				{
					view.BorderX = borderX;
				}
				if (vElm.TryGetProperty("borderY", out var byElm) && TryGetUInt32(byElm, out var borderY))
				{
					view.BorderY = borderY;
				}
				if (vElm.TryGetProperty("speedX", out var sxElm2) && sxElm2.ValueKind == JsonValueKind.Number)
				{
					view.SpeedX = sxElm2.GetInt32();
				}
				if (vElm.TryGetProperty("speedY", out var syElm2) && syElm2.ValueKind == JsonValueKind.Number)
				{
					view.SpeedY = syElm2.GetInt32();
				}
				if (vElm.TryGetProperty("objectId", out var oiElm) && oiElm.ValueKind == JsonValueKind.String)
				{
					var on = oiElm.GetString() ?? "";
					if (on.Length > 0 && lookup.GameObjects.TryGetValue(on, out var obj))
					{
						view.ObjectId = obj;
					}
				}
				room.Views.Add(view);
			}
		}
		if (d.TryGetProperty("gameObjects", out var gosElm) && gosElm.ValueKind == JsonValueKind.Array)
		{
			room.GameObjects.Clear();
			foreach (var oElm in gosElm.EnumerateArray())
			{
				var go = new GameMakerRoom.GameObject();
				if (oElm.TryGetProperty("x", out var xElm2) && xElm2.ValueKind == JsonValueKind.Number)
				{
					go.X = xElm2.GetInt32();
				}
				if (oElm.TryGetProperty("y", out var yElm2) && yElm2.ValueKind == JsonValueKind.Number)
				{
					go.Y = yElm2.GetInt32();
				}
				if (oElm.TryGetProperty("objectDefinition", out var odElm) && odElm.ValueKind == JsonValueKind.String)
				{
					var on2 = odElm.GetString() ?? "";
					if (on2.Length > 0 && lookup.GameObjects.TryGetValue(on2, out var od))
					{
						go.ObjectDefinition = od;
					}
				}
				if (oElm.TryGetProperty("instanceID", out var iiElm) && TryGetUInt32(iiElm, out var instanceId))
				{
					go.InstanceID = instanceId;
				}
				if (oElm.TryGetProperty("creationCode", out var ccElm2) && ccElm2.ValueKind == JsonValueKind.String)
				{
					var cn2 = ccElm2.GetString() ?? "";
					if (cn2.Length > 0)
					{
						go.CreationCode = EnsureCodeEntry(data, cn2, lookup);
					}
				}
				if (oElm.TryGetProperty("scaleX", out var sxElm3) && sxElm3.ValueKind == JsonValueKind.Number)
				{
					go.ScaleX = (float)sxElm3.GetDouble();
				}
				if (oElm.TryGetProperty("scaleY", out var syElm3) && syElm3.ValueKind == JsonValueKind.Number)
				{
					go.ScaleY = (float)syElm3.GetDouble();
				}
				if (oElm.TryGetProperty("color", out var colElm) && TryGetUInt32(colElm, out var gameObjectColor))
				{
					go.Color = gameObjectColor;
				}
				if (oElm.TryGetProperty("rotation", out var rotElm) && rotElm.ValueKind == JsonValueKind.Number)
				{
					go.Rotation = (float)rotElm.GetDouble();
				}
				if (oElm.TryGetProperty("preCreateCode", out var pcElm) && pcElm.ValueKind == JsonValueKind.String)
				{
					var pcn = pcElm.GetString() ?? "";
					if (pcn.Length > 0)
					{
						go.PreCreateCode = EnsureCodeEntry(data, pcn, lookup);
					}
				}
				if (data.IsVersionAtLeast(2u, 2u, 2u, 302u))
				{
					if (oElm.TryGetProperty("imageSpeed", out var isElm) && isElm.ValueKind == JsonValueKind.Number)
					{
						go.ImageSpeed = (float)isElm.GetDouble();
					}
					if (oElm.TryGetProperty("imageIndex", out var ixElm) && ixElm.ValueKind == JsonValueKind.Number)
					{
						go.ImageIndex = ixElm.GetInt32();
					}
				}
				room.GameObjects.Add(go);
			}
		}
		var roomGameObjectsByInstanceId = new Dictionary<uint, GameMakerRoom.GameObject>(room.GameObjects.Count);
		foreach (var go2 in room.GameObjects)
		{
			roomGameObjectsByInstanceId.TryAdd(go2.InstanceID, go2);
		}
		if (d.TryGetProperty("tiles", out var tilesElm) && tilesElm.ValueKind == JsonValueKind.Array)
		{
			room.Tiles.Clear();
			foreach (var tElm in tilesElm.EnumerateArray())
			{
				var tile = new GameMakerRoom.Tile();
				if (tElm.TryGetProperty("x", out var xElm3) && xElm3.ValueKind == JsonValueKind.Number)
				{
					tile.X = xElm3.GetInt32();
				}
				if (tElm.TryGetProperty("y", out var yElm3) && yElm3.ValueKind == JsonValueKind.Number)
				{
					tile.Y = yElm3.GetInt32();
				}
				if (tElm.TryGetProperty("spriteMode", out var smElm) && (smElm.ValueKind == JsonValueKind.True || smElm.ValueKind == JsonValueKind.False))
				{
					tile.spriteMode = smElm.GetBoolean();
				}
				if (tile.spriteMode)
				{
					if (tElm.TryGetProperty("spriteDefinition", out var sdElm) && sdElm.ValueKind == JsonValueKind.String)
					{
						var sn = sdElm.GetString() ?? "";
						if (sn.Length > 0 && lookup.Sprites.TryGetValue(sn, out var spr))
						{
							tile.SpriteDefinition = spr;
						}
					}
				}
				else if (tElm.TryGetProperty("backgroundDefinition", out var bdElm2) && bdElm2.ValueKind == JsonValueKind.String)
				{
					var bn2 = bdElm2.GetString() ?? "";
					if (bn2.Length > 0 && lookup.Backgrounds.TryGetValue(bn2, out var bg2))
					{
						tile.BackgroundDefinition = bg2;
					}
				}
				if (tElm.TryGetProperty("sourceX", out var sxElm4) && sxElm4.ValueKind == JsonValueKind.Number)
				{
					tile.SourceX = sxElm4.GetInt32();
				}
				if (tElm.TryGetProperty("sourceY", out var syElm4) && syElm4.ValueKind == JsonValueKind.Number)
				{
					tile.SourceY = syElm4.GetInt32();
				}
				if (tElm.TryGetProperty("width", out var wElm2) && TryGetUInt32(wElm2, out var tileWidth))
				{
					tile.Width = tileWidth;
				}
				if (tElm.TryGetProperty("height", out var hElm2) && TryGetUInt32(hElm2, out var tileHeight))
				{
					tile.Height = tileHeight;
				}
				if (tElm.TryGetProperty("tileDepth", out var tdElm) && tdElm.ValueKind == JsonValueKind.Number)
				{
					tile.TileDepth = tdElm.GetInt32();
				}
				if (tElm.TryGetProperty("instanceID", out var iiElm2) && TryGetUInt32(iiElm2, out var tileInstanceId))
				{
					tile.InstanceID = tileInstanceId;
				}
				if (tElm.TryGetProperty("scaleX", out var scxElm) && scxElm.ValueKind == JsonValueKind.Number)
				{
					tile.ScaleX = (float)scxElm.GetDouble();
				}
				if (tElm.TryGetProperty("scaleY", out var scyElm) && scyElm.ValueKind == JsonValueKind.Number)
				{
					tile.ScaleY = (float)scyElm.GetDouble();
				}
				if (tElm.TryGetProperty("color", out var colElm2) && TryGetUInt32(colElm2, out var tileColor))
				{
					tile.Color = tileColor;
				}
				room.Tiles.Add(tile);
			}
		}
		if (data.IsGameMaker2() && d.TryGetProperty("layers", out var layersElm) && layersElm.ValueKind == JsonValueKind.Array)
		{
			List<GameMakerRoom.Layer> previousLayers = [.. room.Layers];
			room.Layers.Clear();
			foreach (var lElm in layersElm.EnumerateArray())
			{
				if (!lElm.TryGetProperty("layerType", out var ltElm) || ltElm.ValueKind != JsonValueKind.Number)
				{
					continue;
				}
				var lt = ltElm.GetInt32();
				var layer = new GameMakerRoom.Layer
				{
					LayerType = (GameMakerRoom.LayerType)lt,
					ParentRoom = room
				};
				if (lElm.TryGetProperty("layerName", out var lnElm) && lnElm.ValueKind == JsonValueKind.String)
				{
					layer.LayerName = MakeRoomString(data, lookup, lnElm.GetString());
				}
				if (lElm.TryGetProperty("layerId", out var liElm) && TryGetUInt32(liElm, out var layerId))
				{
					layer.LayerId = layerId;
				}
				if (lElm.TryGetProperty("layerDepth", out var ldElm) && ldElm.ValueKind == JsonValueKind.Number)
				{
					layer.LayerDepth = ldElm.GetInt32();
				}
				if (lElm.TryGetProperty("xOffset", out var xoElm) && xoElm.ValueKind == JsonValueKind.Number)
				{
					layer.XOffset = (float)xoElm.GetDouble();
				}
				if (lElm.TryGetProperty("yOffset", out var yoElm) && yoElm.ValueKind == JsonValueKind.Number)
				{
					layer.YOffset = (float)yoElm.GetDouble();
				}
				if (lElm.TryGetProperty("hSpeed", out var hsElm) && hsElm.ValueKind == JsonValueKind.Number)
				{
					layer.HSpeed = (float)hsElm.GetDouble();
				}
				if (lElm.TryGetProperty("vSpeed", out var vsElm) && vsElm.ValueKind == JsonValueKind.Number)
				{
					layer.VSpeed = (float)vsElm.GetDouble();
				}
				if (lElm.TryGetProperty("isVisible", out var ivElm) && (ivElm.ValueKind == JsonValueKind.True || ivElm.ValueKind == JsonValueKind.False))
				{
					layer.IsVisible = ivElm.GetBoolean();
				}
				if (data.IsVersionAtLeast(2022u, 1u))
				{
					if (lElm.TryGetProperty("effectEnabled", out var eeElm) && (eeElm.ValueKind == JsonValueKind.True || eeElm.ValueKind == JsonValueKind.False))
					{
						layer.EffectEnabled = eeElm.GetBoolean();
					}
					if (lElm.TryGetProperty("effectType", out var etElm) && etElm.ValueKind == JsonValueKind.String)
					{
						layer.EffectType = MakeRoomString(data, lookup, etElm.GetString());
					}
				}
				switch (lt)
				{
					case 2:
						{
							var instData = new GameMakerRoom.Layer.LayerInstancesData();
							if (lElm.TryGetProperty("instanceIds", out var idsElm) && idsElm.ValueKind == JsonValueKind.Array)
							{
								foreach (var item in idsElm.EnumerateArray())
								{
									if (TryGetUInt32(item, out var iid) && roomGameObjectsByInstanceId.TryGetValue(iid, out var go3))
									{
										instData.Instances.Add(go3);
									}
								}
							}
							layer.Data = instData;
							break;
						}
					case 4:
						{
							var tilesData = new GameMakerRoom.Layer.LayerTilesData
							{
								ParentLayer = layer
							};
							if (lElm.TryGetProperty("tilesBackground", out var tbElm) && tbElm.ValueKind == JsonValueKind.String)
							{
								var bn5 = tbElm.GetString() ?? "";
								if (bn5.Length > 0 && lookup.Backgrounds.TryGetValue(bn5, out var bg5))
								{
									tilesData.Background = bg5;
								}
							}
							if (lElm.TryGetProperty("tilesX", out var txElm) && TryGetUInt32(txElm, out var tilesX))
							{
								tilesData.TilesX = tilesX;
							}
							if (lElm.TryGetProperty("tilesY", out var tyElm) && TryGetUInt32(tyElm, out var tilesY))
							{
								tilesData.TilesY = tilesY;
							}
							if (lElm.TryGetProperty("tileData", out var tdElm2) && tdElm2.ValueKind == JsonValueKind.Array)
							{
								List<uint[]> rows = [];
								foreach (var rowElm in tdElm2.EnumerateArray())
								{
									if (rowElm.ValueKind == JsonValueKind.Array)
									{
										rows.Add([.. from c in rowElm.EnumerateArray()
												  where c.ValueKind == JsonValueKind.Number
												  select (uint)c.GetInt32()]);
									}
								}
								tilesData.TileData = [.. rows];
							}
							layer.Data = tilesData;
							break;
						}
					case 1:
						{
							var bgData = new GameMakerRoom.Layer.LayerBackgroundData
							{
								ParentLayer = layer
							};
							if (lElm.TryGetProperty("backgroundData", out var bdElm4) && bdElm4.ValueKind == JsonValueKind.Object)
							{
								if (bdElm4.TryGetProperty("visible", out var vElm2) && (vElm2.ValueKind == JsonValueKind.True || vElm2.ValueKind == JsonValueKind.False))
								{
									bgData.Visible = vElm2.GetBoolean();
								}
								if (bdElm4.TryGetProperty("foreground", out var fElm) && (fElm.ValueKind == JsonValueKind.True || fElm.ValueKind == JsonValueKind.False))
								{
									bgData.Foreground = fElm.GetBoolean();
								}
								if (bdElm4.TryGetProperty("sprite", out var sElm2) && sElm2.ValueKind == JsonValueKind.String)
								{
									var sn4 = sElm2.GetString() ?? "";
									if (sn4.Length > 0 && lookup.Sprites.TryGetValue(sn4, out var spr4))
									{
										bgData.Sprite = spr4;
									}
								}
								if (bdElm4.TryGetProperty("tiledHorizontally", out var thElm2) && (thElm2.ValueKind == JsonValueKind.True || thElm2.ValueKind == JsonValueKind.False))
								{
									bgData.TiledHorizontally = thElm2.GetBoolean();
								}
								if (bdElm4.TryGetProperty("tiledVertically", out var tvElm2) && (tvElm2.ValueKind == JsonValueKind.True || tvElm2.ValueKind == JsonValueKind.False))
								{
									bgData.TiledVertically = tvElm2.GetBoolean();
								}
								if (bdElm4.TryGetProperty("stretch", out var stElm2) && (stElm2.ValueKind == JsonValueKind.True || stElm2.ValueKind == JsonValueKind.False))
								{
									bgData.Stretch = stElm2.GetBoolean();
								}
								if (bdElm4.TryGetProperty("color", out var cElm) && TryGetUInt32(cElm, out var backgroundLayerColor))
								{
									bgData.Color = backgroundLayerColor;
								}
								if (bdElm4.TryGetProperty("firstFrame", out var ffElm) && ffElm.ValueKind == JsonValueKind.Number)
								{
									bgData.FirstFrame = (float)ffElm.GetDouble();
								}
								if (bdElm4.TryGetProperty("animationSpeed", out var asElm2) && asElm2.ValueKind == JsonValueKind.Number)
								{
									bgData.AnimationSpeed = (float)asElm2.GetDouble();
								}
								if (bdElm4.TryGetProperty("animationSpeedType", out var astElm2) && astElm2.ValueKind == JsonValueKind.Number)
								{
									bgData.AnimationSpeedType = (AnimationSpeedType)astElm2.GetInt32();
								}
							}
							layer.Data = bgData;
							break;
						}
					case 3:
						{
							var assetsData = new GameMakerRoom.Layer.LayerAssetsData
							{
								LegacyTiles = [],
								Sprites = []
							};
							if (data.IsVersionAtLeast(2u, 3u))
							{
								assetsData.Sequences = [];
							}
							if (lElm.TryGetProperty("assetsData", out var adElm) && adElm.ValueKind == JsonValueKind.Object)
							{
								if (adElm.TryGetProperty("legacyTiles", out var ltElm2) && ltElm2.ValueKind == JsonValueKind.Array)
								{
									foreach (var tElm2 in ltElm2.EnumerateArray())
									{
										var tile2 = new GameMakerRoom.Tile();
										if (tElm2.TryGetProperty("x", out var xElm4) && xElm4.ValueKind == JsonValueKind.Number)
										{
											tile2.X = xElm4.GetInt32();
										}
										if (tElm2.TryGetProperty("y", out var yElm4) && yElm4.ValueKind == JsonValueKind.Number)
										{
											tile2.Y = yElm4.GetInt32();
										}
										if (tElm2.TryGetProperty("spriteMode", out var smElm2) && (smElm2.ValueKind == JsonValueKind.True || smElm2.ValueKind == JsonValueKind.False))
										{
											tile2.spriteMode = smElm2.GetBoolean();
										}
										if (tElm2.TryGetProperty("sourceX", out var sxElm5) && sxElm5.ValueKind == JsonValueKind.Number)
										{
											tile2.SourceX = sxElm5.GetInt32();
										}
										if (tElm2.TryGetProperty("sourceY", out var syElm5) && syElm5.ValueKind == JsonValueKind.Number)
										{
											tile2.SourceY = syElm5.GetInt32();
										}
										if (tElm2.TryGetProperty("width", out var wElm3) && TryGetUInt32(wElm3, out var legacyWidth))
										{
											tile2.Width = legacyWidth;
										}
										if (tElm2.TryGetProperty("height", out var hElm3) && TryGetUInt32(hElm3, out var legacyHeight))
										{
											tile2.Height = legacyHeight;
										}
										if (tElm2.TryGetProperty("tileDepth", out var dElm) && dElm.ValueKind == JsonValueKind.Number)
										{
											tile2.TileDepth = dElm.GetInt32();
										}
										if (tElm2.TryGetProperty("instanceID", out var iiElm3) && TryGetUInt32(iiElm3, out var legacyInstanceId))
										{
											tile2.InstanceID = legacyInstanceId;
										}
										if (tElm2.TryGetProperty("scaleX", out var scxElm2) && scxElm2.ValueKind == JsonValueKind.Number)
										{
											tile2.ScaleX = (float)scxElm2.GetDouble();
										}
										if (tElm2.TryGetProperty("scaleY", out var scyElm2) && scyElm2.ValueKind == JsonValueKind.Number)
										{
											tile2.ScaleY = (float)scyElm2.GetDouble();
										}
										if (tElm2.TryGetProperty("color", out var colElm3) && TryGetUInt32(colElm3, out var legacyTileColor))
										{
											tile2.Color = legacyTileColor;
										}
										if (tile2.spriteMode)
										{
											if (tElm2.TryGetProperty("spriteDefinition", out var sdElm2) && sdElm2.ValueKind == JsonValueKind.String)
											{
												var sn2 = sdElm2.GetString() ?? "";
												if (sn2.Length > 0 && lookup.Sprites.TryGetValue(sn2, out var spr2))
												{
													tile2.SpriteDefinition = spr2;
												}
											}
										}
										else if (tElm2.TryGetProperty("backgroundDefinition", out var bdElm3) && bdElm3.ValueKind == JsonValueKind.String)
										{
											var bn3 = bdElm3.GetString() ?? "";
											if (bn3.Length > 0 && lookup.Backgrounds.TryGetValue(bn3, out var bg3))
											{
												tile2.BackgroundDefinition = bg3;
											}
										}
										else if (tElm2.TryGetProperty("background", out var bgElm2) && bgElm2.ValueKind == JsonValueKind.String)
										{
											var bn4 = bgElm2.GetString() ?? "";
											if (bn4.Length > 0 && lookup.Backgrounds.TryGetValue(bn4, out var bg4))
											{
												tile2.BackgroundDefinition = bg4;
											}
										}
										assetsData.LegacyTiles.Add(tile2);
									}
								}
								if (adElm.TryGetProperty("sprites", out var sprElm) && sprElm.ValueKind == JsonValueKind.Array)
								{
									foreach (var sElm in sprElm.EnumerateArray())
									{
										var si = new GameMakerRoom.SpriteInstance();
										if (sElm.TryGetProperty("name", out var nElm) && nElm.ValueKind == JsonValueKind.String)
										{
											si.Name = MakeRoomString(data, lookup, nElm.GetString());
										}
										if (sElm.TryGetProperty("sprite", out var srElm) && srElm.ValueKind == JsonValueKind.String)
										{
											var sn3 = srElm.GetString() ?? "";
											if (sn3.Length > 0 && lookup.Sprites.TryGetValue(sn3, out var spr3))
											{
												si.Sprite = spr3;
											}
										}
										if (sElm.TryGetProperty("x", out var xElm5) && xElm5.ValueKind == JsonValueKind.Number)
										{
											si.X = xElm5.GetInt32();
										}
										if (sElm.TryGetProperty("y", out var yElm5) && yElm5.ValueKind == JsonValueKind.Number)
										{
											si.Y = yElm5.GetInt32();
										}
										if (sElm.TryGetProperty("scaleX", out var sxElm6) && sxElm6.ValueKind == JsonValueKind.Number)
										{
											si.ScaleX = (float)sxElm6.GetDouble();
										}
										if (sElm.TryGetProperty("scaleY", out var syElm6) && syElm6.ValueKind == JsonValueKind.Number)
										{
											si.ScaleY = (float)syElm6.GetDouble();
										}
										if (sElm.TryGetProperty("color", out var colElm4) && TryGetUInt32(colElm4, out var spriteColor))
										{
											si.Color = spriteColor;
										}
										if (sElm.TryGetProperty("animationSpeed", out var asElm) && asElm.ValueKind == JsonValueKind.Number)
										{
											si.AnimationSpeed = (float)asElm.GetDouble();
										}
										if (sElm.TryGetProperty("animationSpeedType", out var astElm) && astElm.ValueKind == JsonValueKind.Number)
										{
											si.AnimationSpeedType = (AnimationSpeedType)astElm.GetInt32();
										}
										if (sElm.TryGetProperty("frameIndex", out var fiElm) && fiElm.ValueKind == JsonValueKind.Number)
										{
											si.FrameIndex = (float)fiElm.GetDouble();
										}
										if (sElm.TryGetProperty("rotation", out var rotElm2) && rotElm2.ValueKind == JsonValueKind.Number)
										{
											si.Rotation = (float)rotElm2.GetDouble();
										}
										assetsData.Sprites.Add(si);
									}
								}
							}
							layer.Data = assetsData;
							break;
						}
					case 6:
						layer.Data = new GameMakerRoom.Layer.LayerEffectData();
						break;
				}
				var previous = previousLayers.FirstOrDefault(l => l.LayerId == layer.LayerId && l.LayerType == layer.LayerType);
				if (previous != null)
				{
					CarryOverLayerData(previous, layer);
				}
				room.Layers.Add(layer);
			}
		}
		if (data.IsVersionAtLeast(2u, 3u) && d.TryGetProperty("sequences", out var seqsElm) && seqsElm.ValueKind == JsonValueKind.Array)
		{
			room.Sequences.Clear();
			foreach (var sElm3 in seqsElm.EnumerateArray())
			{
				if (sElm3.ValueKind == JsonValueKind.String)
				{
					var sn5 = sElm3.GetString() ?? "";
					if (sn5.Length != 0 && lookup.Sequences.TryGetValue(sn5, out var seq))
					{
						room.Sequences.Add(new GameMakerResourceById<GameMakerSequence, GameMakerChunkSEQN>
						{
							Resource = seq
						});
					}
				}
			}
		}
		if (!data.IsVersionAtLeast(2024u, 13u) || !d.TryGetProperty("instanceCreationOrderIDs", out var icoElm) || icoElm.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		if (room.InstanceCreationOrderIDs == null)
		{
			var instanceIDList = room.InstanceCreationOrderIDs = new GameMakerRoom.InstanceIDList();
		}
		room.InstanceCreationOrderIDs.InstanceIDs.Clear();
		foreach (var idElm in icoElm.EnumerateArray())
		{
			if (idElm.ValueKind == JsonValueKind.Number)
			{
				room.InstanceCreationOrderIDs.InstanceIDs.Add((uint)Math.Max(0, idElm.GetInt32()));
			}
		}
	}

	// The room JSON has no place for effect properties, sequence, particle system and text item instances, or an
	// empty-versus-missing effect type, so an edited room keeps them from the layer it replaces.
	private static void CarryOverLayerData(GameMakerRoom.Layer previous, GameMakerRoom.Layer layer)
	{
		if ((previous.EffectType?.Content ?? "") == (layer.EffectType?.Content ?? ""))
		{
			layer.EffectType = previous.EffectType;
			layer.EffectProperties = previous.EffectProperties;
		}
		if (previous.Data is GameMakerRoom.Layer.LayerAssetsData oldAssets && layer.Data is GameMakerRoom.Layer.LayerAssetsData newAssets)
		{
			newAssets.Sequences = oldAssets.Sequences ?? newAssets.Sequences;
			newAssets.NineSlices = oldAssets.NineSlices;
			newAssets.ParticleSystems = oldAssets.ParticleSystems;
			newAssets.TextItems = oldAssets.TextItems;
		}
		else if (previous.Data is GameMakerRoom.Layer.LayerEffectData && layer.Data is GameMakerRoom.Layer.LayerEffectData)
		{
			layer.Data = previous.Data;
		}
	}

	private static GameMakerString MakeRoomString(GameMakerData data, RoomImportLookup lookup, string content)
	{
		if (lookup.Strings.TryGetValue(content, out var existing))
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
		lookup.Strings[content] = created;
		return created;
	}

	private static GameMakerCode EnsureCodeEntry(GameMakerData data, string codeName, RoomImportLookup lookup)
	{
		if (!lookup.Code.TryGetValue(codeName, out var code))
		{
			code = new GameMakerCode
			{
				Name = MakeRoomString(data, lookup, codeName)
			};
			data.Code.Add(code);
			lookup.Code[codeName] = code;
			var cl = new GameMakerCodeLocals
			{
				Name = code.Name
			};
			data.CodeLocals.Add(cl);
			code.LocalsCount = 0u;
		}
		return code;
	}

	private static bool ShouldAutoGenerateCollisionMask(JsonElement? meta)
	{
		if (meta.HasValue)
		{
			return meta.Value.TryGetProperty("collisionMasks", out _);
		}
		return true;
	}

	private static void ImportSprites(GameMakerData data, string inputDir)
	{
		Log("[ImportSprites] Importing from: " + inputDir);
		Dictionary<string, JsonElement?> spriteMetadataCache = [];
		Dictionary<string, List<Dictionary<string, object>>?> textureFrameCache = [];
		Dictionary<GameMakerEmbeddedTexture, MagickImage> atlasImageCache = [];
		var lookup = DataLookup.Build(data);
		var dirs = GetTopLevelDirs(inputDir);
		BatchRun.Expect(dirs.Length);
		HashSet<string> metaFailed = [];
		HashSet<string> handledFolders = [];
		List<SpriteImportData> importDataList = [];
		var array = dirs;
		foreach (var spriteFolder in array)
		{
			var folderName = Path.GetFileName(spriteFolder);
			var meta = LoadMeta(folderName);
			if (meta.HasValue)
			{
				var spriteName = folderName;
				if (meta.Value.TryGetProperty("name", out var nameElm))
				{
					spriteName = nameElm.GetString() ?? folderName;
				}
				var frameData = LoadFrameData(folderName);
				var isNew = FindSprite(spriteName) == null;
				var targetIndex = -1;
				if (meta.Value.TryGetProperty("index", out var indexElm))
				{
					targetIndex = indexElm.GetInt32();
				}
				var hasValidTextureIndex = frameData != null && frameData.Count == 0 || (frameData?.Any(f => f.ContainsKey("texturePageIndex") && Convert.ToInt32(f["texturePageIndex"], CultureInfo.InvariantCulture) >= 0 && Convert.ToInt32(f["texturePageIndex"], CultureInfo.InvariantCulture) < data.EmbeddedTextures.Count) ?? false);
				importDataList.Add(new SpriteImportData
				{
					TargetIndex = targetIndex,
					Name = spriteName,
					FolderName = folderName,
					FolderPath = spriteFolder,
					Meta = meta,
					FrameData = frameData,
					IsNew = isNew,
					HasValidTextureIndex = hasValidTextureIndex
				});
			}
			else if (!metaFailed.Contains(folderName))
			{
				BatchRun.Item(folderName, ResultKind.Skipped, "Missing sprite metadata");
			}
		}
		Log($"[ImportSprites] Collected {importDataList.Count} sprites. New: {importDataList.Count(d => d.IsNew)}, Existing: {importDataList.Count(d => !d.IsNew)}");
		ReportProgress(5, 100);
		foreach (var importData in importDataList)
		{
			if (importData.Name != importData.FolderName && spriteMetadataCache.TryGetValue(importData.FolderName, out var cachedMeta))
			{
				spriteMetadataCache[importData.Name] = cachedMeta;
			}
		}
		var created = 0;
		foreach (var importData2 in importDataList.Where(d => d.IsNew && d.HasValidTextureIndex))
		{
			var sprite2 = CreateSpriteFromMetadata(data, importData2, FindExistingTPI, MakeSpriteString);
			if (sprite2 != null)
			{
				lookup.Add(sprite2);
				handledFolders.Add(importData2.FolderPath);
				created++;
			}
		}
		Log($"[ImportSprites] Phase 1a: {created} new sprites created from metadata");
		ReportProgress(15, 100);
		var updated = 0;
		foreach (var importData3 in importDataList.Where(d => !d.IsNew))
		{
			var sprite3 = FindSprite(importData3.Name);
			if (sprite3 == null || !importData3.Meta.HasValue)
			{
				continue;
			}
			ApplySpriteMetadata(data, sprite3, importData3.Meta.Value);
			if (importData3.FrameData != null)
			{
				List<GameMakerTexturePageItem> oldTPIs = [];
				foreach (var texEntry in sprite3.Textures)
				{
					if (texEntry?.Texture != null)
					{
						oldTPIs.Add(texEntry.Texture);
					}
				}
				sprite3.Textures.Clear();
				var oldTPIIndex = 0;
				foreach (var frame in importData3.FrameData)
				{
					if (frame.TryGetValue("isNull", out var isNull) && (bool)isNull)
					{
						sprite3.Textures.Add(new GameMakerSprite.TextureEntry
						{
							Texture = null
						});
						continue;
					}
					var texturePageIndex = frame.TryGetValue("texturePageIndex", out var tpIdx) ? Convert.ToInt32(tpIdx, CultureInfo.InvariantCulture) : (-1);
					if (texturePageIndex < 0 || texturePageIndex >= data.EmbeddedTextures.Count)
					{
						break;
					}
					var sourceX = (ushort)(frame.TryGetValue("sourceX", out var sxv) ? ((ushort)Convert.ToInt32(sxv, CultureInfo.InvariantCulture)) : 0);
					var sourceY = (ushort)(frame.TryGetValue("sourceY", out var syv) ? ((ushort)Convert.ToInt32(syv, CultureInfo.InvariantCulture)) : 0);
					var sourceWidth = (ushort)(frame.TryGetValue("sourceWidth", out var swv) ? ((ushort)Convert.ToInt32(swv, CultureInfo.InvariantCulture)) : 0);
					var sourceHeight = (ushort)(frame.TryGetValue("sourceHeight", out var shv) ? ((ushort)Convert.ToInt32(shv, CultureInfo.InvariantCulture)) : 0);
					var tpi = FindExistingTPI(texturePageIndex, sourceX, sourceY, sourceWidth, sourceHeight);
					if (tpi == null && oldTPIIndex < oldTPIs.Count)
					{
						tpi = oldTPIs[oldTPIIndex++];
						tpi.TexturePage = data.EmbeddedTextures[texturePageIndex];
						tpi.SourceX = sourceX;
						tpi.SourceY = sourceY;
						tpi.SourceWidth = sourceWidth;
						tpi.SourceHeight = sourceHeight;
						ApplyOptionalFrameFields(tpi, frame);
					}
					else if (tpi == null)
					{
						tpi = new GameMakerTexturePageItem
						{
							Name = new GameMakerString($"PageItem {data.TexturePageItems.Count}"),
							TexturePage = data.EmbeddedTextures[texturePageIndex],
							SourceX = sourceX,
							SourceY = sourceY,
							SourceWidth = sourceWidth,
							SourceHeight = sourceHeight
						};
						ApplyOptionalFrameFields(tpi, frame);
						data.TexturePageItems.Add(tpi);
					}
					else
					{
						ApplyFrameProps(tpi, importData3.FrameData, sprite3.Textures.Count);
					}
					sprite3.Textures.Add(new GameMakerSprite.TextureEntry
					{
						Texture = tpi
					});
				}
			}
			handledFolders.Add(importData3.FolderPath);
			updated++;
		}
		Log($"[ImportSprites] Phase 1b: {updated} existing sprites updated");
		ReportProgress(25, 100);
		List<string> foldersWithPngs;
		try
		{
			foldersWithPngs = [.. from d in importDataList
							   where GetFilesIn(d.FolderPath, "*.png").Length != 0
							   where SpriteFolderRequiresRepack(d.FolderPath, d.Name)
							   select d.FolderPath];
		}
		finally
		{
			foreach (var value in atlasImageCache.Values)
			{
				value.Dispose();
			}
			atlasImageCache.Clear();
		}
		Log($"[ImportSprites] {foldersWithPngs.Count} sprite folders with PNGs need texture repacking");
		ReportProgress(35, 100);
		if (foldersWithPngs.Count == 0)
		{
			Log("[ImportSprites] All sprites handled via direct metadata import (no repacking needed)");
			ReportProgress(100, 100);
			ReportSprites(null);
			return;
		}
		var logicalNamesByFolder = importDataList.ToDictionary(spriteImportData => Path.GetFullPath(spriteImportData.FolderPath), spriteImportData => spriteImportData.Name, StringComparer.OrdinalIgnoreCase);
		try
		{
			DoTextureRepack(data, foldersWithPngs, logicalNamesByFolder, LoadMeta, LoadFrameData, ApplyFrameProps, FindSprite, MakeSpriteString, lookup);
		}
		catch (Exception repackEx) when (BatchRun.Active && !ModdingCancellation.IsCancellation(repackEx))
		{
			ReportSprites(repackEx);
			return;
		}
		ReportSprites(null);
		static void ApplyFrameProps(GameMakerTexturePageItem gameMakerTexturePageItem, List<Dictionary<string, object>> list, int frameIndex)
		{
			if (frameIndex < list.Count)
			{
				var frame2 = list[frameIndex];
				if (!frame2.TryGetValue("isNull", out var isNullVal) || !(bool)isNullVal)
				{
					if (frame2.TryGetValue("targetX", out var txVal))
					{
						gameMakerTexturePageItem.TargetX = (ushort)txVal;
					}
					if (frame2.TryGetValue("targetY", out var tyVal))
					{
						gameMakerTexturePageItem.TargetY = (ushort)tyVal;
					}
					if (frame2.TryGetValue("targetWidth", out var twVal))
					{
						gameMakerTexturePageItem.TargetWidth = (ushort)twVal;
					}
					if (frame2.TryGetValue("targetHeight", out var thVal))
					{
						gameMakerTexturePageItem.TargetHeight = (ushort)thVal;
					}
					if (frame2.TryGetValue("boundingWidth", out var bwVal))
					{
						gameMakerTexturePageItem.BoundingWidth = (ushort)bwVal;
					}
					if (frame2.TryGetValue("boundingHeight", out var bhVal))
					{
						gameMakerTexturePageItem.BoundingHeight = (ushort)bhVal;
					}
				}
			}
		}
		static void ApplyOptionalFrameFields(GameMakerTexturePageItem gameMakerTexturePageItem, Dictionary<string, object> dictionary)
		{
			if (dictionary.TryGetValue("targetX", out var txv))
			{
				gameMakerTexturePageItem.TargetX = (ushort)Convert.ToInt32(txv, CultureInfo.InvariantCulture);
			}
			if (dictionary.TryGetValue("targetY", out var tyv))
			{
				gameMakerTexturePageItem.TargetY = (ushort)Convert.ToInt32(tyv, CultureInfo.InvariantCulture);
			}
			if (dictionary.TryGetValue("targetWidth", out var twv))
			{
				gameMakerTexturePageItem.TargetWidth = (ushort)Convert.ToInt32(twv, CultureInfo.InvariantCulture);
			}
			if (dictionary.TryGetValue("targetHeight", out var thv))
			{
				gameMakerTexturePageItem.TargetHeight = (ushort)Convert.ToInt32(thv, CultureInfo.InvariantCulture);
			}
			if (dictionary.TryGetValue("boundingWidth", out var bwv))
			{
				gameMakerTexturePageItem.BoundingWidth = (ushort)Convert.ToInt32(bwv, CultureInfo.InvariantCulture);
			}
			if (dictionary.TryGetValue("boundingHeight", out var bhv))
			{
				gameMakerTexturePageItem.BoundingHeight = (ushort)Convert.ToInt32(bhv, CultureInfo.InvariantCulture);
			}
		}
		GameMakerTexturePageItem? FindExistingTPI(int num, ushort num2, ushort num3, ushort num4, ushort num5)
		{
			if (num < 0 || num >= data.EmbeddedTextures.Count)
			{
				return null;
			}
			var texturePage = data.EmbeddedTextures[num];
			foreach (var tpi2 in data.TexturePageItems)
			{
				if (tpi2.TexturePage == texturePage && tpi2.SourceX == num2 && tpi2.SourceY == num3 && tpi2.SourceWidth == num4 && tpi2.SourceHeight == num5)
				{
					return tpi2;
				}
			}
			return null;
		}
		GameMakerSprite? FindSprite(string name)
		{
			return lookup.TryGet(name, out GameMakerSprite sprite4, out _) ? sprite4 : null;
		}
		List<Dictionary<string, object>>? LoadFrameData(string text)
		{
			if (textureFrameCache.TryGetValue(text, out var cached))
			{
				return cached;
			}
			var meta2 = LoadMeta(text);
			if (meta2.HasValue && meta2.Value.TryGetProperty("textureFrames", out var framesElm) && framesElm.ValueKind == JsonValueKind.Array)
			{
				List<Dictionary<string, object>> frames = [];
				foreach (var frameElm in framesElm.EnumerateArray())
				{
					Dictionary<string, object> frame2 = [];
					if (frameElm.TryGetProperty("frameIndex", out var idxElm))
					{
						frame2["frameIndex"] = idxElm.GetInt32();
					}
					if (frameElm.TryGetProperty("isNull", out var nullElm))
					{
						frame2["isNull"] = nullElm.GetBoolean();
					}
					if (frameElm.TryGetProperty("texturePageIndex", out var tpIdxElm))
					{
						frame2["texturePageIndex"] = tpIdxElm.GetInt32();
					}
					if (frameElm.TryGetProperty("sourceX", out var sxElm))
					{
						frame2["sourceX"] = (ushort)sxElm.GetInt32();
					}
					if (frameElm.TryGetProperty("sourceY", out var syElm))
					{
						frame2["sourceY"] = (ushort)syElm.GetInt32();
					}
					if (frameElm.TryGetProperty("sourceWidth", out var swElm))
					{
						frame2["sourceWidth"] = (ushort)swElm.GetInt32();
					}
					if (frameElm.TryGetProperty("sourceHeight", out var shElm))
					{
						frame2["sourceHeight"] = (ushort)shElm.GetInt32();
					}
					if (frameElm.TryGetProperty("targetX", out var txElm))
					{
						frame2["targetX"] = (ushort)txElm.GetInt32();
					}
					if (frameElm.TryGetProperty("targetY", out var tyElm))
					{
						frame2["targetY"] = (ushort)tyElm.GetInt32();
					}
					if (frameElm.TryGetProperty("targetWidth", out var twElm))
					{
						frame2["targetWidth"] = (ushort)twElm.GetInt32();
					}
					if (frameElm.TryGetProperty("targetHeight", out var thElm))
					{
						frame2["targetHeight"] = (ushort)thElm.GetInt32();
					}
					if (frameElm.TryGetProperty("boundingWidth", out var bwElm))
					{
						frame2["boundingWidth"] = (ushort)bwElm.GetInt32();
					}
					if (frameElm.TryGetProperty("boundingHeight", out var bhElm))
					{
						frame2["boundingHeight"] = (ushort)bhElm.GetInt32();
					}
					frames.Add(frame2);
				}
				textureFrameCache[text] = frames;
				return frames;
			}
			textureFrameCache[text] = null;
			return null;
		}
		JsonElement? LoadMeta(string text)
		{
			if (spriteMetadataCache.TryGetValue(text, out var cached))
			{
				return cached;
			}
			var metaFile = Path.Combine(inputDir, text, text + ".json");
			if (!FExists(metaFile))
			{
				metaFile = Path.Combine(inputDir, text, "sprite_meta.json");
			}
			if (!FExists(metaFile))
			{
				spriteMetadataCache[text] = null;
				return null;
			}
			try
			{
				var doc = JsonDocument.Parse(FReadText(metaFile));
				spriteMetadataCache[text] = doc.RootElement.Clone();
				return spriteMetadataCache[text];
			}
			catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
			{
				spriteMetadataCache[text] = null;
				metaFailed.Add(text);
				BatchRun.Failed(text, ex);
				return null;
			}
		}
		GameMakerString MakeSpriteString(string content)
		{
			return lookup.GetOrAddString(content);
		}
		void ReportSprites(Exception? repackError)
		{
			foreach (var d in importDataList)
			{
				if (repackError != null && foldersWithPngs.Contains(d.FolderPath))
				{
					BatchRun.Failed(d.Name, repackError);
				}
				else if (handledFolders.Contains(d.FolderPath) || foldersWithPngs.Contains(d.FolderPath))
				{
					BatchRun.Item(d.Name);
				}
				else
				{
					BatchRun.Item(d.Name, ResultKind.Skipped, "No texture data to import");
				}
			}
		}
		bool SpriteFolderRequiresRepack(string folderPath, string text)
		{
			string[] pngFiles = [.. GetFilesIn(folderPath, "*.png").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
			if (pngFiles.Length == 0)
			{
				return false;
			}
			if (ResourceAssetUtil.IsLocaleArtSpriteName(text))
			{
				Log("[ImportSprites] Repack required for " + text + ": locale art PNG payload must not be relinked to a shared texture page");
				return true;
			}
			var existingSprite = FindSprite(text);
			if (existingSprite == null)
			{
				Log("[ImportSprites] Repack required for " + text + ": sprite does not exist yet");
				return true;
			}
			var nonNullTextureCount = existingSprite.Textures.Count(te => te?.Texture != null);
			if (nonNullTextureCount != pngFiles.Length)
			{
				Log($"[ImportSprites] Repack required for {text}: texture count mismatch existing={nonNullTextureCount}, pngs={pngFiles.Length}");
				return true;
			}
			for (var frameIndex = 0; frameIndex < pngFiles.Length; frameIndex++)
			{
				if (frameIndex >= existingSprite.Textures.Count)
				{
					Log($"[ImportSprites] Repack required for {text}: frame index {frameIndex} out of range");
					return true;
				}
				var tpi2 = existingSprite.Textures[frameIndex]?.Texture;
				var texturePage = tpi2?.TexturePage;
				if (tpi2 == null || texturePage?.TextureData?.Image == null)
				{
					Log($"[ImportSprites] Repack required for {text}: missing existing texture page item for frame {frameIndex}");
					return true;
				}
				try
				{
					if (!atlasImageCache.TryGetValue(texturePage, out var sourceAtlas))
					{
						sourceAtlas = new MagickImage(texturePage.TextureData.Image.ConvertToPng().GetData());
						atlasImageCache[texturePage] = sourceAtlas;
					}
					using var sourceFrame = sourceAtlas.CloneArea(tpi2.SourceX, tpi2.SourceY, tpi2.SourceWidth, tpi2.SourceHeight);
					using var importFrame = new MagickImage(FReadBytes(pngFiles[frameIndex]));
					if (sourceFrame.Width != importFrame.Width || sourceFrame.Height != importFrame.Height)
					{
						Log($"[ImportSprites] Repack required for {text}: frame {frameIndex} size mismatch atlas={sourceFrame.Width}x{sourceFrame.Height}, import={importFrame.Width}x{importFrame.Height}");
						return true;
					}
					var array2 = sourceFrame.GetPixels().ToByteArray(PixelMapping.RGBA);
					if (!MemoryExtensions.SequenceEqual(other: (ReadOnlySpan<byte>)importFrame.GetPixels().ToByteArray(PixelMapping.RGBA), span: array2.AsSpan()))
					{
						Log($"[ImportSprites] Repack required for {text}: frame {frameIndex} pixel mismatch");
						return true;
					}
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					Log($"[ImportSprites] Repack required for {text}: comparison failed for frame {frameIndex}: {ex.Message}");
					return true;
				}
			}
			Log("[ImportSprites] Repack skipped for " + text + ": imported PNGs match existing texture slices");
			return false;
		}
	}

	private static GameMakerSprite? CreateSpriteFromMetadata(GameMakerData data, SpriteImportData importData, Func<int, ushort, ushort, ushort, ushort, GameMakerTexturePageItem?> findExistingTPI, Func<string, GameMakerString> makeString)
	{
		if (importData.FrameData == null)
		{
			return null;
		}
		var sprite = new GameMakerSprite
		{
			Name = makeString(importData.Name)
		};
		if (importData.Meta.HasValue)
		{
			ApplySpriteMetadata(data, sprite, importData.Meta.Value);
		}
		foreach (var frame in importData.FrameData)
		{
			if (frame.TryGetValue("isNull", out var isNull) && (bool)isNull)
			{
				sprite.Textures.Add(new GameMakerSprite.TextureEntry
				{
					Texture = null
				});
				continue;
			}
			var texturePageIndex = frame.TryGetValue("texturePageIndex", out var tpIdx) ? Convert.ToInt32(tpIdx, CultureInfo.InvariantCulture) : (-1);
			if (texturePageIndex < 0 || texturePageIndex >= data.EmbeddedTextures.Count)
			{
				return null;
			}
			var sourceX = (ushort)(frame.TryGetValue("sourceX", out var sxv) ? ((ushort)Convert.ToInt32(sxv, CultureInfo.InvariantCulture)) : 0);
			var sourceY = (ushort)(frame.TryGetValue("sourceY", out var syv) ? ((ushort)Convert.ToInt32(syv, CultureInfo.InvariantCulture)) : 0);
			var sourceWidth = (ushort)(frame.TryGetValue("sourceWidth", out var swv) ? ((ushort)Convert.ToInt32(swv, CultureInfo.InvariantCulture)) : 0);
			var sourceHeight = (ushort)(frame.TryGetValue("sourceHeight", out var shv) ? ((ushort)Convert.ToInt32(shv, CultureInfo.InvariantCulture)) : 0);
			var tpi = findExistingTPI(texturePageIndex, sourceX, sourceY, sourceWidth, sourceHeight);
			if (tpi == null)
			{
				tpi = new GameMakerTexturePageItem
				{
					Name = new GameMakerString($"PageItem {data.TexturePageItems.Count}"),
					TexturePage = data.EmbeddedTextures[texturePageIndex],
					SourceX = sourceX,
					SourceY = sourceY,
					SourceWidth = sourceWidth,
					SourceHeight = sourceHeight
				};
				if (frame.TryGetValue("targetX", out var txv))
				{
					tpi.TargetX = (ushort)Convert.ToInt32(txv, CultureInfo.InvariantCulture);
				}
				if (frame.TryGetValue("targetY", out var tyv))
				{
					tpi.TargetY = (ushort)Convert.ToInt32(tyv, CultureInfo.InvariantCulture);
				}
				if (frame.TryGetValue("targetWidth", out var twv))
				{
					tpi.TargetWidth = (ushort)Convert.ToInt32(twv, CultureInfo.InvariantCulture);
				}
				if (frame.TryGetValue("targetHeight", out var thv))
				{
					tpi.TargetHeight = (ushort)Convert.ToInt32(thv, CultureInfo.InvariantCulture);
				}
				if (frame.TryGetValue("boundingWidth", out var bwv))
				{
					tpi.BoundingWidth = (ushort)Convert.ToInt32(bwv, CultureInfo.InvariantCulture);
				}
				if (frame.TryGetValue("boundingHeight", out var bhv))
				{
					tpi.BoundingHeight = (ushort)Convert.ToInt32(bhv, CultureInfo.InvariantCulture);
				}
				data.TexturePageItems.Add(tpi);
			}
			sprite.Textures.Add(new GameMakerSprite.TextureEntry
			{
				Texture = tpi
			});
		}
		return sprite;
	}

	private static void ApplySpriteMetadata(GameMakerData data, GameMakerSprite sprite, JsonElement root)
	{
		if (root.TryGetProperty("width", out var wElm))
		{
			sprite.Width = (uint)wElm.GetInt64();
		}
		if (root.TryGetProperty("height", out var hElm))
		{
			sprite.Height = (uint)hElm.GetInt64();
		}
		if (root.TryGetProperty("originX", out var oxElm))
		{
			sprite.OriginX = oxElm.GetInt32();
		}
		if (root.TryGetProperty("originY", out var oyElm))
		{
			sprite.OriginY = oyElm.GetInt32();
		}
		if (root.TryGetProperty("marginLeft", out var mlElm))
		{
			sprite.MarginLeft = mlElm.GetInt32();
		}
		if (root.TryGetProperty("marginRight", out var mrElm))
		{
			sprite.MarginRight = mrElm.GetInt32();
		}
		if (root.TryGetProperty("marginTop", out var mtElm))
		{
			sprite.MarginTop = mtElm.GetInt32();
		}
		if (root.TryGetProperty("marginBottom", out var mbElm))
		{
			sprite.MarginBottom = mbElm.GetInt32();
		}
		if (root.TryGetProperty("transparent", out var trElm))
		{
			sprite.Transparent = trElm.GetBoolean();
		}
		if (root.TryGetProperty("smooth", out var smthElm))
		{
			sprite.Smooth = smthElm.GetBoolean();
		}
		if (root.TryGetProperty("preload", out var plElm))
		{
			sprite.Preload = plElm.GetBoolean();
		}
		if (root.TryGetProperty("bboxMode", out var bmElm))
		{
			sprite.BBoxMode = (uint)bmElm.GetInt64();
		}
		var hasCollisionMasks = root.TryGetProperty("collisionMasks", out var cmElm) && cmElm.ValueKind == JsonValueKind.Array;
		if (root.TryGetProperty("sepMasks", out var smElm))
		{
			var sepMasks = (GameMakerSprite.SepMaskType)smElm.GetInt32();
			if (!hasCollisionMasks && sprite.SepMasks != sepMasks)
			{
				sprite.CollisionMasks.Clear();
			}
			sprite.SepMasks = sepMasks;
		}
		if (data.IsGameMaker2())
		{
			if (root.TryGetProperty("isSpecialType", out var istElm))
			{
				sprite.IsSpecialType = istElm.GetBoolean();
			}
			if (root.TryGetProperty("sVersion", out var svElm))
			{
				sprite.SVersion = (uint)svElm.GetInt64();
			}
			if (root.TryGetProperty("sSpriteType", out var stElm))
			{
				sprite.SSpriteType = (GameMakerSprite.SpriteType)stElm.GetInt32();
			}
			if (root.TryGetProperty("spineVersion", out var spineVersionElm) && sprite.IsSpineSprite)
			{
				sprite.SpineVersion = spineVersionElm.GetInt32();
			}
			if (root.TryGetProperty("swfVersion", out var swfVersionElm) && sprite.IsYYSWFSprite)
			{
				sprite.SWFVersion = swfVersionElm.GetInt32();
			}
			if (root.TryGetProperty("gms2PlaybackSpeed", out var psElm))
			{
				sprite.GMS2PlaybackSpeed = (float)psElm.GetDouble();
			}
			if (root.TryGetProperty("gms2PlaybackSpeedType", out var pstElm))
			{
				sprite.GMS2PlaybackSpeedType = (AnimSpeedType)pstElm.GetInt32();
			}
		}
		if (hasCollisionMasks)
		{
			sprite.CollisionMasks.Clear();
			foreach (var maskElm in cmElm.EnumerateArray())
			{
				var mask = new GameMakerSprite.MaskEntry();
				if (maskElm.TryGetProperty("width", out var mwElm))
				{
					mask.Width = (int)mwElm.GetInt64();
				}
				if (maskElm.TryGetProperty("height", out var mhElm))
				{
					mask.Height = (int)mhElm.GetInt64();
				}
				if (maskElm.TryGetProperty("data", out var dataElm))
				{
					var base64 = dataElm.GetString();
					if (!string.IsNullOrEmpty(base64))
					{
						mask.Data = Convert.FromBase64String(base64);
					}
				}
				sprite.CollisionMasks.Add(mask);
			}
		}
		if (!root.TryGetProperty("nineSlice", out var nsElm) || nsElm.ValueKind != JsonValueKind.Object)
		{
			return;
		}
		var ns = sprite.V3NineSlice = new GameMakerSprite.NineSlice();
		if (nsElm.TryGetProperty("left", out var nlElm))
		{
			ns.Left = nlElm.GetInt32();
		}
		if (nsElm.TryGetProperty("top", out var ntElm))
		{
			ns.Top = ntElm.GetInt32();
		}
		if (nsElm.TryGetProperty("right", out var nrElm))
		{
			ns.Right = nrElm.GetInt32();
		}
		if (nsElm.TryGetProperty("bottom", out var nbElm))
		{
			ns.Bottom = nbElm.GetInt32();
		}
		if (nsElm.TryGetProperty("enabled", out var neElm))
		{
			ns.Enabled = neElm.GetBoolean();
		}
		if (nsElm.TryGetProperty("tileModes", out var nmElm) && nmElm.ValueKind == JsonValueKind.Array)
		{
			JsonElement[] modesArray = [.. nmElm.EnumerateArray()];
			for (var i = 0; i < Math.Min(5, modesArray.Length); i++)
			{
				ns.TileModes[i] = (GameMakerSprite.NineSlice.TileMode)modesArray[i].GetInt32();
			}
		}
	}

	[GeneratedRegex("^(.+?)(?:_(\\d+))$", RegexOptions.Compiled)]
	private static partial Regex SprFrameRegex();

	private static void DoTextureRepack(GameMakerData data, List<string> newSpriteFolders, Dictionary<string, string> logicalNamesByFolder, Func<string, JsonElement?> loadMeta, Func<string, List<Dictionary<string, object>>?> loadFrameData, Action<GameMakerTexturePageItem, List<Dictionary<string, object>>, int> applyFrameProps, Func<string, GameMakerSprite?> findSprite, Func<string, GameMakerString> makeString, DataLookup lookup)
	{
		var noMasksForBasicRectangles = data.IsVersionAtLeast(2022u, 9u);
		const int atlasSize = 2048;
		const int padding = 2;
		List<MagickImage> imagesToCleanup = [];
		Dictionary<GameMakerSprite, PlacedFrame> maskNodes = [];
		try
		{
			string[] pngFiles = [.. newSpriteFolders.SelectMany(folder => GetFilesIn(folder, "*.png")).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
			Log($"[ImportSprites] Prepared {pngFiles.Length} PNG files from {newSpriteFolders.Count} sprite folders for repacking");
			ReportProgress(5, 100);
			if (pngFiles.Length == 0)
			{
				Log("[ImportSprites] No PNGs to repack");
				return;
			}
			var packer = new TextureGroupPacker(atlasSize, padding, false, GMImage.ImageFormat.RawBgra);
			// Frames that fill a whole page cannot take a border; the old packer placed them on a page of their own.
			var wholePagePacker = new TextureGroupPacker(atlasSize, 0, false, GMImage.ImageFormat.RawBgra);
			var frames = new List<PlacedFrame>(pngFiles.Length);
			for (var fileIndex = 0; fileIndex < pngFiles.Length; fileIndex++)
			{
				var img = (GetPatchFileSystem() != null) ? new MagickImage(FReadBytes(pngFiles[fileIndex])) : new MagickImage(pngFiles[fileIndex]);
				if (img.Width > atlasSize || img.Height > atlasSize)
				{
					img.Dispose();
					continue;
				}
				imagesToCleanup.Add(img);
				var wholePage = img.Width + padding * 2 > atlasSize || img.Height + padding * 2 > atlasSize;
				frames.Add(new PlacedFrame(pngFiles[fileIndex], wholePage ? wholePagePacker.AddImage(img, TextureGroupPacker.BorderFlags.None, false) : packer.AddImage(img, TextureGroupPacker.BorderFlags.Enabled, false)));
				ReportProgress(fileIndex + 1, pngFiles.Length);
			}
			packer.PackPages();
			wholePagePacker.PackPages();
			ReportProgress(50, 100);
			packer.ImportToData(data);
			wholePagePacker.ImportToData(data);
			var newCreated = 0;
			var atlasIndex = 0;
			List<IGrouping<GameMakerEmbeddedTexture, PlacedFrame>> pages = [.. frames.GroupBy(frame => frame.Item.TexturePage)];
			var atlasTotal = Math.Max(pages.Count, 1);
			var encodeSlots = new SemaphoreSlim(Environment.ProcessorCount);
			var encodes = new List<Task>(pages.Count);
			foreach (var page in pages)
			{
				// Pages stay raw until the masks are built, then each is encoded once.
				var rawPage = page.Key.TextureData.Image;
				var pageWidth = rawPage.Width;
				foreach (var n in page)
				{
					int boundingWidth = n.Item.BoundingWidth;
					int boundingHeight = n.Item.BoundingHeight;
					int frameWidth = n.Item.SourceWidth;
					int frameHeight = n.Item.SourceHeight;
					var stripped = Path.GetFileNameWithoutExtension(Path.GetFileName(n.Source));
					var sourceFolder = Path.GetDirectoryName(n.Source);
					string? logicalName = null;
					var hasLogicalName = sourceFolder != null && logicalNamesByFolder.TryGetValue(Path.GetFullPath(sourceFolder), out logicalName);
					var spriteName = hasLogicalName ? logicalName : string.Empty;
					var frame = 0;
					try
					{
						var match = SprFrameRegex().Match(stripped);
						if (!match.Success)
						{
							continue;
						}
						if (!hasLogicalName)
						{
							spriteName = match.Groups[1].Value;
						}
						if (!int.TryParse(match.Groups[2].Value, out frame))
						{
							frame = 0;
						}
						goto IL_02c6;
					}
					catch
					{
					}
					continue;
				IL_02c6:
					var existingSprite = findSprite(spriteName);
					var tpi = n.Item;
					var texEntry = new GameMakerSprite.TextureEntry
					{
						Texture = tpi
					};
					var frameData = loadFrameData(spriteName);
					if (frameData != null)
					{
						applyFrameProps(tpi, frameData, frame);
					}
					GameMakerSprite sprite;
					if (existingSprite == null)
					{
						sprite = new GameMakerSprite
						{
							Name = makeString(spriteName),
							Width = (uint)boundingWidth,
							Height = (uint)boundingHeight,
							MarginLeft = 0,
							MarginRight = frameWidth - 1,
							MarginTop = 0,
							MarginBottom = frameHeight - 1,
							OriginX = 0,
							OriginY = 0
						};
						var meta = loadMeta(spriteName);
						if (meta.HasValue)
						{
							ApplySpriteMetadata(data, sprite, meta.Value);
						}
						for (var i = 0; i < frame; i++)
						{
							sprite.Textures.Add(new GameMakerSprite.TextureEntry
							{
								Texture = null
							});
						}
						var flag = ShouldAutoGenerateCollisionMask(meta);
						if (flag)
						{
							var flag2 = !noMasksForBasicRectangles;
							if (!flag2)
							{
								var sepMasks = sprite.SepMasks;
								var flag3 = sepMasks is GameMakerSprite.SepMaskType.AxisAlignedRect or GameMakerSprite.SepMaskType.RotatedRect;
								flag2 = !flag3;
							}
							flag = flag2;
						}
						if (flag && sprite.CollisionMasks.Count == 0)
						{
							maskNodes[sprite] = n;
						}
						sprite.Textures.Add(texEntry);
						lookup.Add(sprite);
						newCreated++;
					}
					else
					{
						sprite = existingSprite;
						if (frame >= sprite.Textures.Count)
						{
							while (frame >= sprite.Textures.Count)
							{
								sprite.Textures.Add(new GameMakerSprite.TextureEntry
								{
									Texture = null
								});
							}
						}
						var oldTex = sprite.Textures[frame]?.Texture ?? sprite.Textures.FirstOrDefault(te => te != null)?.Texture;
						if (oldTex != null)
						{
							tpi.TargetX = oldTex.TargetX;
							tpi.TargetY = oldTex.TargetY;
							tpi.TargetWidth = oldTex.TargetWidth;
							tpi.TargetHeight = oldTex.TargetHeight;
							tpi.BoundingWidth = oldTex.BoundingWidth;
							tpi.BoundingHeight = oldTex.BoundingHeight;
						}
						if (frameData != null)
						{
							applyFrameProps(tpi, frameData, frame);
						}
						var existMeta = loadMeta(spriteName);
						if (existMeta.HasValue)
						{
							ApplySpriteMetadata(data, sprite, existMeta.Value);
						}
						sprite.Textures[frame] = texEntry;
					}
					if (ShouldAutoGenerateCollisionMask(loadMeta(spriteName)) && sprite.SepMasks == GameMakerSprite.SepMaskType.Precise && sprite.CollisionMasks.Count == 0)
					{
						maskNodes[sprite] = n;
					}
				}
				foreach (var (maskSpr, maskNode) in maskNodes)
				{
					try
					{
						maskSpr.CollisionMasks.Clear();
						maskSpr.CollisionMasks.Add(maskSpr.NewMaskEntry(data));
						var (mw, mh) = maskSpr.CalculateMaskDimensions(data);
						if (mw <= 0 || mh <= 0)
						{
							maskSpr.CollisionMasks.Clear();
							continue;
						}
						var maskItem = maskNode.Item;
						var region = new byte[maskItem.SourceWidth * maskItem.SourceHeight * 4];
						for (var row = 0; row < maskItem.SourceHeight; row++)
						{
							rawPage.GetRawImageData().Slice(((maskItem.SourceY + row) * pageWidth + maskItem.SourceX) * 4, maskItem.SourceWidth * 4).CopyTo(region.AsSpan(row * maskItem.SourceWidth * 4));
						}
						var bytes = MaskBuilder.Build(region, maskItem.SourceWidth, maskItem.SourceHeight, mw, mh);
						if (maskSpr.CollisionMasks[0].Data.Length == bytes.Length)
						{
							Array.Copy(bytes, maskSpr.CollisionMasks[0].Data, bytes.Length);
						}
						else
						{
							maskSpr.CollisionMasks.Clear();
						}
					}
					catch
					{
						maskSpr.CollisionMasks.Clear();
					}
				}
				maskNodes.Clear();
				encodeSlots.Wait();
				var encodePage = page.Key;
				encodes.Add(Task.Run(() =>
				{
					try
					{
						encodePage.TextureData.Image = rawPage.ConvertToPng();
					}
					finally
					{
						encodeSlots.Release();
					}
				}));
				atlasIndex++;
				ReportProgress(50 + atlasIndex * 45 / atlasTotal, 100);
			}
			Task.WaitAll([.. encodes]);
			ReportProgress(100, 100);
			Log($"[ImportSprites] Repack done. New from repack: {newCreated}");
		}
		catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
		{
			Log("[ImportSprites] REPACK ERROR: " + ex.Message);
			throw;
		}
		finally
		{
			foreach (var item in imagesToCleanup)
			{
				item.Dispose();
			}
		}
	}
}
