using System.IO;
using G3MLib.DataFile;

namespace G3MLib.Modding.Resources;

// Batch shaped overloads of the per type exports. Each reports one result per item and one progress step per item;
// an item that fails becomes a Failed result and the run goes on. Cancelling throws and removes what the run added.
public static partial class ResourceExportService
{
	private static HashSet<string>? AsSet(ISet<string>? filter) => filter as HashSet<string> ?? (filter == null ? null : new HashSet<string>(filter));

	private static IReadOnlyList<ItemResult> ExportSingle(string outputDir, string type, IProgress<BatchProgress>? progress, CancellationToken cancellationToken, Action export) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () =>
		{
			BatchRun.Expect(1);
			export();
			var written = Directory.Exists(Path.Combine(outputDir, type));
			BatchRun.Item(type, written ? ResultKind.Written : ResultKind.Skipped, written ? null : "Nothing to export");
		});

	public static IReadOnlyList<ItemResult> ExportGeneralInfo(GameMakerData data, string outputDir, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		ExportSingle(outputDir, "GeneralInfo", progress, cancellationToken, () => ExportGeneralInfo(data, outputDir));

	public static IReadOnlyList<ItemResult> ExportOptions(GameMakerData data, string outputDir, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		ExportSingle(outputDir, "Options", progress, cancellationToken, () => ExportOptions(data, outputDir));

	public static IReadOnlyList<ItemResult> ExportGlobalScripts(GameMakerData data, string outputDir, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		ExportSingle(outputDir, "GlobalScripts", progress, cancellationToken, () => ExportGlobalScripts(data, outputDir));

	public static IReadOnlyList<ItemResult> ExportFeatureFlags(GameMakerData data, string outputDir, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		ExportSingle(outputDir, "FeatureFlags", progress, cancellationToken, () => ExportFeatureFlags(data, outputDir));

	public static IReadOnlyList<ItemResult> ExportLanguage(GameMakerData data, string outputDir, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		ExportSingle(outputDir, "Language", progress, cancellationToken, () => ExportLanguage(data, outputDir));

	public static IReadOnlyList<ItemResult> ExportTags(GameMakerData data, string outputDir, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		ExportSingle(outputDir, "Tags", progress, cancellationToken, () => ExportTags(data, outputDir));

	public static IReadOnlyList<ItemResult> ExportTexturePageItems(GameMakerData data, string outputDir, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		ExportSingle(outputDir, "TexturePageItems", progress, cancellationToken, () => ExportTexturePageItems(data, outputDir));

	public static IReadOnlyList<ItemResult> ExportTextureGroupInfo(GameMakerData data, string outputDir, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportTextureGroupInfo(data, outputDir));

	public static IReadOnlyList<ItemResult> ExportExtensions(GameMakerData data, string outputDir, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportExtensions(data, outputDir));

	public static IReadOnlyList<ItemResult> ExportScripts(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportScripts(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportEmbeddedImages(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportEmbeddedImages(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportFilterEffects(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportFilterEffects(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportAudioGroups(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportAudioGroups(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportEmbeddedTextures(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportEmbeddedTextures(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportSprites(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportSprites(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportBackgrounds(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportBackgrounds(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportFonts(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportFonts(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportSounds(GameMakerData data, string outputDir, string dataFilePath, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportSounds(data, outputDir, dataFilePath, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportEmbeddedAudio(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportEmbeddedAudio(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportPaths(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportPaths(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportTilesets(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportTilesets(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportShaders(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportShaders(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportTimelines(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportTimelines(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportAnimationCurves(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportAnimationCurves(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportSequences(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportSequences(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportParticleSystems(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportParticleSystems(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportParticleSystemEmitters(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportParticleSystemEmitters(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportGameObjects(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportGameObjects(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportRooms(GameMakerData data, string outputDir, ISet<string>? filter, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportRooms(data, outputDir, AsSet(filter)));

	public static IReadOnlyList<ItemResult> ExportCodeEntries(GameMakerData data, string outputDir, IProgress<BatchProgress>? progress, CancellationToken cancellationToken, int maxDegreeOfParallelism = -1) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportCodeEntries(data, outputDir, maxDegreeOfParallelism));

	public static IReadOnlyList<ItemResult> ExportCodeEntriesSelective(GameMakerData data, string outputDir, ISet<string> entryNames, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.Run(outputDir, progress, cancellationToken, () => ExportCodeEntriesSelective(data, outputDir, AsSet(entryNames)!));
}
