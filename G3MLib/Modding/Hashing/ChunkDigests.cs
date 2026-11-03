using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace G3MLib.Modding.Hashing;

/// <summary>
/// Hashes the chunks of a data file without reading its model, and compares two such lists.
/// </summary>
public static class ChunkDigests
{
	private const int BufferSize = 81920;
	private const int HeaderSize = 8;

	/// <summary>
	/// Walks the chunk headers inside the FORM chunk of a data file and hashes the body of each chunk.
	/// Offsets are recorded relative to the start of the FORM chunk (where FORM begins is offset 0).
	/// </summary>
	/// <param name="dataFile">A readable, seekable stream positioned at the start of the data file. Its position is not restored.</param>
	/// <param name="algorithm">Hash algorithm. Default: SHA-256.</param>
	/// <param name="token">Checked between chunks and while a chunk body is read.</param>
	/// <returns>One digest per chunk, in file order.</returns>
	/// <exception cref="IOException">The stream is not a data file, or a chunk reaches past the end of its parent.</exception>
	public static IReadOnlyList<ChunkDigest> Compute(Stream dataFile, HashAlgorithmName? algorithm = null, CancellationToken token = default)
	{
		ArgumentNullException.ThrowIfNull(dataFile);
		if (!dataFile.CanRead || !dataFile.CanSeek)
			throw new ArgumentException("The stream must be readable and seekable.", nameof(dataFile));

		using var hash = IncrementalHash.CreateHash(algorithm ?? HashAlgorithmName.SHA256);
		Span<byte> header = stackalloc byte[HeaderSize];
		var formStart = dataFile.Position;
		if (dataFile.Length - formStart < HeaderSize)
			throw new IOException("The data file is too short to hold a FORM chunk.");
		dataFile.ReadExactly(header);
		var rootName = Encoding.Latin1.GetString(header[..4]);
		if (rootName != "FORM")
			throw new IOException($"Root chunk is \"{rootName}\", not FORM");
		var rootLength = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
		if (rootLength > dataFile.Length - dataFile.Position)
			throw new IOException("Root chunk exceeds the available input.");

		var formEnd = dataFile.Position + rootLength;
		List<ChunkDigest> digests = [];
		var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
		try
		{
			while (dataFile.Position < formEnd)
			{
				token.ThrowIfCancellationRequested();
				if (formEnd - dataFile.Position < HeaderSize)
					throw new IOException("FORM ends in an incomplete chunk header.");

				var offset = dataFile.Position - formStart;
				dataFile.ReadExactly(header);
				var name = Encoding.Latin1.GetString(header[..4]);
				var length = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
				if (length > formEnd - dataFile.Position)
					throw new IOException($"Chunk \"{name}\" exceeds FORM bounds.");

				var remaining = (long)length;
				while (remaining > 0)
				{
					token.ThrowIfCancellationRequested();
					var read = dataFile.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
					if (read == 0)
						throw new EndOfStreamException($"Chunk \"{name}\" ends after the end of the stream.");
					hash.AppendData(buffer, 0, read);
					remaining -= read;
				}
				digests.Add(new ChunkDigest(name, offset, length, Convert.ToHexStringLower(hash.GetHashAndReset())));
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
		return digests;
	}

	/// <summary>
	/// Compares two digest lists made with the same algorithm.
	/// </summary>
	/// <returns>
	/// The names of the chunks that are only in <paramref name="b"/> (added), only in <paramref name="a"/> (removed), or in both with a different hash (changed).
	/// Names found in <paramref name="b"/> come first, in file order, then the removed names in the order of <paramref name="a"/>. Each name is listed once.
	/// </returns>
	public static IReadOnlyList<string> Compare(IEnumerable<ChunkDigest> a, IEnumerable<ChunkDigest> b)
	{
		ArgumentNullException.ThrowIfNull(a);
		ArgumentNullException.ThrowIfNull(b);

		var before = a.ToLookup(digest => digest.Name, StringComparer.Ordinal);
		var after = b.ToLookup(digest => digest.Name, StringComparer.Ordinal);
		List<string> differences = [];
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (var digest in b)
		{
			if (seen.Add(digest.Name))
			{
				if (!before.Contains(digest.Name) || !HashesMatch(before[digest.Name].Select(d => d.Hash), after[digest.Name].Select(d => d.Hash)))
				{
					differences.Add(digest.Name);
				}
			}
		}
		foreach (var digest in a)
		{
			if (!after.Contains(digest.Name) && seen.Add(digest.Name))
			{
				differences.Add(digest.Name);
			}
		}
		return differences;
	}

	private static bool HashesMatch(IEnumerable<string> first, IEnumerable<string> second)
	{
		var counts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var hash in first)
		{
			counts[hash] = counts.GetValueOrDefault(hash) + 1;
		}
		foreach (var hash in second)
		{
			if (!counts.TryGetValue(hash, out var count) || count == 0)
			{
				return false;
			}
			counts[hash] = count - 1;
		}
		return counts.Values.All(c => c == 0);
	}
}
