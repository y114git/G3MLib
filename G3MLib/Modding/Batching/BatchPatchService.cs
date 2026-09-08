using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace G3MLib.Modding.Batching;

public static class BatchPatchService
{
	private sealed record BatchJob(int Index, string Kind, string Key, string[] Inputs, string[] Outputs);

	private const string ApplyJobKind = "apply";

	private const string CreateJobKind = "create";

	private const string MergeJobKind = "merge";

	private const string MergeLogExtension = ".merge_log.md";

	public static async Task<BatchResult> ApplyBatchAsync(IReadOnlyList<string> patchPaths, BatchOptions options)
	{
		string outDir = RequireOutDir(options);
		Directory.CreateDirectory(outDir);
		ValidateOriginal(options.OriginalPath);
		List<string> inputs = NormalizeExistingFiles(patchPaths, "patch");
		string originalPath = options.OriginalPath;
		List<string> list = inputs;
		int num = 0;
		string[] array = new string[1 + list.Count];
		array[num] = originalPath;
		num++;
		Span<string> span = CollectionsMarshal.AsSpan(list);
		span.CopyTo(new Span<string>(array).Slice(num, span.Length));
		_ = num + span.Length;
		Dictionary<string, string> hashes = await ComputeFileHashesAsync(array);
		string originalHash = hashes[Path.GetFullPath(options.OriginalPath)];
		string originalStem = Path.GetFileNameWithoutExtension(options.OriginalPath);
		string dataOutputExtension = GetDataOutputExtension(options.OriginalPath);
		List<string> outputPaths = BuildUniqueOutputPaths(inputs.Select((string path) => Path.Combine(outDir, SanitizeFileName(originalStem) + "_" + SanitizeFileName(Path.GetFileNameWithoutExtension(path)) + dataOutputExtension)));
		List<BatchJob> jobs = new List<BatchJob>();
		for (int i = 0; i < inputs.Count; i++)
		{
			string inputHash = hashes[inputs[i]];
			string key = BuildKey("apply", originalHash, inputHash, options.XdeltaFallback.ToString());
			jobs.Add(new BatchJob(i + 1, "apply", key, new string[1] { inputs[i] }, new string[1] { outputPaths[i] }));
		}
		return await RunDeduplicatedAsync(jobs, options, ExecuteApplyJobAsync);
	}

	public static async Task<BatchResult> CreateBatchAsync(IReadOnlyList<string> modifiedPaths, BatchOptions options)
	{
		string outDir = RequireOutDir(options);
		Directory.CreateDirectory(outDir);
		ValidateOriginal(options.OriginalPath);
		List<string> inputs = NormalizeExistingFiles(modifiedPaths, "modified");
		string originalPath = options.OriginalPath;
		List<string> list = inputs;
		int num = 0;
		string[] array = new string[1 + list.Count];
		array[num] = originalPath;
		num++;
		Span<string> span = CollectionsMarshal.AsSpan(list);
		span.CopyTo(new Span<string>(array).Slice(num, span.Length));
		_ = num + span.Length;
		Dictionary<string, string> hashes = await ComputeFileHashesAsync(array);
		string originalHash = hashes[Path.GetFullPath(options.OriginalPath)];
		string outputExtension = (options.CreateXdelta ? ".xdelta" : ".g3mpatch");
		List<string> outputPaths = BuildUniqueOutputPaths(inputs.Select((string path) => Path.Combine(outDir, SanitizeFileName(Path.GetFileNameWithoutExtension(path)) + outputExtension)));
		List<BatchJob> jobs = new List<BatchJob>();
		for (int i = 0; i < inputs.Count; i++)
		{
			string inputHash = hashes[inputs[i]];
			string key = BuildKey("create", originalHash, inputHash, options.CreateXdelta.ToString(), options.IncludeXdeltaFallback.ToString());
			jobs.Add(new BatchJob(i + 1, "create", key, new string[1] { inputs[i] }, new string[1] { outputPaths[i] }));
		}
		return await RunDeduplicatedAsync(jobs, options, ExecuteCreateJobAsync);
	}

