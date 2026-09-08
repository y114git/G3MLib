using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace G3MLib.Modding.Internal;

public static class PlatformUtil
{
	public sealed class XDeltaPathInfo
	{
		public required string Path { get; init; }

		public bool IsTemporary { get; init; }

		public string? TempDirectory { get; init; }

		public string? DynamicLibraryDirectory { get; init; }
	}

	public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

	public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

	public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

	public static string GetPlatformName()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return "win";
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			return "linux";
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			if (RuntimeInformation.OSArchitecture != Architecture.Arm64)
			{
				return "mac-x64";
			}
			return "mac-arm64";
		}
		return "unknown";
	}

	public static XDeltaPathInfo? GetXDeltaPath(string? overridePath = null)
	{
		string platform = GetPlatformName();
		string exeName = ((platform == "win") ? "xdelta.exe" : "xdelta");
		if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
		{
			SetExecutablePermission(overridePath, platform);
			return new XDeltaPathInfo
			{
				Path = overridePath,
				IsTemporary = false
			};
		}
		XDeltaPathInfo extracted = ExtractXDeltaFromResource(platform, exeName);
		if (extracted != null)
		{
			return extracted;
		}
		string[] searchPaths =
		[
			Path.Combine(AppContext.BaseDirectory, "Assets", "bin", platform, exeName),
			Path.Combine(Environment.CurrentDirectory, "Assets", "bin", platform, exeName),
			Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "bin", platform, exeName)
		];
		foreach (string path in searchPaths)
		{
			if (File.Exists(path))
			{
				SetExecutablePermission(path, platform);
				return new XDeltaPathInfo
				{
					Path = path,
					IsTemporary = false
				};
			}
		}
		return null;
	}

	private static XDeltaPathInfo? ExtractXDeltaFromResource(string platform, string exeName)
	{
		Assembly executingAssembly = Assembly.GetEntryAssembly() ?? typeof(PlatformUtil).Assembly;
		string resourceName = "G3MToolCLI.Assets.bin." + platform.Replace('-', '_') + "." + exeName;
		using Stream stream = executingAssembly.GetManifestResourceStream(resourceName);
		if (stream == null)
		{
			return null;
		}
		string tempDir = Path.Combine(Path.GetTempPath(), $"g3mtool_xdelta_{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		string outputPath = Path.Combine(tempDir, exeName);
		using (FileStream fileStream = File.Create(outputPath))
		{
			stream.CopyTo(fileStream);
		}
		SetExecutablePermission(outputPath, platform);
		return new XDeltaPathInfo
		{
			Path = outputPath,
			IsTemporary = true,
			TempDirectory = tempDir
		};
	}

	private static void SetExecutablePermission(string path, string platform)
	{
		if (platform != "win" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			try
			{
				File.SetUnixFileMode(path, UnixFileMode.OtherExecute | UnixFileMode.OtherRead | UnixFileMode.GroupExecute | UnixFileMode.GroupRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite | UnixFileMode.UserRead);
			}
			catch
			{
			}
		}
	}

	public static string GetExecutableExtension()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return "";
		}
		return ".exe";
	}

	public static string GetExecutableDirectory()
	{
		string processPath = Environment.ProcessPath;
		if (!string.IsNullOrEmpty(processPath))
		{
			string dir = Path.GetDirectoryName(processPath);
			if (!string.IsNullOrEmpty(dir))
			{
				return dir;
			}
		}
		string assemblyLocation = Assembly.GetExecutingAssembly().Location;
		if (!string.IsNullOrEmpty(assemblyLocation))
		{
			string dir2 = Path.GetDirectoryName(assemblyLocation);
			if (!string.IsNullOrEmpty(dir2))
			{
				return dir2;
			}
		}
		return AppContext.BaseDirectory;
	}
}
