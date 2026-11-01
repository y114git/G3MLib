using System.IO;
using System.Text;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Modding.Resources;

/// <summary>
/// An audio group file (audiogroupN.dat). Only the AUDO entry table is read on open; entry bytes are read when asked for.
/// Other chunks are copied unchanged on save. Not thread safe per instance.
/// </summary>
public sealed class AudioGroupFile : IDisposable
{
	private sealed class Entry
	{
		public long Offset;
		public int Length;
		public ReadOnlyMemory<byte>? Data;
	}

	private sealed record Chunk(string Name, long Offset, int Length);

	private FileStream _stream;
	private readonly string _path;
	private List<Chunk> _chunks = [];
	private List<Entry> _entries = [];
	private byte[] _trailing = [];
	private bool _padLastEntry = true;

	private AudioGroupFile(string path, FileStream stream)
	{
		_path = Path.GetFullPath(path);
		_stream = stream;
	}

	public int Count => _entries.Count;

	public static AudioGroupFile Open(string path, CancellationToken cancellationToken = default)
	{
		var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		var file = new AudioGroupFile(path, stream);
		try
		{
			file.ReadTable(cancellationToken);
			return file;
		}
		catch
		{
			stream.Dispose();
			throw;
		}
	}

	private void ReadTable(CancellationToken cancellationToken)
	{
		_chunks = [];
		_entries = [];
		using var reader = new BinaryReader(_stream, Encoding.ASCII, leaveOpen: true);
		_stream.Position = 0;
		if (_stream.Length < 8 || new string(reader.ReadChars(4)) != "FORM")
			throw new InvalidDataException("Not an audio group file: missing FORM header.");
		var end = Math.Min(_stream.Length, 8L + reader.ReadUInt32());
		while (_stream.Position + 8 <= end)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var name = new string(reader.ReadChars(4));
			var length = checked((int)reader.ReadUInt32());
			var start = _stream.Position;
			if (start + length > end)
				throw new InvalidDataException($"Chunk {name} exceeds the file.");
			_chunks.Add(new Chunk(name, start, length));
			if (name == "AUDO")
			{
				var count = reader.ReadUInt32();
				if ((long)count * 4 > length - 4)
					throw new InvalidDataException("The AUDO pointer table exceeds the chunk.");
				var pointers = new long[count];
				for (var i = 0; i < count; i++)
					pointers[i] = reader.ReadUInt32();
				foreach (var pointer in pointers)
				{
					if (pointer < start || pointer + 4 > start + length)
						throw new InvalidDataException("An AUDO entry lies outside its chunk.");
					_stream.Position = pointer;
					var size = checked((int)reader.ReadUInt32());
					if (pointer + 4 + size > start + length)
						throw new InvalidDataException("An AUDO entry exceeds its chunk.");
					_entries.Add(new Entry { Offset = pointer + 4, Length = size });
				}
				// The last entry of the file's last chunk may come without its padding; keep what the file does.
				if (_entries.Count > 0)
				{
					var lastEnd = _entries[^1].Offset + _entries[^1].Length;
					_padLastEntry = start + length >= ((lastEnd + 3) & ~3L);
				}
			}
			_stream.Position = start + length;
		}
		_trailing = new byte[_stream.Length - end];
		_stream.Position = end;
		_stream.ReadExactly(_trailing);
		if (!_chunks.Exists(chunk => chunk.Name == "AUDO"))
			throw new InvalidDataException("The file has no AUDO chunk.");
	}

	private Entry Get(int id)
	{
		if (id < 0 || id >= _entries.Count)
			throw new ArgumentOutOfRangeException(nameof(id));
		return _entries[id];
	}

	public ReadOnlyMemory<byte> GetEntry(int id)
	{
		var entry = Get(id);
		if (entry.Data is { } data)
			return data;
		var bytes = new byte[entry.Length];
		_stream.Position = entry.Offset;
		_stream.ReadExactly(bytes);
		return bytes;
	}

	public int Add(ReadOnlyMemory<byte> data)
	{
		_entries.Add(new Entry { Data = data, Length = data.Length });
		return _entries.Count - 1;
	}

	public void Replace(int id, ReadOnlyMemory<byte> data)
	{
		var entry = Get(id);
		entry.Data = data;
		entry.Length = data.Length;
	}

	/// <summary>Empties the entry. Ids stay as they are until <see cref="Compact"/>.</summary>
	public void Remove(int id) => Replace(id, ReadOnlyMemory<byte>.Empty);

	/// <summary>Drops empty entries and renumbers the rest. Returns old id to new id for every kept entry.</summary>
	public IReadOnlyDictionary<int, int> Compact()
	{
		Dictionary<int, int> map = [];
		List<Entry> kept = [];
		for (var i = 0; i < _entries.Count; i++)
		{
			if (_entries[i].Length == 0)
				continue;
			map[i] = kept.Count;
			kept.Add(_entries[i]);
		}
		_entries = kept;
		return map;
	}

	/// <summary>Writes the file to a temporary file and moves it over <paramref name="path"/>. With <paramref name="backup"/> an existing file is kept as path.bak.</summary>
	public void Save(string path, bool backup, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
	{
		var temp = path + ".g3mtmp";
		try
		{
			using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write))
			using (var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true))
			{
				writer.Write("FORM"u8);
				writer.Write(0u);
				for (var i = 0; i < _chunks.Count; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var chunk = _chunks[i];
					writer.Write(Encoding.ASCII.GetBytes(chunk.Name));
					writer.Write(0u);
					var start = output.Position;
					if (chunk.Name == "AUDO")
						WriteAudo(output, writer, progress, cancellationToken);
					else
						CopyRaw(output, chunk);
					var finish = output.Position;
					output.Position = start - 4;
					writer.Write(checked((uint)(finish - start)));
					output.Position = finish;
				}
				output.Position = 4;
				writer.Write(checked((uint)(output.Length - 8)));
				output.Position = output.Length;
				output.Write(_trailing);
			}
			var inPlace = string.Equals(Path.GetFullPath(path), _path, StringComparison.OrdinalIgnoreCase);
			if (inPlace)
				_stream.Dispose();
			if (backup && File.Exists(path))
				File.Copy(path, path + ".bak", overwrite: true);
			File.Move(temp, path, overwrite: true);
			if (inPlace)
			{
				_stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				ReadTable(cancellationToken);
			}
		}
		catch
		{
			if (File.Exists(temp))
				File.Delete(temp);
			throw;
		}
	}

	private void CopyRaw(FileStream output, Chunk chunk)
	{
		var bytes = new byte[chunk.Length];
		_stream.Position = chunk.Offset;
		_stream.ReadExactly(bytes);
		output.Write(bytes);
	}

	private void WriteAudo(FileStream output, BinaryWriter writer, IProgress<double>? progress, CancellationToken cancellationToken)
	{
		var table = output.Position;
		writer.Write((uint)_entries.Count);
		writer.Write(new byte[_entries.Count * 4]);
		for (var i = 0; i < _entries.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var entryStart = output.Position;
			var data = GetEntry(i);
			writer.Write((uint)data.Length);
			output.Write(data.Span);
			while (output.Position % 4 != 0 && (i < _entries.Count - 1 || _padLastEntry))
				output.WriteByte(0);
			var after = output.Position;
			output.Position = table + 4 + i * 4;
			writer.Write(checked((uint)entryStart));
			output.Position = after;
			progress?.Report((i + 1d) / _entries.Count);
		}
	}

	public void Dispose() => _stream.Dispose();
}