	public static async Task<BatchResult> MergeBatchAsync(IReadOnlyList<string> setSpecs, BatchOptions options)
	{
		string applyDir = (string.IsNullOrWhiteSpace(options.ApplyDir) ? Directory.GetCurrentDirectory() : Path.GetFullPath(options.ApplyDir));
		string patchDir = (string.IsNullOrWhiteSpace(options.OutDir) ? null : Path.GetFullPath(options.OutDir));
		Directory.CreateDirectory(applyDir);
		if (patchDir != null)
		{
			Directory.CreateDirectory(patchDir);
		}
		ValidateOriginal(options.OriginalPath);
		List<List<string>> sets = ParseMergeSets(setSpecs);
		List<string>[] normalizedSets = sets.Select((List<string> paths) => NormalizeExistingFiles(paths, "merge patch")).ToArray();
		List<string> list = new List<string>();
		list.Add(options.OriginalPath);
		list.AddRange(normalizedSets.SelectMany((List<string> result) => result));
		Dictionary<string, string> hashes = await ComputeFileHashesAsync(list);
		string originalHash = hashes[Path.GetFullPath(options.OriginalPath)];
		string dataOutputExtension = GetDataOutputExtension(options.OriginalPath);
		List<BatchJob> jobs = new List<BatchJob>();
		List<string> patchOutputCandidates = new List<string>();
		List<string> dataOutputCandidates = new List<string>();
		for (int i = 0; i < normalizedSets.Length; i++)
		{
			List<string> set = normalizedSets[i];
			string setName = BuildMergeSetName(i + 1, set);
			dataOutputCandidates.Add(Path.Combine(applyDir, setName + dataOutputExtension));
			if (patchDir != null)
			{
				patchOutputCandidates.Add(Path.Combine(patchDir, setName + ".g3mpatch"));
			}
			IEnumerable<string> inputHashes = set.Select((string path) => hashes[path]);
			string key = BuildKey("merge", originalHash, string.Join(">", inputHashes), options.UseCodeMerge.ToString(), options.UsePropertyMerge.ToString());
			jobs.Add(new BatchJob(i + 1, "merge", key, set.ToArray(), Array.Empty<string>()));
		}
		List<string> patchOutputs = BuildUniqueOutputPaths(patchOutputCandidates);
		List<string> dataOutputs = BuildUniqueOutputPaths(dataOutputCandidates);
		for (int i2 = 0; i2 < jobs.Count; i2++)
		{
			string[] outputs = ((patchDir == null) ? new string[1] { dataOutputs[i2] } : new string[2]
			{
				dataOutputs[i2],
				patchOutputs[i2]
			});
			jobs[i2] = jobs[i2]with
			{
				Outputs = outputs
			};
		}
		return await RunDeduplicatedAsync(jobs, options, ExecuteMergeJobAsync);
	}

	private static async Task<BatchResult> RunDeduplicatedAsync(IReadOnlyList<BatchJob> jobs, BatchOptions options, Func<BatchJob, BatchOptions, Task<BatchItemResult>> executor)
	{
		int concurrency = GetSafeConcurrency();
		if (concurrency > 1 && jobs.Count > 1)
		{
			return await RunIsolatedParallelAsync(jobs, options, concurrency);
		}
		BatchResult result = new BatchResult
		{
			Total = jobs.Count
		};
		Dictionary<string, BatchItemResult> completedByKey = new Dictionary<string, BatchItemResult>(StringComparer.Ordinal);
		foreach (BatchJob job in jobs)
		{
			if (completedByKey.TryGetValue(job.Key, out BatchItemResult completed))
			{
				try
				{
					string[] copiedOutputs = CopyOutputs(completed.Outputs, job.Outputs);
					BatchItemResult duplicate = new BatchItemResult
					{
						Index = job.Index,
						Kind = job.Kind,
						Key = job.Key,
						Inputs = job.Inputs,
						Outputs = copiedOutputs,
						Success = true,
						Deduplicated = true,
						Seconds = 0.0
					};
					result.Items.Add(duplicate);
					result.Completed++;
					result.Deduplicated++;
					LogService.Info($"[Batch] {job.Kind} #{job.Index}: duplicated from earlier identical job");
				}
				catch (Exception ex)
				{
					BatchItemResult failedDuplicate = Failed(job, ex.Message, 0.0, deduplicated: true);
					result.Items.Add(failedDuplicate);
					result.Failed++;
					if (!options.ContinueOnError)
					{
						break;
					}
				}
				continue;
			}
			BatchItemResult item = await executor(job, options);
			result.Items.Add(item);
			if (item.Success)
			{
				completedByKey[job.Key] = item;
				result.Completed++;
				continue;
			}
			result.Failed++;
			if (options.ContinueOnError)
			{
				continue;
			}
			break;
		}
		return result;
	}

