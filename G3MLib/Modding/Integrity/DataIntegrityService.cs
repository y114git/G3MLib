using System;
using System.Collections.Generic;
using System.Linq;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Modding.Integrity;

public static class DataIntegrityService
{
	public static DataIntegrityResult RepairAndValidate(GameMakerData data)
	{
		DataIntegrityResult result = new DataIntegrityResult();
		RepairScripts(data, result);
		RepairCodeParentLinks(data, result);
		RepairCodeSerializationOrder(data, result);
		RepairRoomCodeReferences(data, result);
		RepairGeneralInfoBranch(data, result);
		RepairLocalChildFunctionReferences(data, result);
		RepairFunctionReferences(data, result);
		RepairSoundAudioReferences(data, result);
		Validate(data, result);
		if (result.Repairs.Count > 0)
		{
			bool shouldAppend;
			LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(21, 2, out shouldAppend);
			if (shouldAppend)
			{
				handler.AppendLiteral("[Integrity] Repairs: ");
				handler.AppendFormatted(string.Join("; ", result.Repairs.Take(12)));
				handler.AppendFormatted((result.Repairs.Count > 12) ? " ..." : "");
			}
			LogService.Log(ref handler);
		}
		if (result.Errors.Count > 0)
		{
			LogService.Warning("[Integrity] Errors: " + string.Join("; ", result.Errors.Take(12)) + ((result.Errors.Count > 12) ? " ..." : ""));
		}
		return result;
	}

	public static DataIntegrityResult ValidateOnly(GameMakerData data)
	{
		DataIntegrityResult result = new DataIntegrityResult();
		Validate(data, result);
		if (result.Errors.Count > 0)
		{
			LogService.Warning("[Integrity] Errors: " + string.Join("; ", result.Errors.Take(12)) + ((result.Errors.Count > 12) ? " ..." : ""));
		}
		return result;
	}

	private static void RepairGeneralInfoBranch(GameMakerData data, DataIntegrityResult result)
	{
		GameMakerGeneralInfo gi = data.GeneralInfo;
		if (gi != null && gi.BytecodeVersion == 17 && gi.Major == 2023 && gi.Minor == 6 && gi.Release == 0 && gi.Build == 0 && gi.Branch != GameMakerGeneralInfo.BranchType.LTS2022_0)
		{
			gi.Branch = GameMakerGeneralInfo.BranchType.LTS2022_0;
			result.Repairs.Add("restored GeneralInfo branch to LTS2022_0 for 2023.6.0.0 bytecode-17 data");
		}
	}

	private static void RepairSoundAudioReferences(GameMakerData data, DataIntegrityResult result)
	{
		if (data.Sounds == null || data.EmbeddedAudio == null)
		{
			return;
		}
		int builtinGroupId = data.GetBuiltinSoundGroupID();
		int relinkedBuiltin = 0;
		int clearedExternal = 0;
		int clearedEmptyTypes = 0;
		foreach (GameMakerSound sound in data.Sounds)
		{
			if (sound == null)
			{
				continue;
			}
			if (sound.Type != null && string.IsNullOrEmpty(sound.Type.Content))
			{
				sound.Type = null;
				clearedEmptyTypes++;
			}
			if (sound.GroupID == builtinGroupId || sound.GroupID <= 0)
			{
				if (sound.AudioFile == null && sound.AudioID >= 0 && sound.AudioID < data.EmbeddedAudio.Count)
				{
					sound.AudioFile = data.EmbeddedAudio[sound.AudioID];
					relinkedBuiltin++;
				}
			}
			else if (sound.AudioFile != null)
			{
				sound.AudioFile = null;
				clearedExternal++;
			}
		}
		if (relinkedBuiltin > 0)
		{
			result.Repairs.Add($"relinked {relinkedBuiltin} builtin Sound audio reference(s)");
		}
		if (clearedExternal > 0)
		{
			result.Repairs.Add($"cleared {clearedExternal} external Sound audio pointer(s)");
		}
		if (clearedEmptyTypes > 0)
		{
			result.Repairs.Add($"cleared {clearedEmptyTypes} empty Sound type reference(s)");
		}
	}

