using System.Diagnostics;
using System.IO;

namespace G3MLib.Modding.XDelta;

public class XDeltaService
{
	private readonly string? _configuredPath;
	private readonly string? _xdeltaPath;
	private readonly TimeSpan _processTimeout;

	public static string? DefaultExecutablePath { get => _defaultExecutablePath; set => _defaultExecutablePath = value; }
	private static volatile string? _defaultExecutablePath;
	private static readonly AsyncLocal<ExecutableScope?> CurrentExecutable = new();

	/// <summary>Overrides the xdelta executable for the current async operation without changing the process default.</summary>
	public static IDisposable UseExecutable(string executablePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
		var scope = new ExecutableScope(CurrentExecutable.Value, Path.GetFullPath(executablePath));
		CurrentExecutable.Value = scope;
		return scope;
	}

	private sealed class ExecutableScope(ExecutableScope? previous, string path) : IDisposable
	{
		public string Path { get; } = path;
		public void Dispose()
		{
			if (ReferenceEquals(CurrentExecutable.Value, this)) CurrentExecutable.Value = previous;
		}
	}

	public XDeltaService()
		: this(CurrentExecutable.Value?.Path ?? DefaultExecutablePath, TimeSpan.FromMinutes(5L))
	{
	}

	public XDeltaService(string? executablePath, TimeSpan processTimeout)
	{
		_configuredPath = executablePath;
		_xdeltaPath = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath) ? Path.GetFullPath(executablePath) : null;
		_processTimeout = processTimeout;
	}

	public async Task<XDeltaResult> CreatePatchAsync(string originalPath, string modifiedPath, string outputPath)
	{
		if (_xdeltaPath == null)
			return FailedExecutableResult();
		if (!File.Exists(originalPath))
			return new XDeltaResult { Success = false, Error = "Original file not found: " + originalPath };
		if (!File.Exists(modifiedPath))
			return new XDeltaResult { Success = false, Error = "Modified file not found: " + modifiedPath };

		if (TryEnsureOutputDirectory(outputPath) is { } failure)
			return failure;
		return await ExecuteXDeltaAsync(["-e", "-s", Path.GetFullPath(originalPath), Path.GetFullPath(modifiedPath), Path.GetFullPath(outputPath)]).ConfigureAwait(false);
	}

	public async Task<XDeltaResult> ApplyPatchAsync(string originalPath, string patchPath, string outputPath)
	{
		if (_xdeltaPath == null)
			return FailedExecutableResult();
		if (!File.Exists(originalPath))
			return new XDeltaResult { Success = false, Error = "Original file not found: " + originalPath };
		if (!File.Exists(patchPath))
			return new XDeltaResult { Success = false, Error = "Patch file not found: " + patchPath };

		if (TryEnsureOutputDirectory(outputPath) is { } failure)
			return failure;
		return await ExecuteXDeltaAsync(["-d", "-s", Path.GetFullPath(originalPath), Path.GetFullPath(patchPath), Path.GetFullPath(outputPath)]).ConfigureAwait(false);
	}

	public Task<XDeltaResult> ExecuteRawAsync(string[] args)
	{
		ArgumentNullException.ThrowIfNull(args);
		return _xdeltaPath == null ? Task.FromResult(FailedExecutableResult()) : ExecuteXDeltaAsync(args);
	}

	private async Task<XDeltaResult> ExecuteXDeltaAsync(string[] args)
	{
		try
		{
			ProcessStartInfo startInfo = new()
			{
				FileName = _xdeltaPath,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			foreach (var arg in args)
				startInfo.ArgumentList.Add(arg);

			using var process = Process.Start(startInfo);
			if (process == null)
				return new XDeltaResult { Success = false, Error = "Failed to start xdelta process" };

			var outputTask = process.StandardOutput.ReadToEndAsync();
			var errorTask = process.StandardError.ReadToEndAsync();
			using CancellationTokenSource timeoutCts = new(_processTimeout);
			try
			{
				await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				try
				{
					process.Kill(entireProcessTree: true);
					await process.WaitForExitAsync().ConfigureAwait(false);
				}
				catch
				{
				}
				return new XDeltaResult
				{
					Success = false,
					Error = $"xdelta timed out after {_processTimeout.TotalSeconds:F0}s and was terminated",
					Output = await outputTask.WaitAsync(TimeSpan.FromSeconds(2L)).ConfigureAwait(false) + await errorTask.WaitAsync(TimeSpan.FromSeconds(2L)).ConfigureAwait(false)
				};
			}

			var output = await outputTask.ConfigureAwait(false);
			var error = await errorTask.ConfigureAwait(false);
			if (process.ExitCode == 0)
				return new XDeltaResult { Success = true, Output = output + error };

			var exitMessage = $"xdelta exited with code {process.ExitCode}";
			return new XDeltaResult
			{
				Success = false,
				Error = string.IsNullOrEmpty(error) ? exitMessage : error.TrimEnd() + Environment.NewLine + exitMessage,
				Output = output
			};
		}
		catch (Exception ex)
		{
			return new XDeltaResult { Success = false, Error = ex.Message };
		}
	}

	private XDeltaResult FailedExecutableResult() => new()
	{
		Success = false,
		Error = string.IsNullOrWhiteSpace(_configuredPath) ? "xdelta executable path is not configured" : "xdelta executable not found: " + _configuredPath
	};

	private static void EnsureOutputDirectory(string outputPath)
	{
		var outputDirectory = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outputDirectory))
			Directory.CreateDirectory(outputDirectory);
	}

	private static XDeltaResult? TryEnsureOutputDirectory(string outputPath)
	{
		try
		{
			EnsureOutputDirectory(outputPath);
			return null;
		}
		catch (Exception ex)
		{
			return new XDeltaResult { Success = false, Error = ex.Message };
		}
	}
}

public sealed class XDeltaResult
{
	public bool Success { get; set; }
	public string? Error { get; set; }
	public string? Output { get; set; }
}