	private static int GetSafeConcurrency()
	{
		int cpuLimit = Math.Max(1, Environment.ProcessorCount / 2);
		long memoryBudget = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
		int memoryLimit = Math.Max(1, (int)(memoryBudget / 4294967296L));
		return Math.Min(4, Math.Min(cpuLimit, memoryLimit));
	}

	private static async Task<BatchResult> RunIsolatedParallelAsync(IReadOnlyList<BatchJob> jobs, BatchOptions options, int concurrency)
	{
		BatchJob[] uniqueJobs = (from @group in jobs.GroupBy<BatchJob, string>((BatchJob batchJob) => batchJob.Key, StringComparer.Ordinal)
			select @group.First()).ToArray();
		ConcurrentDictionary<int, BatchItemResult> completed = new ConcurrentDictionary<int, BatchItemResult>();
		int nextIndex = -1;
		int stopRequested = 0;
		LogService.Info($"[Batch] Running up to {concurrency} isolated jobs in parallel");
		await Task.WhenAll(Enumerable.Range(0, Math.Min(concurrency, uniqueJobs.Length)).Select((Func<int, Task>)async delegate
		{
			while (options.ContinueOnError || Volatile.Read(in stopRequested) == 0)
			{
				int index = Interlocked.Increment(ref nextIndex);
				if (index >= uniqueJobs.Length)
				{
					break;
				}
				BatchJob job2 = uniqueJobs[index];
				BatchItemResult item2 = await ExecuteJobInChildProcessAsync(job2, options);
				completed[job2.Index] = item2;
				if (!item2.Success && !options.ContinueOnError)
				{
					Interlocked.Exchange(ref stopRequested, 1);
				}
			}
		}));
		BatchResult result = new BatchResult
		{
			Total = jobs.Count
		};
		Dictionary<string, BatchItemResult> completedByKey = new Dictionary<string, BatchItemResult>(StringComparer.Ordinal);
		foreach (BatchJob job in jobs)
		{
			if (completedByKey.TryGetValue(job.Key, out BatchItemResult source))
			{
				try
				{
					string[] outputs = CopyOutputs(source.Outputs, job.Outputs);
					result.Items.Add(new BatchItemResult
					{
						Index = job.Index,
						Kind = job.Kind,
						Key = job.Key,
						Inputs = job.Inputs,
						Outputs = outputs,
						Success = true,
						Deduplicated = true
					});
					result.Completed++;
					result.Deduplicated++;
				}
				catch (Exception ex)
				{
					result.Items.Add(Failed(job, ex.Message, 0.0, deduplicated: true));
					result.Failed++;
					if (!options.ContinueOnError)
					{
						break;
					}
				}
				continue;
			}
			if (!completed.TryGetValue(job.Index, out BatchItemResult item))
			{
				if (!options.ContinueOnError)
				{
					break;
				}
				item = await ExecuteJobInChildProcessAsync(job, options);
			}
			result.Items.Add(item);
			if (item.Success)
			{
				completedByKey[job.Key] = item;
				result.Completed++;
				continue;
			}
			result.Failed++;
			if (options.ContinueOnError)
			{
				continue;
			}
			break;
		}
		if (!options.ContinueOnError && result.Failed > 0)
		{
			HashSet<int> reported = result.Items.Select((BatchItemResult batchItemResult) => batchItemResult.Index).ToHashSet();
			foreach (BatchItemResult item3 in completed.Values.Where((BatchItemResult batchItemResult) => !reported.Contains(batchItemResult.Index)))
			{
				string[] outputs2 = item3.Outputs;
				foreach (string output in outputs2)
				{
					try
					{
						File.Delete(output);
					}
					catch
					{
					}
				}
			}
		}
		return result;
	}

