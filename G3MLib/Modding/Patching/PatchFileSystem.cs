using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace G3MLib.Modding.Patching;

public sealed class PatchFileSystem
{

	private readonly Dictionary<string, byte[]> _files;

	private readonly Dictionary<string, string[]> _childDirs;

	private readonly Dictionary<string, string[]> _childFiles;

	private static readonly JsonSerializerOptions s_jsonOptions = new()
	{
		WriteIndented = true
	};

	public Dictionary<string, string> GmlEntries { get; } = [];

	public Dictionary<string, string> AsmEntries { get; } = [];

	public Dictionary<string, string> AsmEntryPaths { get; } = [];

	public Dictionary<string, string> CodeEntryLogicalNames { get; } = [];

	public G3MPatchManifest? Manifest { get; private set; }

	public string HelpersPrefix { get; private set; } = "Helpers";

	public byte[]? ExactPatchBytes { get; private set; }

	public string? ExactPatchPath { get; private set; }

	public int FileCount => _files.Count;

	public int CodeEntryCount => GmlEntries.Count + AsmEntries.Count;

	public PatchFileSystem(int initialCapacity = 0)
	{
		_files = new Dictionary<string, byte[]>(initialCapacity, StringComparer.OrdinalIgnoreCase);
		_childDirs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
		_childFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
	}

	private static string Norm(string path)
	{
		return path.Replace('\\', '/').TrimEnd('/');
	}

