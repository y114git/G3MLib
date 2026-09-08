using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace G3MLib.Modding.XDelta;

public class XDeltaService
{
	private readonly string? _requestedPath;

	private readonly string? _xdeltaPath;

	private readonly bool _ownsTemporaryExecutable;

	private readonly string? _temporaryDirectory;

	private readonly TimeSpan _processTimeout;

	public static string? DefaultRequestedPath { get; set; }

	public XDeltaService()
		: this(DefaultRequestedPath, TimeSpan.FromMinutes(5L))
	{
	}

	public XDeltaService(string? requestedPath, TimeSpan processTimeout)
	{
		_requestedPath = requestedPath;
		PlatformUtil.XDeltaPathInfo xdeltaPathInfo = PlatformUtil.GetXDeltaPath(requestedPath);
		_xdeltaPath = xdeltaPathInfo?.Path;
		_ownsTemporaryExecutable = xdeltaPathInfo?.IsTemporary ?? false;
		_temporaryDirectory = xdeltaPathInfo?.TempDirectory;
		_processTimeout = processTimeout;
	}

	public async Task<XDeltaResult> CreatePatchAsync(string originalPath, string modifiedPath, string outputPath)
	{
		if (_xdeltaPath == null)
		{
			return new XDeltaResult
			{
				Success = false,
				Error = GetExecutableNotFoundError()
			};
		}
		if (!File.Exists(originalPath))
		{
			return new XDeltaResult
			{
				Success = false,
				Error = "Original file not found: " + originalPath
			};
		}
		if (!File.Exists(modifiedPath))
		{
			return new XDeltaResult
			{
				Success = false,
				Error = "Modified file not found: " + modifiedPath
			};
		}
		string outputDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
		{
			Directory.CreateDirectory(outputDir);
		}
		string[] args = new string[5] { "-e", "-s", originalPath, modifiedPath, outputPath };
		return await ExecuteXDeltaAsync(args);
	}

	public async Task<XDeltaResult> ApplyPatchAsync(string originalPath, string patchPath, string outputPath)
	{
		if (_xdeltaPath == null)
		{
			return new XDeltaResult
			{
				Success = false,
				Error = GetExecutableNotFoundError()
			};
		}
		if (!File.Exists(originalPath))
		{
			return new XDeltaResult
			{
				Success = false,
				Error = "Original file not found: " + originalPath
			};
		}
		if (!File.Exists(patchPath))
		{
			return new XDeltaResult
			{
				Success = false,
				Error = "Patch file not found: " + patchPath
			};
		}
		string outputDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
		{
			Directory.CreateDirectory(outputDir);
		}
		string[] args = new string[5] { "-d", "-s", originalPath, patchPath, outputPath };
		return await ExecuteXDeltaAsync(args);
	}

	public async Task<XDeltaResult> ExecuteRawAsync(string[] args)
	{
		if (_xdeltaPath == null)
		{
			return new XDeltaResult
			{
				Success = false,
				Error = GetExecutableNotFoundError()
			};
		}
		return await ExecuteXDeltaAsync(args);
	}

	private async Task<XDeltaResult> ExecuteXDeltaAsync(string[] args)
	{
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = _xdeltaPath,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			foreach (string arg in args)
			{
				startInfo.ArgumentList.Add(arg);
			}
			using Process process = Process.Start(startInfo);
			if (process == null)
			{
				return new XDeltaResult
				{
					Success = false,
					Error = "Failed to start xdelta process"
				};
			}
			Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
			Task<string> errorTask = process.StandardError.ReadToEndAsync();
			using CancellationTokenSource timeoutCts = new CancellationTokenSource(_processTimeout);
			try
			{
				await process.WaitForExitAsync(timeoutCts.Token);
			}
			catch (OperationCanceledException)
			{
				try
				{
					process.Kill(entireProcessTree: true);
					await process.WaitForExitAsync();
				}
				catch
				{
				}
				string timedOutOutput = await outputTask.WaitAsync(TimeSpan.FromSeconds(2L)).ConfigureAwait(continueOnCapturedContext: false);
				string timedOutError = await errorTask.WaitAsync(TimeSpan.FromSeconds(2L)).ConfigureAwait(continueOnCapturedContext: false);
				return new XDeltaResult
				{
					Success = false,
					Error = $"xdelta timed out after {_processTimeout.TotalSeconds:F0}s and was terminated",
					Output = timedOutOutput + timedOutError
				};
			}
			string output = await outputTask;
			string error = await errorTask;
			if (process.ExitCode == 0)
			{
				return new XDeltaResult
				{
					Success = true,
					Output = output
				};
			}
			string exitMessage = $"xdelta exited with code {process.ExitCode}";
			return new XDeltaResult
			{
				Success = false,
				Error = (string.IsNullOrEmpty(error) ? (exitMessage + ". If the bundled xdelta is blocked or incompatible, pass --xdelta-path <path>.") : (error.TrimEnd() + Environment.NewLine + exitMessage)),
				Output = output
			};
		}
		catch (Exception ex2)
		{
			return new XDeltaResult
			{
				Success = false,
				Error = ex2.Message + ". If the bundled xdelta is blocked or incompatible, pass --xdelta-path <path>."
			};
		}
		finally
		{
			CleanupTemporaryExecutable();
		}
	}

	private void CleanupTemporaryExecutable()
	{
		if (!_ownsTemporaryExecutable)
		{
			return;
		}
		try
		{
			if (!string.IsNullOrEmpty(_xdeltaPath) && File.Exists(_xdeltaPath))
			{
				File.Delete(_xdeltaPath);
			}
		}
		catch
		{
		}
		try
		{
			if (!string.IsNullOrEmpty(_temporaryDirectory) && Directory.Exists(_temporaryDirectory) && !Directory.EnumerateFileSystemEntries(_temporaryDirectory).Any())
			{
				Directory.Delete(_temporaryDirectory);
			}
		}
		catch
		{
		}
	}

	private string GetExecutableNotFoundError()
	{
		if (!string.IsNullOrWhiteSpace(_requestedPath))
		{
			return "xdelta executable not found: " + _requestedPath;
		}
		return "xdelta executable not found";
	}
}

public sealed class XDeltaResult
{
	public bool Success { get; set; }
	public string? Error { get; set; }
	public string? Output { get; set; }
}