	private static async Task<BatchItemResult> ExecuteJobInChildProcessAsync(BatchJob job, BatchOptions options)
	{
		Stopwatch sw = Stopwatch.StartNew();
		try
		{
			ProcessStartInfo startInfo = CreateChildStartInfo();
			foreach (string argument in BuildChildArguments(job, options))
			{
				startInfo.ArgumentList.Add(argument);
			}
			using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start isolated G3MTool CLI process");
			Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
			Task<string> errorTask = process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync();
			string output = await outputTask;
			string error = await errorTask;
			if (process.ExitCode != 0)
			{
				string message = (string.IsNullOrWhiteSpace(error) ? output : error);
				return Failed(job, message.Trim(), sw.Elapsed.TotalSeconds);
			}
			string[] outputs = job.Outputs;
			foreach (string path in outputs)
			{
				if (!File.Exists(path))
				{
					return Failed(job, "Expected output was not created: " + path, sw.Elapsed.TotalSeconds);
				}
			}
			string[] outputs2 = job.Outputs;
			string report = GetReportPath(job, options);
			if (report != null && File.Exists(report))
			{
				outputs = outputs2;
				int i = 0;
				string[] array = new string[1 + outputs.Length];
				ReadOnlySpan<string> readOnlySpan = new ReadOnlySpan<string>(outputs);
				readOnlySpan.CopyTo(new Span<string>(array).Slice(i, readOnlySpan.Length));
				i += readOnlySpan.Length;
				array[i] = report;
				outputs2 = array;
			}
			return Succeeded(job with
			{
				Outputs = outputs2
			}, sw.Elapsed.TotalSeconds);
		}
		catch (Exception ex)
		{
			return Failed(job, ex.Message, sw.Elapsed.TotalSeconds);
		}
	}

	private static ProcessStartInfo CreateChildStartInfo()
	{
		string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to locate the G3MTool CLI executable");
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = processPath,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
		{
			string assemblyPath = Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("Unable to locate the G3MTool CLI assembly");
			startInfo.ArgumentList.Add(assemblyPath);
		}
		return startInfo;
	}

	private static IEnumerable<string> BuildChildArguments(BatchJob job, BatchOptions options)
	{
		List<string> args = new List<string> { "patch", job.Kind, options.OriginalPath };
		args.AddRange(job.Inputs);
		switch (job.Kind)
		{
		case "apply":
		case "create":
			args.Add(job.Outputs[0]);
			if ((job.Kind == "apply" && options.XdeltaFallback) || (job.Kind == "create" && options.IncludeXdeltaFallback))
			{
				args.Add("--xdelta-fallback");
			}
			if (job.Kind == "create" && options.CreateXdelta)
			{
				args.Add("--xdelta");
			}
			break;
		case "merge":
		{
			string patchOutput = job.Outputs.FirstOrDefault(IsG3MPatchPath);
			string dataOutput = job.Outputs.FirstOrDefault((string path) => !IsG3MPatchPath(path));
			if (patchOutput != null)
			{
				args.Add("--out");
				args.Add(patchOutput);
			}
			if (dataOutput != null)
			{
				args.Add("--apply");
				args.Add(dataOutput);
			}
			if (options.UseCodeMerge)
			{
				args.Add("--code");
			}
			if (options.UsePropertyMerge)
			{
				args.Add("--properties");
			}
			string report = GetReportPath(job, options);
			if (report != null)
			{
				args.Add("--report");
				args.Add(report);
			}
			break;
		}
		}
		string cacheDirectory = options.CacheOptions?.ReadDirectory;
		if (!string.IsNullOrWhiteSpace(cacheDirectory) && string.Equals(cacheDirectory, options.CacheOptions?.WriteDirectory, StringComparison.OrdinalIgnoreCase))
		{
			args.Add("--cache");
			args.Add(cacheDirectory);
		}
		return args;
	}