	private static void RepairFunctionReferences(GameMakerData data, DataIntegrityResult result)
	{
		if (data.Functions == null)
		{
			return;
		}
		HashSet<GameMakerFunction> functionSet = new HashSet<GameMakerFunction>(data.Functions);
		int relinked = 0;
		int restored = 0;
		HashSet<GameMakerFunction> referencedFunctions = new HashSet<GameMakerFunction>();
		foreach (GameMakerCode item in data.Code ?? new List<GameMakerCode>())
		{
			foreach (GameMakerInstruction instruction in item?.Instructions ?? new List<GameMakerInstruction>())
			{
				GameMakerFunction function = instruction.ValueFunction;
				if (function == null)
				{
					continue;
				}
				string name = function.Name?.Content;
				if (string.IsNullOrEmpty(name))
				{
					continue;
				}
				string canonicalName = FunctionNameUtil.CanonicalizeFunctionName(name);
				GameMakerFunction replacement = null;
				if (functionSet.Contains(function) && string.Equals(name, canonicalName, StringComparison.Ordinal))
				{
					replacement = function;
				}
				else
				{
					replacement = data.Functions.ByName(canonicalName);
					if (replacement == null)
					{
						replacement = data.Functions.Define(canonicalName, data.Strings);
						functionSet.Add(replacement);
						restored++;
					}
					instruction.ValueFunction = replacement;
					relinked++;
				}
				referencedFunctions.Add(replacement);
			}
		}
		HashSet<string> codeNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (GameMakerCode item2 in data.Code ?? new List<GameMakerCode>())
		{
			string codeName = item2?.Name?.Content;
			if (!string.IsNullOrEmpty(codeName))
			{
				codeNames.Add(codeName);
			}
		}
		int removedInvalidFunctions = 0;
		for (int i = data.Functions.Count - 1; i >= 0; i--)
		{
			GameMakerFunction function2 = data.Functions[i];
			string name2 = function2?.Name?.Content;
			if (function2 != null && !referencedFunctions.Contains(function2) && !string.IsNullOrEmpty(name2))
			{
				bool num = !string.Equals(name2, FunctionNameUtil.CanonicalizeFunctionName(name2), StringComparison.Ordinal);
				bool isOrphanScriptFunction = name2.StartsWith("gml_Script_", StringComparison.Ordinal) && !codeNames.Contains(name2);
				if (num || isOrphanScriptFunction)
				{
					data.Functions.RemoveAt(i);
					removedInvalidFunctions++;
				}
			}
		}
		if (restored > 0)
		{
			result.Repairs.Add($"restored {restored} missing Function entries referenced by CodeEntries");
		}
		if (relinked > 0)
		{
			result.Repairs.Add($"relinked {relinked} CodeEntry instruction Function references");
		}
		if (removedInvalidFunctions > 0)
		{
			result.Repairs.Add($"removed {removedInvalidFunctions} invalid or orphan Script Function entries");
		}
	}

	internal static Dictionary<string, GameMakerFunction?> BuildLocalChildFunctionMap(GameMakerCode owner, IReadOnlyDictionary<string, GameMakerFunction> functions)
	{
		Dictionary<string, GameMakerFunction> result = new Dictionary<string, GameMakerFunction>(StringComparer.Ordinal);
		foreach (GameMakerCode item in owner.ChildEntries ?? new List<GameMakerCode>())
		{
			string childName = item?.Name?.Content;
			if (!string.IsNullOrEmpty(childName) && FunctionNameUtil.IsLocalChildFunctionName(childName) && functions.TryGetValue(childName, out GameMakerFunction childFunction))
			{
				string signature = FunctionNameUtil.NormalizeLocalChildFunctionName(childName);
				result[signature] = (result.ContainsKey(signature) ? null : childFunction);
			}
		}
		return result;
	}

	internal static Dictionary<string, GameMakerFunction> BuildLocalChildOrdinalMap(GameMakerData data, GameMakerCode owner, IReadOnlyDictionary<string, GameMakerFunction> functions)
	{
		Dictionary<string, List<(int Ordinal, GameMakerFunction Function)>> liveByFamily = new Dictionary<string, List<(int Ordinal, GameMakerFunction Function)>>(StringComparer.Ordinal);
		foreach (GameMakerCode item in owner.ChildEntries ?? new List<GameMakerCode>())
		{
			string childName = item?.Name?.Content;
			if (!string.IsNullOrEmpty(childName) && functions.TryGetValue(childName, out GameMakerFunction childFunction) && FunctionNameUtil.TryGetLocalChildOrdinalFamily(childName, out string family, out int ordinal))
			{
				if (!liveByFamily.TryGetValue(family, out var list))
				{
					list = (liveByFamily[family] = new List<(int Ordinal, GameMakerFunction Function)>());
				}
				list.Add((ordinal, childFunction));
			}
		}
		Dictionary<string, SortedSet<int>> referencedByFamily = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
		foreach (GameMakerCode code in data.Code ?? new List<GameMakerCode>())
		{
			if ((code?.ParentEntry ?? code) != owner)
			{
				continue;
			}
			foreach (GameMakerInstruction item2 in code?.Instructions ?? new List<GameMakerInstruction>())
			{
				string name = item2.ValueFunction?.Name?.Content;
				if (!string.IsNullOrEmpty(name) && FunctionNameUtil.TryGetLocalChildOrdinalFamily(name, out string family2, out int ordinal2))
				{
					if (!referencedByFamily.TryGetValue(family2, out var ordinals))
					{
						ordinals = (referencedByFamily[family2] = new SortedSet<int>());
					}
					ordinals.Add(ordinal2);
				}
			}
		}
		Dictionary<string, GameMakerFunction> result = new Dictionary<string, GameMakerFunction>(StringComparer.Ordinal);
		foreach (var (family3, referencedOrdinals) in referencedByFamily)
		{
			if (!liveByFamily.TryGetValue(family3, out var liveFunctions))
			{
				continue;
			}
			List<GameMakerFunction> liveOrdered = (from x in liveFunctions
				orderby x.Ordinal
				select x.Function).ToList();
			List<int> referencedOrdered = referencedOrdinals.Order().ToList();
			if (liveOrdered.Count >= referencedOrdered.Count)
			{
				for (int i = 0; i < referencedOrdered.Count; i++)
				{
					result[family3 + "|" + referencedOrdered[i]] = liveOrdered[i];
				}
			}
		}
		return result;
	}

