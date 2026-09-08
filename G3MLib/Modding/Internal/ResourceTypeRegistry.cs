namespace G3MLib.Modding.Internal;

public static class ResourceTypeRegistry
{
	public static readonly string[] AllTypes = new string[30]
	{
		"GeneralInfo", "Options", "GlobalScripts", "Scripts", "Language", "FeatureFlags", "Tags", "FilterEffects", "AudioGroups", "EmbeddedAudio",
		"TextureGroupInfo", "EmbeddedTextures", "TexturePageItems", "EmbeddedImages", "Sprites", "Backgrounds", "Fonts", "Sounds", "Paths", "Tilesets",
		"Shaders", "Timelines", "GameObjects", "AnimationCurves", "ParticleSystemEmitters", "ParticleSystems", "Sequences", "Rooms", "CodeEntries", "Extensions"
	};

	public static readonly string[] ImportOrder = new string[30]
	{
		"Options", "GlobalScripts", "Scripts", "Language", "FeatureFlags", "Tags", "FilterEffects", "AudioGroups", "EmbeddedAudio", "TextureGroupInfo",
		"EmbeddedTextures", "TexturePageItems", "EmbeddedImages", "Sprites", "Backgrounds", "Fonts", "Sounds", "Paths", "Tilesets", "Shaders",
		"Timelines", "GameObjects", "AnimationCurves", "ParticleSystemEmitters", "ParticleSystems", "Sequences", "Rooms", "Extensions", "GeneralInfo", "CodeEntries"
	};
}