	private static string? GetReportPath(BatchJob job, BatchOptions options)
	{
		if (job.Kind != "merge" || !options.WriteReports)
		{
			return null;
		}
		return Path.ChangeExtension(job.Outputs.FirstOrDefault(IsG3MPatchPath) ?? job.Outputs.FirstOrDefault() ?? "merge", ".merge_log.md");
	}

	private static async Task<BatchItemResult> ExecuteApplyJobAsync(BatchJob job, BatchOptions options)
	{
		Stopwatch sw = Stopwatch.StartNew();
		try
		{
			string input = job.Inputs[0];
			string output = job.Outputs[0];
			string tempDir = null;
			try
			{
				tempDir = Path.Combine(Path.GetTempPath(), $"g3mtool_batch_apply_{Guid.NewGuid():N}");
				File.Copy(await PatchInputService.MaterializeDataAsync(options.OriginalPath, input, tempDir), output, overwrite: true);
			}
			finally
			{
				TryDeleteDirectory(tempDir);
			}
			return Succeeded(job, sw.Elapsed.TotalSeconds);
		}
		catch (Exception ex)
		{
			return Failed(job, ex.Message, sw.Elapsed.TotalSeconds);
		}
	}

	private static async Task<BatchItemResult> ExecuteCreateJobAsync(BatchJob job, BatchOptions options)
	{
		Stopwatch sw = Stopwatch.StartNew();
		string tempDir = null;
		try
		{
			tempDir = Path.Combine(Path.GetTempPath(), $"g3mtool_batch_create_{Guid.NewGuid():N}");
			Directory.CreateDirectory(tempDir);
			string materialized = await PatchInputService.MaterializeDataAsync(options.OriginalPath, job.Inputs[0], tempDir);
			if (options.CreateXdelta)
			{
				XDeltaResult xdeltaResult = await new XDeltaService().CreatePatchAsync(options.OriginalPath, materialized, job.Outputs[0]);
				return xdeltaResult.Success ? Succeeded(job, sw.Elapsed.TotalSeconds) : Failed(job, xdeltaResult.Error ?? "xdelta create failed", sw.Elapsed.TotalSeconds);
			}
			PatchCreateResult createResult = await PatchService.CreatePatchAsync(options.OriginalPath, materialized, job.Outputs[0], null, null, null, null, null, options.IncludeXdeltaFallback, options.CacheOptions);
			return createResult.Success ? Succeeded(job, sw.Elapsed.TotalSeconds) : Failed(job, createResult.Error ?? "patch create failed", sw.Elapsed.TotalSeconds);
		}
		catch (Exception ex)
		{
			return Failed(job, ex.Message, sw.Elapsed.TotalSeconds);
		}
		finally
		{
			TryDeleteDirectory(tempDir);
		}
	}

	private static async Task<BatchItemResult> ExecuteMergeJobAsync(BatchJob job, BatchOptions options)
	{
		Stopwatch sw = Stopwatch.StartNew();
		try
		{
			string patchOutputPath = job.Outputs.FirstOrDefault(IsG3MPatchPath);
			string dataOutputPath = job.Outputs.FirstOrDefault((string path) => !IsG3MPatchPath(path));
			string reportBasePath = patchOutputPath ?? dataOutputPath ?? job.Outputs[0];
			string reportPath = (options.WriteReports ? Path.ChangeExtension(reportBasePath, ".merge_log.md") : null);
			MergeResult mergeResult = await MergeService.MergePatchesAsync(options.OriginalPath, job.Inputs.ToList(), new MergeOptions
			{
				OutputPath = patchOutputPath,
				ApplyPath = dataOutputPath,
				UseCodeMerge = options.UseCodeMerge,
				UsePropertyMerge = options.UsePropertyMerge,
				ReportPath = reportPath,
				CacheOptions = options.CacheOptions
			});
			if (!mergeResult.Success)
			{
				return Failed(job, mergeResult.Error ?? "patch merge failed", sw.Elapsed.TotalSeconds);
			}
			string[] outputs = job.Outputs;
			string implicitReportPath = Path.ChangeExtension(reportBasePath, ".merge_log.md");
			if (File.Exists(implicitReportPath))
			{
				string[] outputs2 = job.Outputs;
				int num = 0;
				string[] array = new string[1 + outputs2.Length];
				ReadOnlySpan<string> readOnlySpan = new ReadOnlySpan<string>(outputs2);
				readOnlySpan.CopyTo(new Span<string>(array).Slice(num, readOnlySpan.Length));
				num += readOnlySpan.Length;
				array[num] = implicitReportPath;
				outputs = array;
			}
			return Succeeded(job with
			{
				Outputs = outputs
			}, sw.Elapsed.TotalSeconds);
		}
		catch (Exception ex)
		{
			return Failed(job, ex.Message, sw.Elapsed.TotalSeconds);
		}
	}

