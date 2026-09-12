using System;
using System.IO;
using G3MLib.DataFile;
using G3MLib.DataFile.Project;
using G3MLib.DataFile.Util;

namespace G3MLib.Modding.Internal;

internal static class ResourceAssetUtil
{
	public static int GetGameObjectOccurrence(GameMakerData data, int objectIndex)
	{
		if (objectIndex < 0 || objectIndex >= data.GameObjects.Count)
		{
			return -1;
		}
		string name = data.GameObjects[objectIndex]?.Name?.Content;
		if (name == null)
		{
			return -1;
		}
		int occurrence = 0;
		for (int i = 0; i < objectIndex; i++)
		{
			if (string.Equals(data.GameObjects[i]?.Name?.Content, name, StringComparison.Ordinal))
			{
				occurrence++;
			}
		}
		return occurrence;
	}

	public static string? GetAssetNameByTagType(GameMakerData data, ResourceType type, int index)
	{
		switch (type)
		{
		case ResourceType.Object:
			if (index >= 0 && index < data.GameObjects.Count)
			{
				return data.GameObjects[index]?.Name?.Content;
			}
			break;
		case ResourceType.Sprite:
			if (index >= 0 && index < data.Sprites.Count)
			{
				return data.Sprites[index]?.Name?.Content;
			}
			break;
		case ResourceType.Sound:
			if (index >= 0 && index < data.Sounds.Count)
			{
				return data.Sounds[index]?.Name?.Content;
			}
			break;
		case ResourceType.Room:
			if (index >= 0 && index < data.Rooms.Count)
			{
				return data.Rooms[index]?.Name?.Content;
			}
			break;
		case ResourceType.Path:
			if (index >= 0 && index < data.Paths.Count)
			{
				return data.Paths[index]?.Name?.Content;
			}
			break;
		case ResourceType.Script:
			if (index >= 0 && index < data.Scripts.Count)
			{
				return data.Scripts[index]?.Name?.Content;
			}
			break;
		case ResourceType.Font:
			if (index >= 0 && index < data.Fonts.Count)
			{
				return data.Fonts[index]?.Name?.Content;
			}
			break;
		case ResourceType.Timeline:
			if (index >= 0 && index < data.Timelines.Count)
			{
				return data.Timelines[index]?.Name?.Content;
			}
			break;
		case ResourceType.Background:
			if (index >= 0 && index < data.Backgrounds.Count)
			{
				return data.Backgrounds[index]?.Name?.Content;
			}
			break;
		case ResourceType.Shader:
			if (index >= 0 && index < data.Shaders.Count)
			{
				return data.Shaders[index]?.Name?.Content;
			}
			break;
		case ResourceType.Sequence:
			if (data.Sequences != null && index >= 0 && index < data.Sequences.Count)
			{
				return data.Sequences[index]?.Name?.Content;
			}
			break;
		case ResourceType.AnimCurve:
			if (data.AnimationCurves != null && index >= 0 && index < data.AnimationCurves.Count)
			{
				return data.AnimationCurves[index]?.Name?.Content;
			}
			break;
		case ResourceType.ParticleSystem:
			if (data.ParticleSystems != null && index >= 0 && index < data.ParticleSystems.Count)
			{
				return data.ParticleSystems[index]?.Name?.Content;
			}
			break;
		}
		return null;
	}

	public static string? ResolveAudioGroupPath(GameMakerData data, string dataDir, int groupId)
	{
		string relativePath = $"audiogroup{groupId}.dat";
		if (data.AudioGroups != null && groupId >= 0 && groupId < data.AudioGroups.Count && !string.IsNullOrWhiteSpace(data.AudioGroups[groupId]?.Path?.Content))
		{
			relativePath = data.AudioGroups[groupId]?.Path?.Content ?? relativePath;
		}
		try
		{
			string baseDir = Path.GetFullPath(dataDir);
			string fullPath = Path.GetFullPath(Path.Combine(baseDir, relativePath));
			return Paths.IsWithinDirectory(baseDir, fullPath) ? fullPath : null;
		}
		catch
		{
			return null;
		}
	}

	public static string? ResolveAudioGroupPathFromDataFile(GameMakerData data, string dataPath, int groupId)
	{
		string baseDir = Path.GetDirectoryName(Path.GetFullPath(dataPath));
		if (!string.IsNullOrWhiteSpace(baseDir))
		{
			return ResolveAudioGroupPath(data, baseDir, groupId);
		}
		return null;
	}

	public static ProjectContext CreateProjectContext(GameMakerData data, string root)
	{
		Directory.CreateDirectory(root);
		string project = Path.Combine(root, "project", "project.yy");
		Directory.CreateDirectory(Path.GetDirectoryName(project));
		return new ProjectContext(data, Path.Combine(root, "load.win"), Path.Combine(root, "save.win"), project, "G3MLib");
	}

	public static bool IsLocaleArtSpriteName(string spriteName)
	{
		if (!spriteName.StartsWith("spr_ja_", StringComparison.OrdinalIgnoreCase))
		{
			return spriteName.StartsWith("bg_lang_ja_", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
