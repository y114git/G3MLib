using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;

namespace G3MLib.Modding.Resources;

public sealed record FrameJob(GameMakerTexturePageItem Item, string Path, bool Padding);

/// <summary>Exports frames to caller chosen paths. Frames are grouped by texture page and a page is evicted when its frames are done, so only a few pages are in memory.</summary>
public static class FrameExporter
{
	public static IReadOnlyList<ItemResult> ExportPng(IReadOnlyList<FrameJob> jobs, int maxPagesInFlight, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
		BatchRun.RunImport(progress, cancellationToken, () =>
		{
			List<IGrouping<GameMakerEmbeddedTexture, FrameJob>> pages = [.. jobs.GroupBy(job => job.Item.TexturePage)];
			BatchRun.Expect(jobs.Count - pages.Count);
			using var worker = new TextureWorker();
			var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxPagesInFlight) };
			CancellableParallel.ForEach(pages, options, group =>
			{
				foreach (var job in group)
				{
					try
					{
						worker.ExportAsPNG(job.Item, job.Path, null, job.Padding);
						BatchRun.Item(job.Path);
					}
					catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
					{
						BatchRun.Failed(job.Path, ex);
					}
				}
				if (group.Key != null) worker.Evict(group.Key);
			});
		});
}