	private static BatchItemResult Succeeded(BatchJob job, double seconds)
	{
		return new BatchItemResult
		{
			Index = job.Index,
			Kind = job.Kind,
			Key = job.Key,
			Inputs = job.Inputs,
			Outputs = job.Outputs,
			Success = true,
			Seconds = seconds
		};
	}

	private static BatchItemResult Failed(BatchJob job, string? error, double seconds, bool deduplicated = false)
	{
		return new BatchItemResult
		{
			Index = job.Index,
			Kind = job.Kind,
			Key = job.Key,
			Inputs = job.Inputs,
			Outputs = job.Outputs,
			Success = false,
			Deduplicated = deduplicated,
			Error = error,
			Seconds = seconds
		};
	}

	private static void ValidateOriginal(string originalPath)
	{
		if (!File.Exists(originalPath))
		{
			throw new FileNotFoundException("Original data file not found: " + originalPath, originalPath);
		}
	}

	private static string RequireOutDir(BatchOptions options)
	{
		if (string.IsNullOrWhiteSpace(options.OutDir))
		{
			throw new ArgumentException("--out-dir is required for this batch command.");
		}
		return Path.GetFullPath(options.OutDir);
	}

	private static bool IsG3MPatchPath(string path)
	{
		return Path.GetExtension(path).Equals(".g3mpatch", StringComparison.OrdinalIgnoreCase);
	}

	private static List<string> NormalizeExistingFiles(IEnumerable<string> paths, string label)
	{
		List<string> result = new List<string>();
		foreach (string path in paths)
		{
			string text = path.Trim();
			if (text.Length == 0)
			{
				throw new ArgumentException("Empty " + label + " path is not allowed.");
			}
			string fullPath = Path.GetFullPath(text);
			if (!File.Exists(fullPath))
			{
				throw new FileNotFoundException(label + " file not found: " + fullPath, fullPath);
			}
			result.Add(fullPath);
		}
		return result;
	}

	private static List<List<string>> ParseMergeSets(IReadOnlyList<string> setSpecs)
	{
		if (setSpecs.Count == 0)
		{
			throw new ArgumentException("At least one merge set is required.");
		}
		List<List<string>> sets = new List<List<string>>();
		foreach (string spec in setSpecs)
		{
			if (spec.Contains(';'))
			{
				throw new ArgumentException("Merge sets use comma separators only; semicolons are not supported.");
			}
			string[] parts = spec.Split(',', StringSplitOptions.TrimEntries);
			if (parts.Any((string part) => part.Length == 0))
			{
				throw new ArgumentException("Merge set has an empty path: " + spec);
			}
			if (parts.Length < 2)
			{
				throw new ArgumentException("Merge set must contain at least 2 patches: " + spec);
			}
			sets.Add(parts.ToList());
		}
		return sets;
	}

