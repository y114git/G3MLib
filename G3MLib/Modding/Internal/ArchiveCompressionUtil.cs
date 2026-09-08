using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace G3MLib.Modding.Internal;

internal static class ArchiveCompressionUtil
{
	private static readonly HashSet<string> s_uncompressedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".webp", ".gif", ".dds", ".qoi", ".bz2", ".mp3", ".ogg",
		".wav", ".flac", ".bin", ".dat", ".xdelta"
	};

	public static CompressionLevel GetLevel(string entryPath)
	{
		if (string.IsNullOrWhiteSpace(entryPath))
		{
			return CompressionLevel.Optimal;
		}
		string extension = Path.GetExtension(entryPath);
		if (s_uncompressedExtensions.Contains(extension))
		{
			return CompressionLevel.NoCompression;
		}
		return CompressionLevel.Optimal;
	}
}
