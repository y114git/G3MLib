using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace G3MLib.Modding.Patching;

public sealed class PatchFileSystem
{

	private readonly Dictionary<string, byte[]> _files;

	private readonly Dictionary<string, string[]> _childDirs;

	private readonly Dictionary<string, string[]> _childFiles;

	private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public Dictionary<string, string> GmlEntries { get; } = new Dictionary<string, string>();

	public Dictionary<string, string> AsmEntries { get; } = new Dictionary<string, string>();

	public Dictionary<string, string> AsmEntryPaths { get; } = new Dictionary<string, string>();

	public Dictionary<string, string> CodeEntryLogicalNames { get; } = new Dictionary<string, string>();

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
		Stopwatch sw = Stopwatch.StartNew();
		long zipLength = new FileInfo(zipPath).Length;
		using FileStream zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan);
		using ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
		PatchFileSystem pfs = new PatchFileSystem(archive.Entries.Count);
		HashSet<string> asmBackedCodeKeys = null;
		if (skipAsmBackedGml)
		{
			asmBackedCodeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				if (!string.IsNullOrEmpty(entry.Name) && TryGetCodeEntryInfo(Norm(entry.FullName), entry.Name, out string codeKey, out string extension) && extension.Equals(".asm", StringComparison.OrdinalIgnoreCase))
				{
					asmBackedCodeKeys.Add(codeKey);
				}
			}
		}
		foreach (ZipArchiveEntry entry2 in archive.Entries)
		{
			if (string.IsNullOrEmpty(entry2.Name))
			{
				continue;
			}
			string fullName = Norm(entry2.FullName);
			if ((!loadExactPayloads && (fullName.StartsWith("Xdelta/", StringComparison.OrdinalIgnoreCase) || fullName.StartsWith("Exact/", StringComparison.OrdinalIgnoreCase))) || (asmBackedCodeKeys != null && TryGetCodeEntryInfo(fullName, entry2.Name, out string earlyCodeKey, out string earlyExtension) && earlyExtension.Equals(".gml", StringComparison.OrdinalIgnoreCase) && asmBackedCodeKeys.Contains(earlyCodeKey)))
			{
				continue;
			}
			byte[] bytes;
			using (Stream stream = entry2.Open())
			{
				if (entry2.Length > 0)
				{
					bytes = new byte[entry2.Length];
					stream.ReadExactly(bytes);
				}
				else
				{
					using MemoryStream buf = new MemoryStream();
					stream.CopyTo(buf);
					bytes = buf.ToArray();
				}
			}
			if (fullName.Equals("g3mpatch.json", StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					pfs.Manifest = JsonSerializer.Deserialize<G3MPatchManifest>(bytes);
				}
				catch
				{
				}
				pfs._files[fullName] = bytes;
			}
			else if (fullName.StartsWith("CodeEntries/", StringComparison.OrdinalIgnoreCase))
			{
				if (TryGetCodeEntryInfo(fullName, entry2.Name, out string codeKey2, out string extension2))
				{
					string logicalName = Path.GetFileNameWithoutExtension(entry2.Name);
					string content = DecodeUtf8(bytes);
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
		bool hasHelpers = false;
		bool hasAssetOrder = false;
		foreach (string key in pfs._files.Keys)
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
		pfs.HelpersPrefix = (hasHelpers ? "Helpers" : (hasAssetOrder ? "AssetOrder" : "Helpers"));
		pfs.HydrateCodeEntryLogicalNamesFromHelpers();
		pfs.BuildDirectoryIndex();
		long totalBytes = ((IEnumerable<byte[]>)pfs._files.Values).Sum((Func<byte[], long>)((byte[] b) => b.Length));
		bool shouldAppend;
		LogInterpolatedStringHandler handler = new LogInterpolatedStringHandler(74, 6, out shouldAppend);
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
		string relativePath = fullName.Substring("CodeEntries/".Length);
		int extIndex = relativePath.LastIndexOf('.');
		if (extIndex <= 0)
		{
			return false;
		}
		string[] pathParts = relativePath.Substring(0, extIndex).Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (pathParts.Length < 2)
		{
			return false;
		}
		codeKey = (pathParts[^2].Equals(pathParts[^1], StringComparison.OrdinalIgnoreCase) ? pathParts[^2] : (pathParts[^2] + "/" + pathParts[^1]));
		return true;
	}

	private void BuildDirectoryIndex()
	{
		Dictionary<string, HashSet<string>> dirSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, HashSet<string>> fileSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (string path in _files.Keys)
		{
			int lastSlash = path.LastIndexOf('/');
			string fileParent = ((lastSlash < 0) ? "" : path.Substring(0, lastSlash));
			if (!fileSets.TryGetValue(fileParent, out var fileSet))
			{
				fileSet = (fileSets[fileParent] = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
			}
			fileSet.Add(path);
			int previousSlash = -1;
			for (int slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
			{
				string parent = ((previousSlash < 0) ? "" : path.Substring(0, previousSlash));
				string child = path.Substring(0, slash);
				if (!dirSets.TryGetValue(parent, out var dirSet))
				{
					dirSet = (dirSets[parent] = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
				}
				dirSet.Add(child);
				previousSlash = slash;
			}
		}
		string key;
		HashSet<string> value;
		foreach (KeyValuePair<string, HashSet<string>> item in dirSets)
		{
			item.Deconstruct(out key, out value);
			string parent2 = key;
			HashSet<string> children = value;
			_childDirs[parent2] = children.OrderBy<string, string>((string x) => x, StringComparer.OrdinalIgnoreCase).ToArray();
		}
		foreach (KeyValuePair<string, HashSet<string>> item2 in fileSets)
		{
			item2.Deconstruct(out key, out value);
			string parent3 = key;
			HashSet<string> files = value;
			_childFiles[parent3] = files.OrderBy<string, string>((string x) => x, StringComparer.OrdinalIgnoreCase).ToArray();
		}
	}

	private void HydrateCodeEntryLogicalNamesFromHelpers()
	{
		string helperPath = HelpersPrefix + "/variables_functions.json";
		if (!_files.TryGetValue(helperPath, out byte[] helperBytes) && !_files.TryGetValue(HelpersPrefix + "/code_patch_metadata.json", out helperBytes))
		{
			return;
		}
		try
		{
			using JsonDocument doc = JsonDocument.Parse(helperBytes);
			if (!doc.RootElement.TryGetProperty("codeEntries", out var codeEntries) || codeEntries.ValueKind != JsonValueKind.Array)
			{
				return;
			}
			foreach (JsonElement entry in codeEntries.EnumerateArray())
			{
				if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("key", out var keyElem) && entry.TryGetProperty("name", out var nameElem))
				{
					string key = keyElem.GetString();
					string name = nameElem.GetString();
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
		string n = Norm(path);
		if (!_files.TryGetValue(n, out byte[] bytes))
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
		string[] lines = ReadAllText(path).Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			lines[i] = lines[i].TrimEnd('\r');
		}
		return lines;
	}

	public bool FileExists(string path)
	{
		return _files.ContainsKey(Norm(path));
	}

	public string[] GetDirectories(string dirPath)
	{
		string n = Norm(dirPath);
		if (!_childDirs.TryGetValue(n, out string[] dirs))
		{
			return Array.Empty<string>();
		}
		return dirs;
	}

	public string[] GetFiles(string dirPath, string pattern = "*")
	{
		string n = Norm(dirPath);
		if (!_childFiles.TryGetValue(n, out string[] files))
		{
			return Array.Empty<string>();
		}
		if ((pattern == "*" || pattern == "*.*") ? true : false)
		{
			return files;
		}
		if (pattern.StartsWith("*."))
		{
			string ext = pattern.Substring(1);
			return files.Where((string f) => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).ToArray();
		}
		return files;
	}

	public bool DirectoryExists(string dirPath)
	{
		string n = Norm(dirPath);
		if (!_childDirs.ContainsKey(n))
		{
			return _childFiles.ContainsKey(n);
		}
		return true;
	}

	public void AddFile(string path, byte[] data)
	{
		string n = Norm(path);
		_files[n] = data;
	}

	public void AddTextFile(string path, string content)
	{
		AddFile(path, Encoding.UTF8.GetBytes(content));
	}

	public bool RemoveFile(string path)
	{
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
		AsmEntryPaths[entryKey] = (string.IsNullOrWhiteSpace(archivePath) ? $"CodeEntries/{entryKey}/{entryKey}.asm" : Norm(archivePath));
		CodeEntryLogicalNames[entryKey] = logicalName ?? CodeEntryLogicalNames.GetValueOrDefault(entryKey) ?? Path.GetFileName(entryKey);
		RemoveFile($"CodeEntries/{entryKey}/{entryKey}.asm");
	}

	public bool RemoveGmlEntry(string entryKey)
	{
		bool result = GmlEntries.Remove(entryKey);
		if (!AsmEntries.ContainsKey(entryKey))
		{
			CodeEntryLogicalNames.Remove(entryKey);
		}
		return result;
	}

	public bool RemoveAsmEntry(string entryKey)
	{
		AsmEntryPaths.Remove(entryKey);
		bool result = AsmEntries.Remove(entryKey);
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
		string outputDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
		{
			Directory.CreateDirectory(outputDir);
		}
		using FileStream zipStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, FileOptions.SequentialScan);
		using ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
		if (manifest != null)
		{
			using Stream stream = archive.CreateEntry("g3mpatch.json", ArchiveCompressionUtil.GetLevel("g3mpatch.json")).Open();
			JsonSerializer.Serialize(stream, manifest, s_jsonOptions);
		}
		string key;
		foreach (KeyValuePair<string, byte[]> file in _files)
		{
			file.Deconstruct(out key, out var value);
			string path = key;
			byte[] data = value;
			if ((!path.Equals("g3mpatch.json", StringComparison.OrdinalIgnoreCase) || manifest == null) && !path.StartsWith("CodeEntries/", StringComparison.OrdinalIgnoreCase))
			{
				using Stream stream2 = archive.CreateEntry(path, ArchiveCompressionUtil.GetLevel(path)).Open();
				stream2.Write(data, 0, data.Length);
			}
		}
		string value2;
		foreach (KeyValuePair<string, string> gmlEntry in GmlEntries)
		{
			gmlEntry.Deconstruct(out key, out value2);
			string entryKey = key;
			string gml = value2;
			string entryPath = $"CodeEntries/{entryKey}/{entryKey}.gml";
			using StreamWriter writer = new StreamWriter(archive.CreateEntry(entryPath, ArchiveCompressionUtil.GetLevel(entryPath)).Open(), Encoding.UTF8);
			writer.Write(gml);
		}
		foreach (KeyValuePair<string, string> asmEntry in AsmEntries)
		{
			asmEntry.Deconstruct(out value2, out key);
			string entryKey2 = value2;
			string asm = key;
			string entryPath2 = AsmEntryPaths.GetValueOrDefault(entryKey2) ?? $"CodeEntries/{entryKey2}/{entryKey2}.asm";
			using StreamWriter writer2 = new StreamWriter(archive.CreateEntry(entryPath2, ArchiveCompressionUtil.GetLevel(entryPath2)).Open(), Encoding.UTF8);
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
		HashSet<string> types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (_childDirs.TryGetValue("", out string[] rootDirs))
		{
			string[] array = rootDirs;
			foreach (string dir in array)
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