	private static void RepairLocalChildFunctionReferences(GameMakerData data, DataIntegrityResult result)
	{
		if (data.Functions == null || data.Code == null)
		{
			return;
		}
		Dictionary<string, GameMakerCode> codeByName = BuildCodeByName(data);
		Dictionary<string, GameMakerFunction> functionByName = new Dictionary<string, GameMakerFunction>(StringComparer.Ordinal);
		foreach (GameMakerFunction function in data.Functions)
		{
			string name = function?.Name?.Content;
			if (!string.IsNullOrEmpty(name) && function != null && !functionByName.ContainsKey(name))
			{
				functionByName[name] = function;
			}
		}
		Dictionary<GameMakerCode, Dictionary<string, GameMakerFunction?>> localChildMaps = new Dictionary<GameMakerCode, Dictionary<string, GameMakerFunction>>();
		Dictionary<GameMakerCode, Dictionary<string, GameMakerFunction>> localChildOrdinalMaps = new Dictionary<GameMakerCode, Dictionary<string, GameMakerFunction>>();
		int repaired = 0;
		foreach (GameMakerCode code in data.Code)
		{
			if (code == null)
			{
				continue;
			}
			GameMakerCode owner = code.ParentEntry ?? code;
			if (owner.ChildEntries == null || owner.ChildEntries.Count == 0)
			{
				continue;
			}
			Dictionary<string, GameMakerFunction> localMap = GetLocalChildMap(owner);
			if (localMap.Count == 0)
			{
				continue;
			}
			foreach (GameMakerInstruction instruction in code.Instructions ?? new List<GameMakerInstruction>())
			{
				GameMakerFunction currentFunction = instruction.ValueFunction;
				string currentName = currentFunction?.Name?.Content;
				if (!string.IsNullOrEmpty(currentName) && FunctionNameUtil.IsLocalChildFunctionName(currentName) && (!codeByName.TryGetValue(currentName, out var referencedCode) || referencedCode.ParentEntry != owner))
				{
					string signature = FunctionNameUtil.NormalizeLocalChildFunctionName(currentName);
					if ((!localMap.TryGetValue(signature, out var replacement) || replacement == null) && FunctionNameUtil.TryGetLocalChildOrdinalFamily(currentName, out string family, out int ordinal))
					{
						GetLocalChildOrdinalMap(owner).TryGetValue(family + "|" + ordinal, out replacement);
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
			result.Repairs.Add($"relinked {repaired} local child Function references to their owning CodeEntry");
		}
		Dictionary<string, GameMakerFunction?> GetLocalChildMap(GameMakerCode parent)
		{
			if (localChildMaps.TryGetValue(parent, out Dictionary<string, GameMakerFunction> cached))
			{
				return cached;
			}
			return localChildMaps[parent] = BuildLocalChildFunctionMap(parent, functionByName);
		}
		Dictionary<string, GameMakerFunction> GetLocalChildOrdinalMap(GameMakerCode parent)
		{
			if (localChildOrdinalMaps.TryGetValue(parent, out Dictionary<string, GameMakerFunction> cached))
			{
				return cached;
			}
			return localChildOrdinalMaps[parent] = BuildLocalChildOrdinalMap(data, parent, functionByName);
		}
	}

	private static Dictionary<string, GameMakerCode> BuildCodeByName(GameMakerData data)
	{
		Dictionary<string, GameMakerCode> byName = new Dictionary<string, GameMakerCode>(StringComparer.Ordinal);
		foreach (GameMakerCode code in data.Code ?? new List<GameMakerCode>())
		{
			string name = code?.Name?.Content;
			if (!string.IsNullOrEmpty(name) && code != null)
			{
				byName[name] = code;
			}
		}
		return byName;
	}

	private static void RepairCodeParentLinks(GameMakerData data, DataIntegrityResult result)
	{
		HashSet<GameMakerCode> codeSet = new HashSet<GameMakerCode>(data.Code ?? new List<GameMakerCode>());
		int relinkedParents = 0;
		int addedChildren = 0;
		Dictionary<string, GameMakerCode> byName = BuildCodeByName(data);
		foreach (GameMakerCode code in data.Code ?? new List<GameMakerCode>())
		{
			if (code == null)
			{
				continue;
			}
			if (code.ParentEntry != null && !codeSet.Contains(code.ParentEntry))
			{
				string parentName = code.ParentEntry.Name?.Content;
				if (!string.IsNullOrEmpty(parentName) && byName.TryGetValue(parentName, out var liveParent))
				{
					code.ParentEntry = liveParent;
					relinkedParents++;
				}
			}
			if (code.ParentEntry != null && codeSet.Contains(code.ParentEntry) && !code.ParentEntry.ChildEntries.Contains(code))
			{
				code.ParentEntry.ChildEntries.Add(code);
				addedChildren++;
			}
		}
		if (relinkedParents > 0)
		{
			result.Repairs.Add($"relinked {relinkedParents} CodeEntry ParentEntry references by name");
		}
		if (addedChildren > 0)
		{
			result.Repairs.Add($"restored {addedChildren} missing CodeEntry parent child links");
		}
	}

	private static void RepairCodeSerializationOrder(GameMakerData data, DataIntegrityResult result)
	{
		if (data.Code.Count < 2)
		{
			return;
		}
		List<GameMakerCode> original = data.Code.ToList();
		Dictionary<GameMakerCode, int> position = new Dictionary<GameMakerCode, int>(ReferenceEqualityComparer.Instance);
		for (int index = 0; index < original.Count; index++)
		{
			position[original[index]] = index;
		}
		List<GameMakerCode> ordered = new List<GameMakerCode>(original.Count);
		HashSet<GameMakerCode> visited = new HashSet<GameMakerCode>(ReferenceEqualityComparer.Instance);
		foreach (GameMakerCode code in original)
		{
			if (code.ParentEntry == null)
			{
				AddTree(code);
			}
		}
		foreach (GameMakerCode code2 in original)
		{
			AddTree(code2);
		}
		if (ordered.Count != original.Count || ordered.Select((GameMakerCode gameMakerCode, int index2) => gameMakerCode == original[index2]).All((bool equal) => equal))
		{
			return;
		}
		data.Code.Clear();
		foreach (GameMakerCode code3 in ordered)
		{
			data.Code.Add(code3);
		}
		result.Repairs.Add("restored parent-before-child CodeEntry serialization order");
		void AddTree(GameMakerCode gameMakerCode)
		{
			if (!visited.Add(gameMakerCode))
			{
				return;
			}
			ordered.Add(gameMakerCode);
			foreach (GameMakerCode child in from key in gameMakerCode.ChildEntries
				where key != null && key.ParentEntry == gameMakerCode
				orderby position.GetValueOrDefault(key, int.MaxValue)
				select key)
			{
				AddTree(child);
			}
		}
	}

	private static GameMakerCode? LiveCode(Dictionary<string, GameMakerCode> byName, HashSet<GameMakerCode> codeSet, GameMakerCode? code)
	{
		if (code == null)
		{
			return null;
		}
		if (codeSet.Contains(code))
		{
			return code;
		}
		string name = code.Name?.Content;
		if (!string.IsNullOrEmpty(name) && byName.TryGetValue(name, out GameMakerCode live))
		{
			return live;
		}
		return code;
	}

	private static void RepairRoomCodeReferences(GameMakerData data, DataIntegrityResult result)
	{
		HashSet<GameMakerCode> codeSet = new HashSet<GameMakerCode>(data.Code ?? new List<GameMakerCode>());
		Dictionary<string, GameMakerCode> byName = BuildCodeByName(data);
		int relinked = 0;
		foreach (GameMakerRoom room in data.Rooms ?? new List<GameMakerRoom>())
		{
			if (room == null)
			{
				continue;
			}
			GameMakerCode liveCreation = LiveCode(byName, codeSet, room.CreationCodeId);
			if (liveCreation != room.CreationCodeId)
			{
				room.CreationCodeId = liveCreation;
				relinked++;
			}
			foreach (GameMakerRoom.GameObject instance in room.GameObjects ?? new GameMakerPointerList<GameMakerRoom.GameObject>())
			{
				relinked += RepairRoomObjectCodeReferences(instance, byName, codeSet);
			}
			foreach (GameMakerRoom.Layer layer in room.Layers ?? new GameMakerPointerList<GameMakerRoom.Layer>())
			{
				if (layer?.InstancesData?.Instances == null)
				{
					continue;
				}
				foreach (GameMakerRoom.GameObject instance2 in layer.InstancesData.Instances)
				{
					relinked += RepairRoomObjectCodeReferences(instance2, byName, codeSet);
				}
			}
		}
		if (relinked > 0)
		{
			result.Repairs.Add($"relinked {relinked} Room CodeEntry references by name");
		}
	}

	private static int RepairRoomObjectCodeReferences(GameMakerRoom.GameObject? instance, Dictionary<string, GameMakerCode> byName, HashSet<GameMakerCode> codeSet)
	{
		if (instance == null)
		{
			return 0;
		}
		int relinked = 0;
		GameMakerCode liveCreation = LiveCode(byName, codeSet, instance.CreationCode);
		if (liveCreation != instance.CreationCode)
		{
			instance.CreationCode = liveCreation;
			relinked++;
		}
		GameMakerCode livePreCreate = LiveCode(byName, codeSet, instance.PreCreateCode);
		if (livePreCreate != instance.PreCreateCode)
		{
			instance.PreCreateCode = livePreCreate;
			relinked++;
		}
		return relinked;
	}

	private static void RepairScripts(GameMakerData data, DataIntegrityResult result)
	{
		int relinked = 0;
		int created = 0;
		HashSet<GameMakerCode> codeSet = new HashSet<GameMakerCode>(data.Code);
		HashSet<string> scriptNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (GameMakerScript script in data.Scripts)
		{
			if (script == null || script.Name?.Content == null)
			{
				continue;
			}
			scriptNames.Add(script.Name.Content);
			if (script.Code == null || !codeSet.Contains(script.Code))
			{
				GameMakerCode replacement = PatchService.ScriptCodeResolver.Resolve(data, script.Name.Content);
				if (replacement != null)
				{
					script.Code = replacement;
					relinked++;
				}
			}
		}
		foreach (GameMakerCode code in data.Code)
		{
			string codeName = code?.Name?.Content;
			if (!string.IsNullOrEmpty(codeName) && code.ParentEntry == null && codeName.StartsWith("gml_Script_", StringComparison.Ordinal) && !codeName.StartsWith("gml_Script_anon_", StringComparison.Ordinal))
			{
				string scriptName = codeName.Substring("gml_Script_".Length);
				if (!string.IsNullOrEmpty(scriptName) && !scriptNames.Contains(codeName) && scriptNames.Add(scriptName))
				{
					data.Scripts.Add(new GameMakerScript
					{
						Name = data.Strings.MakeString(scriptName),
						Code = code
					});
					created++;
				}
			}
		}
		if (relinked > 0)
		{
			result.Repairs.Add($"relinked {relinked} Script entries to live CodeEntries");
		}
		if (created > 0)
		{
			result.Repairs.Add($"created {created} Script entries for live gml_Script CodeEntries");
		}
	}

	private static void Validate(GameMakerData data, DataIntegrityResult result)
	{
		HashSet<GameMakerCode> codeSet = new HashSet<GameMakerCode>(data.Code ?? new List<GameMakerCode>());
		HashSet<GameMakerFunction> functionSet = new HashSet<GameMakerFunction>(data.Functions ?? new List<GameMakerFunction>());
		HashSet<GameMakerGameObject> objectSet = new HashSet<GameMakerGameObject>(data.GameObjects ?? new List<GameMakerGameObject>());
		HashSet<GameMakerSprite> spriteSet = new HashSet<GameMakerSprite>(data.Sprites ?? new List<GameMakerSprite>());
		HashSet<GameMakerBackground> backgroundSet = new HashSet<GameMakerBackground>(data.Backgrounds ?? new List<GameMakerBackground>());
		HashSet<GameMakerTexturePageItem> texturePageItemSet = new HashSet<GameMakerTexturePageItem>(data.TexturePageItems ?? new List<GameMakerTexturePageItem>());
		HashSet<GameMakerEmbeddedTexture> embeddedTextureSet = new HashSet<GameMakerEmbeddedTexture>(data.EmbeddedTextures ?? new List<GameMakerEmbeddedTexture>());
		HashSet<GameMakerAudioGroup> audioGroupSet = new HashSet<GameMakerAudioGroup>(data.AudioGroups ?? new List<GameMakerAudioGroup>());
		HashSet<GameMakerSequence> sequenceSet = new HashSet<GameMakerSequence>(data.Sequences ?? new List<GameMakerSequence>());
		HashSet<GameMakerParticleSystem> particleSystemSet = new HashSet<GameMakerParticleSystem>(data.ParticleSystems ?? new List<GameMakerParticleSystem>());
		HashSet<GameMakerParticleSystemEmitter> particleEmitterSet = new HashSet<GameMakerParticleSystemEmitter>(data.ParticleSystemEmitters ?? new List<GameMakerParticleSystemEmitter>());
		ValidateScriptCodeFunction(data, codeSet, functionSet, result);
		ValidateObjects(data, codeSet, objectSet, spriteSet, result);
		ValidateTextures(data, spriteSet, texturePageItemSet, embeddedTextureSet, result);
		ValidateSounds(data, audioGroupSet, result);
		ValidateRooms(data, codeSet, objectSet, spriteSet, backgroundSet, sequenceSet, particleSystemSet, result);
		ValidateParticles(data, spriteSet, particleEmitterSet, result);
	}

	private static void ValidateScriptCodeFunction(GameMakerData data, HashSet<GameMakerCode> codeSet, HashSet<GameMakerFunction> functionSet, DataIntegrityResult result)
	{
		HashSet<string> scriptNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (GameMakerScript script in data.Scripts)
		{
			if (script != null && script.Name?.Content != null)
			{
				scriptNames.Add(script.Name.Content);
			}
			if (script?.Code != null && !codeSet.Contains(script.Code))
			{
				result.Errors.Add($"Script '{script.Name?.Content ?? "<null>"}' references missing CodeEntry '{script.Code.Name?.Content ?? "<null>"}'");
			}
		}
		foreach (GameMakerCode code in data.Code)
		{
			string codeName = code?.Name?.Content;
			if (string.IsNullOrEmpty(codeName))
			{
				continue;
			}
			foreach (GameMakerCode child in code.ChildEntries ?? new List<GameMakerCode>())
			{
				if (child == null || !codeSet.Contains(child))
				{
					result.Errors.Add("CodeEntry '" + codeName + "' has child missing from Code list");
				}
				else if (child.ParentEntry != code)
				{
					result.Errors.Add($"CodeEntry '{codeName}' child '{child.Name?.Content ?? "<null>"}' has incorrect ParentEntry");
				}
			}
			if (code.ParentEntry != null)
			{
				if (!codeSet.Contains(code.ParentEntry))
				{
					result.Errors.Add($"CodeEntry '{codeName}' references missing ParentEntry '{code.ParentEntry.Name?.Content ?? "<null>"}'");
				}
				else if (!code.ParentEntry.ChildEntries.Contains(code))
				{
					result.Errors.Add("CodeEntry '" + codeName + "' ParentEntry does not contain this child");
				}
			}
			foreach (GameMakerInstruction instruction in code.Instructions ?? new List<GameMakerInstruction>())
			{
				if (instruction.ValueFunction != null && !functionSet.Contains(instruction.ValueFunction))
				{
					result.Errors.Add($"CodeEntry '{codeName}' references missing Function '{instruction.ValueFunction.Name?.Content ?? "<null>"}'");
				}
			}
		}
		foreach (GameMakerGlobalInit global in data.GlobalInitScripts ?? new List<GameMakerGlobalInit>())
		{
			if (global?.Code != null && !codeSet.Contains(global.Code))
			{
				result.Errors.Add("GlobalInitScript references missing CodeEntry '" + (global.Code.Name?.Content ?? "<null>") + "'");
			}
		}
	}

	private static void ValidateObjects(GameMakerData data, HashSet<GameMakerCode> codeSet, HashSet<GameMakerGameObject> objectSet, HashSet<GameMakerSprite> spriteSet, DataIntegrityResult result)
	{
		foreach (GameMakerGameObject obj in data.GameObjects)
		{
			if (obj == null)
			{
				continue;
			}
			string name = obj.Name?.Content ?? "<unnamed object>";
			if (obj.Sprite != null && !spriteSet.Contains(obj.Sprite))
			{
				result.Errors.Add($"Object '{name}' references missing Sprite '{obj.Sprite.Name?.Content ?? "<null>"}'");
			}
			if (obj.TextureMaskId != null && !spriteSet.Contains(obj.TextureMaskId))
			{
				result.Errors.Add($"Object '{name}' references missing TextureMask Sprite '{obj.TextureMaskId.Name?.Content ?? "<null>"}'");
			}
			if (obj.ParentId != null && !objectSet.Contains(obj.ParentId))
			{
				result.Errors.Add($"Object '{name}' references missing parent Object '{obj.ParentId.Name?.Content ?? "<null>"}'");
			}
			if (HasParentCycle(obj))
			{
				result.Errors.Add("Object '" + name + "' has parent cycle");
			}
			if (obj.Events.Count <= 4)
			{
				result.Errors.Add($"Object '{name}' has too few event lists: {obj.Events.Count}");
			}
			for (int eventType = 0; eventType < obj.Events.Count; eventType++)
			{
				foreach (GameMakerGameObject.Event evt in obj.Events[eventType])
				{
					if (eventType == 4 && (evt.EventSubtype >= data.GameObjects.Count || data.GameObjects[(int)evt.EventSubtype] == null || !objectSet.Contains(data.GameObjects[(int)evt.EventSubtype])))
					{
						result.Errors.Add($"Object '{name}' collision event references missing Object index {evt.EventSubtype}");
					}
					foreach (GameMakerGameObject.EventAction action in evt.Actions ?? new GameMakerPointerList<GameMakerGameObject.EventAction>())
					{
						if (action?.CodeId != null && !codeSet.Contains(action.CodeId))
						{
							result.Errors.Add($"Object '{name}' event {eventType}/{evt.EventSubtype} references missing CodeEntry '{action.CodeId.Name?.Content ?? "<null>"}'");
						}
					}
				}
			}
		}
	}

	private static bool HasParentCycle(GameMakerGameObject obj)
	{
		HashSet<GameMakerGameObject> seen = new HashSet<GameMakerGameObject>();
		for (GameMakerGameObject current = obj.ParentId; current != null; current = current.ParentId)
		{
			if (!seen.Add(current))
			{
				return true;
			}
		}
		return false;
	}

	private static void ValidateTextures(GameMakerData data, HashSet<GameMakerSprite> spriteSet, HashSet<GameMakerTexturePageItem> texturePageItemSet, HashSet<GameMakerEmbeddedTexture> embeddedTextureSet, DataIntegrityResult result)
	{
		foreach (GameMakerTexturePageItem tpi in data.TexturePageItems)
		{
			if (tpi?.TexturePage != null && !embeddedTextureSet.Contains(tpi.TexturePage))
			{
				result.Errors.Add("TexturePageItem '" + (tpi.Name?.Content ?? "<unnamed>") + "' references missing EmbeddedTexture");
			}
		}
		foreach (GameMakerSprite sprite in data.Sprites)
		{
			if (sprite == null)
			{
				continue;
			}
			foreach (GameMakerSprite.TextureEntry texture in sprite.Textures ?? new GameMakerSimpleList<GameMakerSprite.TextureEntry>())
			{
				if (texture?.Texture != null && !texturePageItemSet.Contains(texture.Texture))
				{
					result.Errors.Add("Sprite '" + (sprite.Name?.Content ?? "<unnamed>") + "' references missing TexturePageItem");
				}
			}
		}
		foreach (GameMakerBackground background in data.Backgrounds)
		{
			if (background?.Texture != null && !texturePageItemSet.Contains(background.Texture))
			{
				result.Errors.Add("Background '" + (background.Name?.Content ?? "<unnamed>") + "' references missing TexturePageItem");
			}
			if (background?.GMS2ExportedSprite != null && !spriteSet.Contains(background.GMS2ExportedSprite))
			{
				result.Errors.Add("Background '" + (background.Name?.Content ?? "<unnamed>") + "' references missing exported Sprite");
			}
		}
		foreach (GameMakerFont font in data.Fonts)
		{
			if (font?.Texture != null && !texturePageItemSet.Contains(font.Texture))
			{
				result.Errors.Add("Font '" + (font.Name?.Content ?? "<unnamed>") + "' references missing TexturePageItem");
			}
		}
	}

	private static void ValidateSounds(GameMakerData data, HashSet<GameMakerAudioGroup> audioGroupSet, DataIntegrityResult result)
	{
		foreach (GameMakerSound sound in data.Sounds)
		{
			if (sound?.AudioGroup != null && !audioGroupSet.Contains(sound.AudioGroup))
			{
				result.Errors.Add($"Sound '{sound.Name?.Content ?? "<unnamed>"}' references missing AudioGroup '{sound.AudioGroup.Name?.Content ?? "<null>"}'");
			}
			if (sound != null && data.AudioGroups != null && data.AudioGroups.Count > 0 && sound.GroupID >= data.AudioGroups.Count)
			{
				result.Errors.Add($"Sound '{sound.Name?.Content ?? "<unnamed>"}' has out-of-range GroupID {sound.GroupID}");
			}
		}
	}

	private static void ValidateRooms(GameMakerData data, HashSet<GameMakerCode> codeSet, HashSet<GameMakerGameObject> objectSet, HashSet<GameMakerSprite> spriteSet, HashSet<GameMakerBackground> backgroundSet, HashSet<GameMakerSequence> sequenceSet, HashSet<GameMakerParticleSystem> particleSystemSet, DataIntegrityResult result)
	{
		foreach (GameMakerRoom room in data.Rooms)
		{
			if (room == null)
			{
				continue;
			}
			string name = room.Name?.Content ?? "<unnamed room>";
			if (room.CreationCodeId != null && !codeSet.Contains(room.CreationCodeId))
			{
				result.Errors.Add($"Room '{name}' references missing CreationCode '{room.CreationCodeId.Name?.Content ?? "<null>"}'");
			}
			if (data.GeneralInfo?.RoomOrder != null)
			{
				foreach (GameMakerResourceById<GameMakerRoom, GameMakerChunkROOM> roomRef in data.GeneralInfo.RoomOrder)
				{
					if (roomRef?.Resource != null && !data.Rooms.Contains(roomRef.Resource))
					{
						result.Errors.Add("GeneralInfo room order references missing Room '" + (roomRef.Resource.Name?.Content ?? "<null>") + "'");
					}
				}
			}
			foreach (GameMakerRoom.View view in room.Views ?? new GameMakerPointerList<GameMakerRoom.View>())
			{
				if (view?.ObjectId != null && !objectSet.Contains(view.ObjectId))
				{
					result.Errors.Add($"Room '{name}' view references missing Object '{view.ObjectId.Name?.Content ?? "<null>"}'");
				}
			}
			foreach (GameMakerRoom.GameObject instance in room.GameObjects ?? new GameMakerPointerList<GameMakerRoom.GameObject>())
			{
				ValidateRoomObject(name, instance, codeSet, objectSet, result);
			}
			foreach (GameMakerRoom.Tile tile in room.Tiles ?? new GameMakerPointerList<GameMakerRoom.Tile>())
			{
				ValidateTile(name, tile, spriteSet, backgroundSet, result);
			}
			foreach (GameMakerRoom.Layer layer in room.Layers ?? new GameMakerPointerList<GameMakerRoom.Layer>())
			{
				if (layer == null)
				{
					continue;
				}
				if (layer.ParentRoom != room)
				{
					layer.ParentRoom = room;
				}
				if (layer.InstancesData?.Instances != null)
				{
					foreach (GameMakerRoom.GameObject instance2 in layer.InstancesData.Instances)
					{
						ValidateRoomObject(name, instance2, codeSet, objectSet, result);
					}
				}
				if (layer.TilesData?.Background != null && !backgroundSet.Contains(layer.TilesData.Background))
				{
					result.Errors.Add($"Room '{name}' tile layer references missing Background '{layer.TilesData.Background.Name?.Content ?? "<null>"}'");
				}
				if (layer.TilesData?.TileData != null && (layer.TilesData.TileData.Length != layer.TilesData.TilesY || layer.TilesData.TileData.Any((uint[] row) => row == null || row.Length != layer.TilesData.TilesX)))
				{
					result.Errors.Add("Room '" + name + "' tile layer has invalid TileData dimensions");
				}
				if (layer.BackgroundData?.Sprite != null && !spriteSet.Contains(layer.BackgroundData.Sprite))
				{
					result.Errors.Add($"Room '{name}' layer background references missing Sprite '{layer.BackgroundData.Sprite.Name?.Content ?? "<null>"}'");
				}
				if (layer.AssetsData?.Sprites != null)
				{
					foreach (GameMakerRoom.SpriteInstance spriteInstance in layer.AssetsData.Sprites)
					{
						if (spriteInstance?.Sprite != null && !spriteSet.Contains(spriteInstance.Sprite))
						{
							result.Errors.Add($"Room '{name}' asset layer references missing Sprite '{spriteInstance.Sprite.Name?.Content ?? "<null>"}'");
						}
					}
				}
				if (layer.AssetsData?.Sequences != null)
				{
					foreach (GameMakerRoom.SequenceInstance sequenceInstance in layer.AssetsData.Sequences)
					{
						if (sequenceInstance?.Sequence != null && !sequenceSet.Contains(sequenceInstance.Sequence))
						{
							result.Errors.Add($"Room '{name}' asset layer references missing Sequence '{sequenceInstance.Sequence.Name?.Content ?? "<null>"}'");
						}
					}
				}
				if (layer.AssetsData?.ParticleSystems == null)
				{
					continue;
				}
				foreach (GameMakerRoom.ParticleSystemInstance particleInstance in layer.AssetsData.ParticleSystems)
				{
					if (particleInstance?.ParticleSystem != null && !particleSystemSet.Contains(particleInstance.ParticleSystem))
					{
						result.Errors.Add($"Room '{name}' asset layer references missing ParticleSystem '{particleInstance.ParticleSystem.Name?.Content ?? "<null>"}'");
					}
				}
			}
		}
	}

	private static void ValidateRoomObject(string roomName, GameMakerRoom.GameObject? instance, HashSet<GameMakerCode> codeSet, HashSet<GameMakerGameObject> objectSet, DataIntegrityResult result)
	{
		if (instance != null)
		{
			if (instance.ObjectDefinition != null && !objectSet.Contains(instance.ObjectDefinition))
			{
				result.Errors.Add($"Room '{roomName}' instance {instance.InstanceID} references missing Object '{instance.ObjectDefinition.Name?.Content ?? "<null>"}'");
			}
			if (instance.CreationCode != null && !codeSet.Contains(instance.CreationCode))
			{
				result.Errors.Add($"Room '{roomName}' instance {instance.InstanceID} references missing CreationCode '{instance.CreationCode.Name?.Content ?? "<null>"}'");
			}
			if (instance.PreCreateCode != null && !codeSet.Contains(instance.PreCreateCode))
			{
				result.Errors.Add($"Room '{roomName}' instance {instance.InstanceID} references missing PreCreateCode '{instance.PreCreateCode.Name?.Content ?? "<null>"}'");
			}
		}
	}

	private static void ValidateTile(string roomName, GameMakerRoom.Tile? tile, HashSet<GameMakerSprite> spriteSet, HashSet<GameMakerBackground> backgroundSet, DataIntegrityResult result)
	{
		if (tile == null)
		{
			return;
		}
		if (tile.spriteMode)
		{
			if (tile.SpriteDefinition != null && !spriteSet.Contains(tile.SpriteDefinition))
			{
				result.Errors.Add($"Room '{roomName}' tile {tile.InstanceID} references missing Sprite '{tile.SpriteDefinition.Name?.Content ?? "<null>"}'");
			}
		}
		else if (tile.BackgroundDefinition != null && !backgroundSet.Contains(tile.BackgroundDefinition))
		{
			result.Errors.Add($"Room '{roomName}' tile {tile.InstanceID} references missing Background '{tile.BackgroundDefinition.Name?.Content ?? "<null>"}'");
		}
	}

	private static void ValidateParticles(GameMakerData data, HashSet<GameMakerSprite> spriteSet, HashSet<GameMakerParticleSystemEmitter> particleEmitterSet, DataIntegrityResult result)
	{
		foreach (GameMakerParticleSystemEmitter emitter in data.ParticleSystemEmitters ?? new List<GameMakerParticleSystemEmitter>())
		{
			if (emitter != null)
			{
				string name = emitter.Name?.Content ?? "<unnamed emitter>";
				if (emitter.Sprite != null && !spriteSet.Contains(emitter.Sprite))
				{
					result.Errors.Add($"ParticleEmitter '{name}' references missing Sprite '{emitter.Sprite.Name?.Content ?? "<null>"}'");
				}
				if (emitter.SpawnOnDeath != null && !particleEmitterSet.Contains(emitter.SpawnOnDeath))
				{
					result.Errors.Add($"ParticleEmitter '{name}' references missing SpawnOnDeath emitter '{emitter.SpawnOnDeath.Name?.Content ?? "<null>"}'");
				}
				if (emitter.SpawnOnUpdate != null && !particleEmitterSet.Contains(emitter.SpawnOnUpdate))
				{
					result.Errors.Add($"ParticleEmitter '{name}' references missing SpawnOnUpdate emitter '{emitter.SpawnOnUpdate.Name?.Content ?? "<null>"}'");
				}
			}
		}
	}
}

public sealed class DataIntegrityResult
{
	public List<string> Errors { get; } = new List<string>();
	public List<string> Repairs { get; } = new List<string>();
	public bool Success => Errors.Count == 0;
}