/// <summary>Rewrites the audio ids of sounds after <see cref="AudioGroupFile.Compact"/>.</summary>
public static class SoundIdRemap
{
	/// <summary>Sounds of the group whose id is in the map get the new id; sounds whose entry was dropped get -1.</summary>
	public static void Apply(GameMakerData data, int groupId, IReadOnlyDictionary<int, int> map)
	{
		ArgumentNullException.ThrowIfNull(data);
		ArgumentNullException.ThrowIfNull(map);
		if (data.Sounds == null)
			return;
		foreach (var sound in data.Sounds)
		{
			if (sound == null || sound.GroupID != groupId || sound.AudioID < 0)
				continue;
			sound.AudioID = map.TryGetValue(sound.AudioID, out var next) ? next : -1;
		}
	}
}

public enum AudioSource
{
	None,
	Embedded,
	GroupFile
}

/// <summary>Finds the bytes of sounds, opening each audio group file once. Safe to call from several threads.</summary>
public sealed class AudioGroupReader : IDisposable
{
	private readonly GameMakerData _data;
	private readonly string _dataDirectory;
	private readonly Dictionary<int, AudioGroupFile?> _files = [];

	public AudioGroupReader(GameMakerData data, string dataFilePath)
	{
		_data = data;
		_dataDirectory = Path.GetDirectoryName(dataFilePath) ?? string.Empty;
	}

	public ReadOnlyMemory<byte>? GetAudio(GameMakerSound sound, out AudioSource source)
	{
		ArgumentNullException.ThrowIfNull(sound);
		var embedded = sound.AudioFile?.Data;
		var hasEmbedded = embedded is { Length: > 0 };
		if (hasEmbedded && (sound.GroupID > 0 || sound.AudioID < 0))
		{
			source = AudioSource.Embedded;
			return embedded;
		}
		source = AudioSource.None;
		if (sound.GroupID <= 0 || _data.AudioGroups == null || sound.GroupID >= _data.AudioGroups.Count || sound.AudioID < 0)
			return null;
		var file = GetFile(sound.GroupID);
		if (file == null)
			return null;
		lock (file)
		{
			if (sound.AudioID >= file.Count)
				return null;
			var bytes = file.GetEntry(sound.AudioID);
			if (bytes.Length == 0)
				return null;
			source = AudioSource.GroupFile;
			return bytes;
		}
	}

	private AudioGroupFile? GetFile(int groupId)
	{
		lock (_files)
		{
			if (_files.TryGetValue(groupId, out var cached))
				return cached;
			var path = ResourceAssetUtil.ResolveAudioGroupPath(_data, _dataDirectory, groupId);
			AudioGroupFile? file = null;
			if (path != null && File.Exists(path))
			{
				try
				{
					file = AudioGroupFile.Open(path);
				}
				catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
				{
					file = null;
				}
			}
			_files[groupId] = file;
			return file;
		}
	}

	public void Dispose()
	{
		foreach (var file in _files.Values)
			file?.Dispose();
		_files.Clear();
	}
}