	private static List<string> BuildUniqueOutputPaths(IEnumerable<string> candidates)
	{
		HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> result = new List<string>();
		foreach (string candidate in candidates)
		{
			string unique;
			string path = (unique = candidate);
			string dir = Path.GetDirectoryName(path) ?? "";
			string stem = Path.GetFileNameWithoutExtension(path);
			string ext = Path.GetExtension(path);
			int suffix = 2;
			while (!used.Add(unique))
			{
				unique = Path.Combine(dir, $"{stem}_{suffix}{ext}");
				suffix++;
			}
			result.Add(unique);
		}
		return result;
	}

	private static string BuildMergeSetName(int index, IReadOnlyList<string> inputs)
	{
		IEnumerable<string> pieces = (from path in inputs
			select SanitizeFileName(Path.GetFileNameWithoutExtension(path)) into piece
			where piece.Length > 0
			select piece).Take(5);
		string suffix = string.Join("_", pieces);
		if (suffix.Length > 120)
		{
			suffix = suffix.Substring(0, 120).TrimEnd(new char[3] { '_', '.', ' ' });
		}
		if (suffix.Length == 0)
		{
			return $"merge_{index:000}";
		}
		return $"merge_{index:000}_{suffix}";
	}

	private static string SanitizeFileName(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		StringBuilder builder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			builder.Append(invalid.Contains(c) ? '_' : c);
		}
		return builder.ToString().Trim(new char[3] { ' ', '.', '_' });
	}

	private static string GetDataOutputExtension(string originalPath)
	{
		string extension = Path.GetExtension(originalPath);
		if (!string.IsNullOrWhiteSpace(extension))
		{
			return extension;
		}
		return ".win";
	}

	private static string BuildKey(params string[] parts)
	{
		return string.Join("|", parts.Select((string part) => part.Replace("|", "||", StringComparison.Ordinal)));
	}

	private static async Task<Dictionary<string, string>> ComputeFileHashesAsync(IEnumerable<string> paths)
	{
		string[] source = paths.Select(Path.GetFullPath).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		ConcurrentDictionary<string, string> hashes = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		await Parallel.ForEachAsync(source, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Min(4, Math.Max(1, Environment.ProcessorCount / 2))
		}, async delegate(string path, CancellationToken _)
		{
			ConcurrentDictionary<string, string> concurrentDictionary = hashes;
			concurrentDictionary[path] = await HashService.ComputeFileHashAsync(path);
		});
		return new Dictionary<string, string>(hashes, StringComparer.OrdinalIgnoreCase);
	}

	private static string[] CopyOutputs(IReadOnlyList<string> sourceOutputs, IReadOnlyList<string> targetOutputs)
	{
		if (sourceOutputs.Count < targetOutputs.Count)
		{
			throw new InvalidOperationException("Cannot duplicate batch result: source output count mismatch.");
		}
		List<string> copied = new List<string>();
		for (int i = 0; i < sourceOutputs.Count; i++)
		{
			string source = sourceOutputs[i];
			string target = ((i < targetOutputs.Count) ? targetOutputs[i] : DeriveDuplicateExtraOutputPath(source, targetOutputs));
			if (!File.Exists(source))
			{
				throw new FileNotFoundException("Cached batch output missing: " + source, source);
			}
			string dir = Path.GetDirectoryName(target);
			if (!string.IsNullOrWhiteSpace(dir))
			{
				Directory.CreateDirectory(dir);
			}
			File.Copy(source, target, overwrite: true);
			copied.Add(target);
		}
		return copied.ToArray();
	}

	private static string DeriveDuplicateExtraOutputPath(string sourceExtraOutput, IReadOnlyList<string> targetOutputs)
	{
		if (targetOutputs.Count == 0)
		{
			throw new InvalidOperationException("Cannot derive duplicate output path without primary output.");
		}
		if (Path.GetFileName(sourceExtraOutput).EndsWith(".merge_log.md", StringComparison.OrdinalIgnoreCase))
		{
			return Path.ChangeExtension(targetOutputs[0], ".merge_log.md");
		}
		return Path.Combine(Path.GetDirectoryName(targetOutputs[0]) ?? "", Path.GetFileName(sourceExtraOutput));
	}

	private static void TryDeleteDirectory(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
		}
	}
}
