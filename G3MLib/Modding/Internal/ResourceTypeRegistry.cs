namespace G3MLib.Modding.Internal;

public static class ResourceTypeRegistry
{
	public static readonly string[] AllTypes =
	[
		"GeneralInfo", "Options", "GlobalScripts", "Scripts", "Language", "FeatureFlags", "Tags", "FilterEffects", "AudioGroups", "EmbeddedAudio",
		"TextureGroupInfo", "EmbeddedTextures", "TexturePageItems", "EmbeddedImages", "Sprites", "Backgrounds", "Fonts", "Sounds", "Paths", "Tilesets",
		"Shaders", "Timelines", "GameObjects", "AnimationCurves", "ParticleSystemEmitters", "ParticleSystems", "Sequences", "Rooms", "CodeEntries", "Extensions"
	];

	public static readonly string[] ImportOrder =
	[
		"Options", "GlobalScripts", "Scripts", "Language", "FeatureFlags", "Tags", "FilterEffects", "AudioGroups", "EmbeddedAudio", "TextureGroupInfo",
		"EmbeddedTextures", "TexturePageItems", "EmbeddedImages", "Sprites", "Backgrounds", "Fonts", "Sounds", "Paths", "Tilesets", "Shaders",
		"Timelines", "GameObjects", "AnimationCurves", "ParticleSystemEmitters", "ParticleSystems", "Sequences", "Rooms", "Extensions", "GeneralInfo", "CodeEntries"
	];
}