	private static string DecodeUtf8(byte[] bytes)
	{
		if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191)
		{
			return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
		}
		return Encoding.UTF8.GetString(bytes);
	}

	public static PatchFileSystem LoadFromZip(string zipPath, bool loadExactPayloads = true, bool skipAsmBackedGml = false)
	{
		var fileInfo = new FileInfo(zipPath);
		if (fileInfo.Length > ModConfig.MaxPayloadBytes)
		{
			throw new InvalidDataException($"Payload file exceeds maximum allowed size of {ModConfig.MaxPayloadBytes} bytes.");
		}
		var sw = Stopwatch.StartNew();
		using var fileStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan);
		return LoadFromSeekableZipStream(fileStream, loadExactPayloads, skipAsmBackedGml, sw);
	}

	public static PatchFileSystem LoadFromZip(Stream zipStream, bool loadExactPayloads = true, bool skipAsmBackedGml = false)
	{
		ArgumentNullException.ThrowIfNull(zipStream);
		var sw = Stopwatch.StartNew();
		if (!zipStream.CanSeek)
		{
			using var bufferedStream = new MemoryStream();
			var buffer = new byte[65536];
			long totalRead = 0;
			int bytesRead;
			while ((bytesRead = zipStream.Read(buffer, 0, buffer.Length)) > 0)
			{
				totalRead += bytesRead;
				if (totalRead > ModConfig.MaxPayloadBytes)
				{
					throw new InvalidDataException($"Payload stream exceeds maximum allowed size of {ModConfig.MaxPayloadBytes} bytes.");
				}
				bufferedStream.Write(buffer, 0, bytesRead);
			}
			bufferedStream.Position = 0;
			return LoadFromSeekableZipStream(bufferedStream, loadExactPayloads, skipAsmBackedGml, sw);
		}
		if (zipStream.Length - zipStream.Position > ModConfig.MaxPayloadBytes)
		{
			throw new InvalidDataException($"Payload stream exceeds maximum allowed size of {ModConfig.MaxPayloadBytes} bytes.");
		}
		return LoadFromSeekableZipStream(zipStream, loadExactPayloads, skipAsmBackedGml, sw);
	}

	private static PatchFileSystem LoadFromSeekableZipStream(Stream archiveStream, bool loadExactPayloads, bool skipAsmBackedGml, Stopwatch sw)
	{
		var zipLength = archiveStream.Length;
		var zipStream = archiveStream;
		using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
		var pfs = new PatchFileSystem(archive.Entries.Count);
		HashSet<string>? asmBackedCodeKeys = null;
		if (skipAsmBackedGml)
		{
			asmBackedCodeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var entry in archive.Entries)
			{
				if (!string.IsNullOrEmpty(entry.Name) && TryGetCodeEntryInfo(Norm(entry.FullName), entry.Name, out var codeKey, out var extension) && extension.Equals(".asm", StringComparison.OrdinalIgnoreCase))
				{
					asmBackedCodeKeys.Add(codeKey);
				}
			}
		}
		foreach (var entry2 in archive.Entries)
		{
			if (string.IsNullOrEmpty(entry2.Name))
			{
				continue;
			}
			var fullName = Norm(entry2.FullName);
			if (!loadExactPayloads && (fullName.StartsWith("Xdelta/", StringComparison.OrdinalIgnoreCase) || fullName.StartsWith("Exact/", StringComparison.OrdinalIgnoreCase)) || asmBackedCodeKeys != null && TryGetCodeEntryInfo(fullName, entry2.Name, out var earlyCodeKey, out var earlyExtension) && earlyExtension.Equals(".gml", StringComparison.OrdinalIgnoreCase) && asmBackedCodeKeys.Contains(earlyCodeKey))
			{
				continue;
			}
			byte[] bytes;
			if (entry2.Length > ModConfig.MaxPayloadBytes)
			{
				throw new InvalidDataException($"Archive entry '{entry2.FullName}' exceeds maximum allowed size.");
			}
			using (var stream = entry2.Open())
			{
				if (entry2.Length > 0)
				{
					bytes = new byte[entry2.Length];
					stream.ReadExactly(bytes);
				}
				else
				{
					using var buf = new MemoryStream();
					var buffer = new byte[65536];
					long totalRead = 0;
					int bytesRead;
					while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
					{
						totalRead += bytesRead;
						if (totalRead > ModConfig.MaxPayloadBytes)
						{
							throw new InvalidDataException($"Archive entry '{entry2.FullName}' exceeds maximum allowed size.");
						}
						buf.Write(buffer, 0, bytesRead);
					}
					bytes = buf.ToArray();
				}
			}
			if (fullName.Equals("g3mpatch.json", StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					pfs.Manifest = JsonSerializer.Deserialize<G3MPatchManifest>(DecodeUtf8(bytes));
				}
				catch
				{
				}
				pfs._files[fullName] = bytes;
			}
			else if (fullName.StartsWith("CodeEntries/", StringComparison.OrdinalIgnoreCase))
			{
				if (TryGetCodeEntryInfo(fullName, entry2.Name, out var codeKey2, out var extension2))
				{
					var logicalName = Path.GetFileNameWithoutExtension(entry2.Name);
					var content = DecodeUtf8(bytes);
					if (extension2.Equals(".gml", StringComparison.OrdinalIgnoreCase))
					{
						pfs.GmlEntries[codeKey2] = content;
					}
					else if (extension2.Equals(".asm", StringComparison.OrdinalIgnoreCase))
					{
						pfs.AsmEntries[codeKey2] = content;
						pfs.AsmEntryPaths[codeKey2] = fullName;
					}
					pfs.CodeEntryLogicalNames[codeKey2] = logicalName;
				}
			}
			else if (fullName.StartsWith("Xdelta/", StringComparison.OrdinalIgnoreCase) || fullName.StartsWith("Exact/", StringComparison.OrdinalIgnoreCase))
			{
				if (pfs.ExactPatchBytes == null && entry2.Name.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase))
				{
					pfs.ExactPatchBytes = bytes;
					pfs.ExactPatchPath = fullName;
				}
				else if (!entry2.Name.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase))
				{
					LogService.Warning($"[PatchFileSystem] Ignoring non-xdelta fallback entry '{entry2.Name}' at '{fullName}'");
				}
			}
			else
			{
				pfs._files[fullName] = bytes;
			}
		}
		var hasHelpers = false;
		var hasAssetOrder = false;
		foreach (var key in pfs._files.Keys)
		{
			if (!hasHelpers && key.StartsWith("Helpers/", StringComparison.OrdinalIgnoreCase))
			{
				hasHelpers = true;
			}
			else if (!hasAssetOrder && key.StartsWith("AssetOrder/", StringComparison.OrdinalIgnoreCase))
			{
				hasAssetOrder = true;
			}
			if (hasHelpers)
			{
				break;
			}
		}
		pfs.HelpersPrefix = hasHelpers ? "Helpers" : (hasAssetOrder ? "AssetOrder" : "Helpers");
		pfs.HydrateCodeEntryLogicalNamesFromHelpers();
		pfs.BuildDirectoryIndex();
		var totalBytes = pfs._files.Values.Sum((Func<byte[], long>)(b => b.Length));
		var handler = new LogInterpolatedStringHandler(74, 6, out var shouldAppend);
		if (shouldAppend)
		{
			handler.AppendLiteral("[PatchFileSystem] Loaded ");
			handler.AppendFormatted(pfs._files.Count);
			handler.AppendLiteral(" files + ");
			handler.AppendFormatted(pfs.GmlEntries.Count);
			handler.AppendLiteral(" GML + ");
			handler.AppendFormatted(pfs.AsmEntries.Count);
			handler.AppendLiteral(" ASM in ");
			handler.AppendFormatted(sw.Elapsed.TotalSeconds, "F1");
			handler.AppendLiteral("s (");
			handler.AppendFormatted(zipLength / 1024 / 1024);
			handler.AppendLiteral("MB patch -> ");
			handler.AppendFormatted(totalBytes / 1024 / 1024);
			handler.AppendLiteral("MB memory)");
		}
		LogService.Log(ref handler);
		return pfs;
	}

	private static bool TryGetCodeEntryInfo(string fullName, string entryName, out string codeKey, out string extension)
	{
		codeKey = "";
		extension = Path.GetExtension(entryName);
		if (!fullName.StartsWith("CodeEntries/", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		var relativePath = fullName["CodeEntries/".Length..];
		var extIndex = relativePath.LastIndexOf('.');
		if (extIndex <= 0)
		{
			return false;
		}
		var pathParts = relativePath[..extIndex].Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (pathParts.Length < 2)
		{
			return false;
		}
		codeKey = pathParts[^2].Equals(pathParts[^1], StringComparison.OrdinalIgnoreCase) ? pathParts[^2] : (pathParts[^2] + "/" + pathParts[^1]);
		return true;
	}

	private void BuildDirectoryIndex()
	{
		var dirSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		var fileSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var path in _files.Keys)
		{
			var lastSlash = path.LastIndexOf('/');
			var fileParent = (lastSlash < 0) ? "" : path[..lastSlash];
			if (!fileSets.TryGetValue(fileParent, out var fileSet))
			{
				fileSet = fileSets[fileParent] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
			fileSet.Add(path);
			var previousSlash = -1;
			for (var slash = path.IndexOf('/', StringComparison.Ordinal); slash >= 0; slash = path.IndexOf('/', slash + 1))
			{
				var parent = (previousSlash < 0) ? "" : path[..previousSlash];
				var child = path[..slash];
				if (!dirSets.TryGetValue(parent, out var dirSet))
				{
					dirSet = dirSets[parent] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				}
				dirSet.Add(child);
				previousSlash = slash;
			}
		}
		string key;
		HashSet<string> value;
		foreach (var item in dirSets)
		{
			item.Deconstruct(out key, out value);
			var parent2 = key;
			var children = value;
			_childDirs[parent2] = [.. children.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
		}
		foreach (var item2 in fileSets)
		{
			item2.Deconstruct(out key, out value);
			var parent3 = key;
			var files = value;
			_childFiles[parent3] = [.. files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
		}
	}

	private void HydrateCodeEntryLogicalNamesFromHelpers()
	{
		var helperPath = HelpersPrefix + "/variables_functions.json";
		if (!_files.TryGetValue(helperPath, out var helperBytes) && !_files.TryGetValue(HelpersPrefix + "/code_patch_metadata.json", out helperBytes))
		{
			return;
		}
		try
		{
			using var doc = JsonDocument.Parse(helperBytes);
			if (!doc.RootElement.TryGetProperty("codeEntries", out var codeEntries) || codeEntries.ValueKind != JsonValueKind.Array)
			{
				return;
			}
			foreach (var entry in codeEntries.EnumerateArray())
			{
				if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("key", out var keyElem) && entry.TryGetProperty("name", out var nameElem))
				{
					var key = keyElem.GetString();
					var name = nameElem.GetString();
					if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(name))
					{
						CodeEntryLogicalNames[key] = name;
					}
				}
			}
		}
		catch
		{
		}
	}

	public byte[] ReadAllBytes(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		var n = Norm(path);
		if (!_files.TryGetValue(n, out var bytes))
		{
			throw new FileNotFoundException("PatchFileSystem: file not found: " + path, path);
		}
		return bytes;
	}

	public string ReadAllText(string path)
	{
		return DecodeUtf8(ReadAllBytes(path));
	}

	public string[] ReadAllLines(string path)
	{
		var lines = ReadAllText(path).Split('\n');
		for (var i = 0; i < lines.Length; i++)
		{
			lines[i] = lines[i].TrimEnd('\r');
		}
		return lines;
	}

	public bool FileExists(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		return _files.ContainsKey(Norm(path));
	}

	public string[] GetDirectories(string dirPath)
	{
		ArgumentNullException.ThrowIfNull(dirPath);
		var n = Norm(dirPath);
		if (!_childDirs.TryGetValue(n, out var dirs))
		{
			return [];
		}
		return dirs;
	}

	public string[] GetFiles(string dirPath, string pattern = "*")
	{
		ArgumentNullException.ThrowIfNull(pattern);
		ArgumentNullException.ThrowIfNull(dirPath);
		var n = Norm(dirPath);
		if (!_childFiles.TryGetValue(n, out var files))
		{
			return [];
		}
		if (pattern is "*" or "*.*")
		{
			return files;
		}
		if (pattern.StartsWith("*.", StringComparison.Ordinal))
		{
			var ext = pattern[1..];
			return [.. files.Where(f => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))];
		}
		return files;
	}

	public bool DirectoryExists(string dirPath)
	{
		ArgumentNullException.ThrowIfNull(dirPath);
		var n = Norm(dirPath);
		if (!_childDirs.ContainsKey(n))
		{
			return _childFiles.ContainsKey(n);
		}
		return true;
	}

	public void AddFile(string path, byte[] data)
	{
		ArgumentNullException.ThrowIfNull(path);
		var n = Norm(path);
		_files[n] = data;
	}

	public void AddTextFile(string path, string content)
	{
		AddFile(path, Encoding.UTF8.GetBytes(content));
	}

	public bool RemoveFile(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		return _files.Remove(Norm(path));
	}

	public IEnumerable<string> GetAllFilePaths()
	{
		return _files.Keys;
	}

	public IReadOnlyDictionary<string, byte[]> GetAllFiles()
	{
		return _files;
	}

	public bool TryGetFile(string path, out byte[] data)
	{
		ArgumentNullException.ThrowIfNull(path);
		return _files.TryGetValue(Norm(path), out data);
	}

	public void AddGmlEntry(string entryKey, string gmlContent, string? logicalName = null)
	{
		GmlEntries[entryKey] = gmlContent;
		CodeEntryLogicalNames[entryKey] = logicalName ?? CodeEntryLogicalNames.GetValueOrDefault(entryKey) ?? Path.GetFileName(entryKey);
		RemoveFile($"CodeEntries/{entryKey}/{entryKey}.gml");
	}

	public void AddAsmEntry(string entryKey, string asmContent, string? archivePath = null, string? logicalName = null)
	{
		AsmEntries[entryKey] = asmContent;
		AsmEntryPaths[entryKey] = string.IsNullOrWhiteSpace(archivePath) ? $"CodeEntries/{entryKey}/{entryKey}.asm" : Norm(archivePath);
		CodeEntryLogicalNames[entryKey] = logicalName ?? CodeEntryLogicalNames.GetValueOrDefault(entryKey) ?? Path.GetFileName(entryKey);
		RemoveFile($"CodeEntries/{entryKey}/{entryKey}.asm");
	}

	public bool RemoveGmlEntry(string entryKey)
	{
		var result = GmlEntries.Remove(entryKey);
		if (!AsmEntries.ContainsKey(entryKey))
		{
			CodeEntryLogicalNames.Remove(entryKey);
		}
		return result;
	}

	public bool RemoveAsmEntry(string entryKey)
	{
		AsmEntryPaths.Remove(entryKey);
		var result = AsmEntries.Remove(entryKey);
		if (!GmlEntries.ContainsKey(entryKey))
		{
			CodeEntryLogicalNames.Remove(entryKey);
		}
		return result;
	}

	public void RebuildDirectoryIndex()
	{
		_childDirs.Clear();
		_childFiles.Clear();
		BuildDirectoryIndex();
	}

	public void SaveToZip(string outputPath, G3MPatchManifest? manifest = null)
	{
		var outputDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
		{
			Directory.CreateDirectory(outputDir);
		}
		using var zipStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, FileOptions.SequentialScan);
		using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
		if (manifest != null)
		{
			using var stream = archive.CreateEntry("g3mpatch.json", ArchiveCompressionUtil.GetLevel("g3mpatch.json")).Open();
			JsonSerializer.Serialize(stream, manifest, s_jsonOptions);
		}
		string key;
		foreach (var file in _files)
		{
			file.Deconstruct(out key, out var value);
			var path = key;
			var data = value;
			if ((!path.Equals("g3mpatch.json", StringComparison.OrdinalIgnoreCase) || manifest == null) && !path.StartsWith("CodeEntries/", StringComparison.OrdinalIgnoreCase))
			{
				using var stream2 = archive.CreateEntry(path, ArchiveCompressionUtil.GetLevel(path)).Open();
				stream2.Write(data, 0, data.Length);
			}
		}
		string value2;
		foreach (var gmlEntry in GmlEntries)
		{
			gmlEntry.Deconstruct(out key, out value2);
			var entryKey = key;
			var gml = value2;
			var entryPath = $"CodeEntries/{entryKey}/{entryKey}.gml";
			using var writer = new StreamWriter(archive.CreateEntry(entryPath, ArchiveCompressionUtil.GetLevel(entryPath)).Open(), Encoding.UTF8);
			writer.Write(gml);
		}
		foreach (var asmEntry in AsmEntries)
		{
			asmEntry.Deconstruct(out value2, out key);
			var entryKey2 = value2;
			var asm = key;
			var entryPath2 = AsmEntryPaths.GetValueOrDefault(entryKey2) ?? $"CodeEntries/{entryKey2}/{entryKey2}.asm";
			using var writer2 = new StreamWriter(archive.CreateEntry(entryPath2, ArchiveCompressionUtil.GetLevel(entryPath2)).Open(), Encoding.UTF8);
			writer2.Write(asm);
		}
	}

	public bool HasResourceType(string resourceType)
	{
		return DirectoryExists(resourceType);
	}

	public bool HasCodeEntries()
	{
		if (GmlEntries.Count <= 0)
		{
			return AsmEntries.Count > 0;
		}
		return true;
	}

	public void ReleaseFileData()
	{
		_files.Clear();
		_childDirs.Clear();
		_childFiles.Clear();
	}

	public void ReleaseCodeEntries()
	{
		GmlEntries.Clear();
		AsmEntries.Clear();
		AsmEntryPaths.Clear();
		CodeEntryLogicalNames.Clear();
	}

	public HashSet<string> GetResourceTypes()
	{
		var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (_childDirs.TryGetValue("", out var rootDirs))
		{
			var array = rootDirs;
			foreach (var dir in array)
			{
				types.Add(dir);
			}
		}
		if (HasCodeEntries())
		{
			types.Add("CodeEntries");
		}
		return types;
	}
}
